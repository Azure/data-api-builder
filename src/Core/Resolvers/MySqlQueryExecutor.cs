// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Net;
using Azure.Core;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Specialized QueryExecutor for MySql mainly providing methods to
    /// handle connecting to the database with a managed identity.
    /// /// </summary>
    public class MySqlQueryExecutor : QueryExecutor<MySqlConnection>
    {
        // This is the same scope for any Azure SQL database that is
        // required to request a default azure credential access token
        // for a managed identity.
        public const string DATABASE_SCOPE = @"https://ossrdbms-aad.database.windows.net/.default";

        /// <summary>
        /// The managed identity Access Token string obtained
        /// from the configuration controller.
        /// Key: datasource name, Value: access token for this datasource.
        /// </summary>
        private Dictionary<string, string?> _accessTokensFromConfiguration;

        public DefaultAzureCredential AzureCredential { get; set; } = new(); // CodeQL [SM05137] DefaultAzureCredential will use Managed Identity if available or fallback to default.

        /// <summary>
        /// The MySql specific connection string builders.
        /// Key: datasource name, Value: connection string builder for this datasource.
        /// </summary>
        public override IDictionary<string, DbConnectionStringBuilder> ConnectionStringBuilders
            => base.ConnectionStringBuilders;

        /// <summary>
        /// The saved cached access token obtained from DefaultAzureCredentials
        /// representing a managed identity. 
        /// </summary>
        private AccessToken? _defaultAccessToken;

        /// <summary>
        /// DatasourceName to boolean value indicating if access token should be set for db.
        /// </summary>
        private Dictionary<string, bool> _dataSourceAccessTokenUsage;

        private readonly RuntimeConfigProvider _runtimeConfigProvider;

        public MySqlQueryExecutor(
            RuntimeConfigProvider runtimeConfigProvider,
            DbExceptionParser dbExceptionParser,
            ILogger<IQueryExecutor> logger,
            IHttpContextAccessor httpContextAccessor,
            HotReloadEventHandler<HotReloadEventArgs>? handler = null)
            : base(dbExceptionParser,
                  logger,
                  runtimeConfigProvider,
                  httpContextAccessor,
                  handler)
        {
            _dataSourceAccessTokenUsage = new Dictionary<string, bool>();
            _accessTokensFromConfiguration = runtimeConfigProvider.ManagedIdentityAccessToken;
            _runtimeConfigProvider = runtimeConfigProvider;
            ConfigureMySqlQueryExecutor();
        }

        /// <summary>
        /// Configure during construction or a hot-reload scenario.
        /// </summary>
        private void ConfigureMySqlQueryExecutor()
        {
            IEnumerable<KeyValuePair<string, DataSource>> mysqldbs = _runtimeConfigProvider.GetConfig().GetDataSourceNamesToDataSourcesIterator().Where(x => x.Value.DatabaseType == DatabaseType.MySQL);

            foreach ((string dataSourceName, DataSource dataSource) in mysqldbs)
            {

                MySqlConnectionStringBuilder builder = new(dataSource.ConnectionString)
                {
                    // Force always allow user variables;
                    AllowUserVariables = true
                };

                if (_runtimeConfigProvider.IsLateConfigured)
                {
                    builder.SslMode = MySqlSslMode.VerifyFull;
                }

                ConnectionStringBuilders.TryAdd(dataSourceName, builder);
                _dataSourceAccessTokenUsage[dataSourceName] = ShouldManagedIdentityAccessBeAttempted(builder);
            }
        }

        /// <summary>
        /// Modifies the properties of the supplied connection to support managed identity access.
        /// In the case of MySql, gets access token if deemed necessary and sets it on the connection.
        /// The supplied connection is assumed to already have the same connection string
        /// provided in the runtime configuration.
        /// </summary>
        /// <param name="conn">The supplied connection to modify for managed identity access.</param>
        /// <param name="dataSourceName">Name of datasource for which to set access token. Default dbName taken from config if null</param>
        public override async Task SetManagedIdentityAccessTokenIfAnyAsync(DbConnection conn, string dataSourceName)
        {
            // using default datasource name for first db - maintaining backward compatibility for single db scenario.
            if (string.IsNullOrEmpty(dataSourceName))
            {
                dataSourceName = ConfigProvider.GetConfig().DefaultDataSourceName;
            }

            _dataSourceAccessTokenUsage.TryGetValue(dataSourceName, out bool setAccessToken);

            // Only attempt to get the access token if the connection string is in the appropriate format
            if (setAccessToken)
            {
                // If the configuration controller provided a managed identity access token use that,
                // else use the default saved access token if still valid.
                // Get a new token only if the saved token is null or expired.
                _accessTokensFromConfiguration.TryGetValue(dataSourceName, out string? accessTokenFromController);
                string? accessToken = accessTokenFromController ??
                    (IsDefaultAccessTokenValid() ?
                        ((AccessToken)_defaultAccessToken!).Token :
                        await GetAccessTokenAsync());

                if (accessToken is not null)
                {
                    MySqlConnectionStringBuilder connstr = new(conn.ConnectionString)
                    {
                        Password = accessToken
                    };
                    conn.ConnectionString = connstr.ConnectionString;
                }
            }
        }

        /// <summary>
        /// Determines if managed identity access should be attempted or not.
        /// It should only be attempted,
        /// 1. If none of UserID, Password or Authentication
        /// method are specified in the connection string since they have higher precedence
        /// and any attempt to use an access token in their presence would lead to
        /// a System.InvalidOperationException.
        /// 2. It is NOT a Windows Integrated Security scenario.
        /// </summary>
        private static bool ShouldManagedIdentityAccessBeAttempted(MySqlConnectionStringBuilder builder)
        {
            return !string.IsNullOrEmpty(builder.UserID) &&
                string.IsNullOrEmpty(builder.Password);
        }

        /// <summary>
        /// Determines if the saved default azure credential's access token is valid and not expired.
        /// </summary>
        /// <returns>True if valid, false otherwise.</returns>
        private bool IsDefaultAccessTokenValid()
        {
            return _defaultAccessToken is not null && ((AccessToken)_defaultAccessToken).ExpiresOn.CompareTo(DateTimeOffset.Now) > 0;
        }

        /// <summary>
        /// Tries to get an access token using DefaultAzureCredentials.
        /// Catches any CredentialUnavailableException and logs only a warning
        /// since this is best effort.
        /// </summary>
        /// <returns>The string representation of the access token if found,
        /// null otherwise.</returns>
        private async Task<string?> GetAccessTokenAsync()
        {
            try
            {
                _defaultAccessToken = await AzureCredential.GetTokenAsync(new TokenRequestContext(new[] { DATABASE_SCOPE }));
            }
            catch (CredentialUnavailableException ex)
            {
                string correlationId = HttpContextExtensions.GetLoggerCorrelationId(HttpContextAccessor.HttpContext);
                QueryExecutorLogger.LogWarning(
                    exception: ex,
                    message: "{correlationId} Failed to retrieve a managed identity access token using DefaultAzureCredential.",
                    correlationId);
            }

            return _defaultAccessToken?.Token;
        }

        /// <summary>
        /// Interprets the result sets produced by an upsert (PUT/PATCH) query built by
        /// <see cref="MySqlQueryBuilder.Build(SqlUpsertQueryStructure)"/> to determine whether the
        /// operation resulted in an update or an insert, and to surface database policy failures.
        /// The upsert query returns:
        ///   result set #1: the number of records already present for the given primary key.
        ///   result set #2: the output of the UPDATE (non-empty when a row matched the primary key and the update policy).
        ///   result set #3 (non-fallback only): the output of the INSERT (non-empty only when a record was inserted).
        /// </summary>
        /// <param name="dbDataReader">A DbDataReader.</param>
        /// <param name="args">The arguments to this handler - args[0] = primary key in pretty format, args[1] = entity name.</param>
        public override async Task<DbResultSet> GetMultipleResultSetsIfAnyAsync(
            DbDataReader dbDataReader, List<string>? args = null)
        {
            // Result set #1: count (0/1) of records already present for the given primary key.
            DbResultSet countResultSet = await ExtractResultSetFromDbDataReaderAsync(dbDataReader);
            DbResultSetRow? countResultSetRow = countResultSet.Rows.FirstOrDefault();
            int numOfRecordsWithGivenPK;

            if (countResultSetRow is not null &&
                countResultSetRow.Columns.TryGetValue(MySqlQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK, out object? rowsWithGivenPK) &&
                rowsWithGivenPK is not null)
            {
                numOfRecordsWithGivenPK = Convert.ToInt32(rowsWithGivenPK);
            }
            else
            {
                throw new DataApiBuilderException(
                    message: "Neither insert nor update could be performed.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            // Result set #2: output of the UPDATE. Non-empty whenever a row matched the primary key and
            // satisfied the update database policy - independent of whether any value physically changed,
            // so authorized idempotent updates are still returned.
            DbResultSet? updateResultSet = await dbDataReader.NextResultAsync()
                ? await ExtractResultSetFromDbDataReaderAsync(dbDataReader)
                : null;

            if (numOfRecordsWithGivenPK == 1)
            {
                // A record existed for the given primary key, so an update was attempted.
                if (updateResultSet is null || updateResultSet.Rows.Count == 0)
                {
                    // Record exists but no row matched the primary key + update policy - indicates the
                    // update database policy was not satisfied (e.g. an attempt to modify another user's row).
                    throw new DataApiBuilderException(
                        message: DataApiBuilderException.AUTHORIZATION_FAILURE,
                        statusCode: HttpStatusCode.Forbidden,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure);
                }

                // Identifies this as the result set of an update operation (used to return HTTP 200
                // instead of 201 and to omit the location header).
                updateResultSet.ResultProperties.Add(SqlMutationEngine.IS_UPDATE_RESULT_SET, true);
                return updateResultSet;
            }

            // No record existed for the given primary key, so an insert was attempted. The insert output
            // is in result set #3. For the update-only (fallback) path there is no insert result set.
            DbResultSet? insertResultSet = await dbDataReader.NextResultAsync()
                ? await ExtractResultSetFromDbDataReaderAsync(dbDataReader)
                : null;

            if (insertResultSet is null)
            {
                // Update-only path (e.g. autogenerated primary key) and no record was found to update.
                if (args is not null && args.Count > 1)
                {
                    string prettyPrintPk = args[0];
                    string entityName = args[1];

                    throw new DataApiBuilderException(
                        message: $"Cannot perform INSERT and could not find {entityName} " +
                            $"with primary key {prettyPrintPk} to perform UPDATE on.",
                        statusCode: HttpStatusCode.NotFound,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.ItemNotFound);
                }

                throw new DataApiBuilderException(
                    message: "Neither insert nor update could be performed.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            if (insertResultSet.Rows.Count == 0)
            {
                // No record existed but nothing was inserted - indicates the create database policy
                // was not satisfied.
                throw new DataApiBuilderException(
                    message: DataApiBuilderException.AUTHORIZATION_FAILURE,
                    statusCode: HttpStatusCode.Forbidden,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure);
            }

            return insertResultSet;
        }
    }
}
