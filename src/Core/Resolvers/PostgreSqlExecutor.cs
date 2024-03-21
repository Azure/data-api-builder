// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using Azure.Core;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Specialized QueryExecutor for PostgreSql mainly providing methods to
    /// handle connecting to the database with a managed identity.
    /// for more info: https://learn.microsoft.com/EN-us/azure/postgresql/single-server/how-to-connect-with-managed-identity
    /// </summary>
    public class PostgreSqlQueryExecutor : QueryExecutor<NpgsqlConnection>
    {
        // This is the same scope for any Azure Database for PostgreSQL that is
        // required to request a default azure credential access token
        // for a managed identity.
        public const string DATABASE_SCOPE = @"https://ossrdbms-aad.database.windows.net/.default";

        /// <summary>
        /// The managed identity Access Token string obtained
        /// from the configuration controller.
        /// Key: datasource name, Value: access token for this datasource.
        /// </summary>
        private readonly Dictionary<string, string?> _accessTokensFromConfiguration;

        public DefaultAzureCredential AzureCredential { get; set; } = new();

        /// <summary>
        /// The PostgreSql specific connection string builders.
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

        public PostgreSqlQueryExecutor(
            RuntimeConfigProvider runtimeConfigProvider,
            DbExceptionParser dbExceptionParser,
            ILogger<IQueryExecutor> logger,
            IHttpContextAccessor httpContextAccessor)
            : base(dbExceptionParser,
                  logger,
                  runtimeConfigProvider,
                  httpContextAccessor)
        {
            IEnumerable<KeyValuePair<string, DataSource>> postgresqldbs = runtimeConfigProvider.GetConfig().GetDataSourceNamesToDataSourcesIterator().Where(x => x.Value.DatabaseType == DatabaseType.PostgreSQL);
            _dataSourceAccessTokenUsage = new Dictionary<string, bool>();
            _accessTokensFromConfiguration = runtimeConfigProvider.ManagedIdentityAccessToken;

            foreach ((string dataSourceName, DataSource dataSource) in postgresqldbs)
            {
                NpgsqlConnectionStringBuilder builder = new(dataSource.ConnectionString);

                if (runtimeConfigProvider.IsLateConfigured)
                {
                    builder.SslMode = SslMode.VerifyFull;
                }

                ConnectionStringBuilders.TryAdd(dataSourceName, builder);
                MsSqlOptions? msSqlOptions = dataSource.GetTypedOptions<MsSqlOptions>();
                _dataSourceAccessTokenUsage[dataSourceName] = ShouldManagedIdentityAccessBeAttempted(builder);
            }
        }

        /// <summary>
        /// Modifies the properties of the supplied connection string to support managed identity access.
        /// In the case of Postgres, if a default managed identity needs to be used, the password in the
        /// connection needs to be replaced with the default access token.
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
                NpgsqlConnection sqlConn = (NpgsqlConnection)conn;

                // If the configuration controller provided a managed identity access token use that,
                // else use the default saved access token if still valid.
                // Get a new token only if the saved token is null or expired.
                _accessTokensFromConfiguration.TryGetValue(dataSourceName, out string? accessTokenFromController);
                string? accessToken = accessTokenFromController ??
                    (IsDefaultAccessTokenValid() ?
                        ((AccessToken)_defaultAccessToken!).Token :
                        await GetAccessTokenAsync(dataSourceName));

                if (accessToken is not null)
                {
                    NpgsqlConnectionStringBuilder newConnectionString = new(sqlConn.ConnectionString)
                    {
                        Password = accessToken
                    };
                    sqlConn.ConnectionString = newConnectionString.ToString();
                }
            }
        }

        /// <summary>
        /// Determines if managed identity access should be attempted or not.
        /// It should only be attempted if the password is not provided
        /// </summary>
        private static bool ShouldManagedIdentityAccessBeAttempted(NpgsqlConnectionStringBuilder builder)
        {
            return string.IsNullOrEmpty(builder.Password);
        }

        /// <summary>
        /// Determines if the saved default azure credential's access token is valid and not expired.
        /// </summary>
        /// <returns>True if valid, false otherwise.</returns>
        private bool IsDefaultAccessTokenValid()
        {
            return _defaultAccessToken is not null &&
                ((AccessToken)_defaultAccessToken).ExpiresOn.CompareTo(DateTimeOffset.Now) > 0;
        }

        /// <summary>
        /// Tries to get an access token using DefaultAzureCredentials.
        /// Catches any CredentialUnavailableException and logs only a warning
        /// since this is best effort.
        /// </summary>
        /// <returns>The string representation of the access token if found,
        /// null otherwise.</returns>
        private async Task<string?> GetAccessTokenAsync(string dataSourceName)
        {
            bool firstAttemptAtDefaultAccessToken = _defaultAccessToken is null;

            try
            {
                _defaultAccessToken =
                    await AzureCredential.GetTokenAsync(
                        new TokenRequestContext(new[] { DATABASE_SCOPE }));
            }
            // because there can be scenarios where password is not specified but
            // default managed identity is not the intended method of authentication
            // so a bunch of different exceptions could occur in that scenario
            catch (Exception ex)
            {
                string messagePrefix = "{correlationId} No password detected in the connection string. Attempt to retrieve a managed identity access token using DefaultAzureCredential failed due to:\n{errorMessage}";
                string messageSuffix = (firstAttemptAtDefaultAccessToken ? $"If authentication with DefaultAzureCrendential is not intended, this warning can be safely ignored." : string.Empty);
                string message = messagePrefix + messageSuffix;
                QueryExecutorLogger.LogWarning(
                    exception: ex,
                    message: message,
                    HttpContextExtensions.GetLoggerCorrelationId(HttpContextAccessor.HttpContext),
                    ex.Message);

                // the config doesn't contain an identity token
                // and a default identity token cannot be obtained
                // so the application should not attempt to set the token
                // for future conntions
                // note though that if a default access token has been previously
                // obtained successfully (firstAttemptAtDefaultAccessToken == false)
                // this might be a transitory failure don't disable attempts to set
                // the token
                //
                // disabling the attempts is useful in scenarios where the user
                // has a valid connection string without a password in it
                if (firstAttemptAtDefaultAccessToken)
                {
                    _dataSourceAccessTokenUsage[dataSourceName] = false;
                }
            }

            return _defaultAccessToken?.Token;
        }
    }
}
