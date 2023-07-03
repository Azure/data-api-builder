// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using System.Net;
using System.Security.Claims;
using System.Text;
using Azure.Core;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.Identity;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Specialized QueryExecutor for MsSql mainly providing methods to
    /// handle connecting to the database with a managed identity.
    /// /// </summary>
    public class MsSqlQueryExecutor : QueryExecutor<SqlConnection>
    {
        // This is the same scope for any Azure SQL database that is
        // required to request a default azure credential access token
        // for a managed identity.
        public const string DATABASE_SCOPE = @"https://database.windows.net/.default";

        /// <summary>
        /// The managed identity Access Token string obtained
        /// from the configuration controller.
        /// </summary>
        private readonly string? _accessTokenFromController;

        /// <summary>
        /// The MsSql specific connection string builder.
        /// </summary>
        public override SqlConnectionStringBuilder ConnectionStringBuilder
            => (SqlConnectionStringBuilder)base.ConnectionStringBuilder;

        public DefaultAzureCredential AzureCredential { get; set; } = new();

        /// <summary>
        /// The saved cached access token obtained from DefaultAzureCredentials
        /// representing a managed identity. 
        /// </summary>
        private AccessToken? _defaultAccessToken;

        private bool _attemptToSetAccessToken;

        private bool _isSessionContextEnabled;

        public MsSqlQueryExecutor(
            RuntimeConfigProvider runtimeConfigProvider,
            DbExceptionParser dbExceptionParser,
            ILogger<IQueryExecutor> logger,
            IHttpContextAccessor httpContextAccessor)
            : base(dbExceptionParser,
                  logger,
                  new SqlConnectionStringBuilder(runtimeConfigProvider.GetConfig().DataSource.ConnectionString),
                  runtimeConfigProvider,
                  httpContextAccessor)
        {
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();

            if (runtimeConfigProvider.IsLateConfigured)
            {
                ConnectionStringBuilder.Encrypt = SqlConnectionEncryptOption.Mandatory;
                ConnectionStringBuilder.TrustServerCertificate = false;
            }

            MsSqlOptions? msSqlOptions = runtimeConfig.DataSource.GetTypedOptions<MsSqlOptions>();
            _isSessionContextEnabled = msSqlOptions is null ? false : msSqlOptions.SetSessionContext;
            _accessTokenFromController = runtimeConfigProvider.ManagedIdentityAccessToken;
            _attemptToSetAccessToken = ShouldManagedIdentityAccessBeAttempted();
        }

        /// <summary>
        /// Modifies the properties of the supplied connection to support managed identity access.
        /// In the case of MsSql, gets access token if deemed necessary and sets it on the connection.
        /// The supplied connection is assumed to already have the same connection string
        /// provided in the runtime configuration.
        /// </summary>
        /// <param name="conn">The supplied connection to modify for managed identity access.</param>
        public override async Task SetManagedIdentityAccessTokenIfAnyAsync(DbConnection conn)
        {
            // Only attempt to get the access token if the connection string is in the appropriate format
            if (_attemptToSetAccessToken)
            {
                SqlConnection sqlConn = (SqlConnection)conn;

                // If the configuration controller provided a managed identity access token use that,
                // else use the default saved access token if still valid.
                // Get a new token only if the saved token is null or expired.
                string? accessToken = _accessTokenFromController ??
                    (IsDefaultAccessTokenValid() ?
                        ((AccessToken)_defaultAccessToken!).Token :
                        await GetAccessTokenAsync());

                if (accessToken is not null)
                {
                    sqlConn.AccessToken = accessToken;
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
        private bool ShouldManagedIdentityAccessBeAttempted()
        {
            return string.IsNullOrEmpty(ConnectionStringBuilder.UserID) &&
                string.IsNullOrEmpty(ConnectionStringBuilder.Password) &&
                ConnectionStringBuilder.Authentication == SqlAuthenticationMethod.NotSpecified &&
                !ConnectionStringBuilder.IntegratedSecurity;
        }

        /// <summary>
        /// Determines if the saved default azure credential's access token is valid and not expired.
        /// </summary>
        /// <returns>True if valid, false otherwise.</returns>
        private bool IsDefaultAccessTokenValid()
        {
            return _defaultAccessToken is not null &&
                ((AccessToken)_defaultAccessToken).ExpiresOn.CompareTo(System.DateTimeOffset.Now) > 0;
        }

        /// <summary>
        /// Tries to get an access token using DefaultAzureCredentials.
        /// Catches any CredentialUnavailableException and logs only a warning
        /// since since this is best effort.
        /// </summary>
        /// <returns>The string representation of the access token if found,
        /// null otherwise.</returns>
        private async Task<string?> GetAccessTokenAsync()
        {
            try
            {
                _defaultAccessToken =
                    await AzureCredential.GetTokenAsync(
                        new TokenRequestContext(new[] { DATABASE_SCOPE }));
            }
            catch (CredentialUnavailableException ex)
            {
                QueryExecutorLogger.LogWarning($"{HttpContextExtensions.GetLoggerCorrelationId(HttpContextAccessor.HttpContext)}" +
                    $"Attempt to retrieve a managed identity access token using DefaultAzureCredential" +
                    $" failed due to: \n{ex}");
            }

            return _defaultAccessToken?.Token;
        }

        /// <summary>
        /// Method to generate the query to send user data to the underlying database via SESSION_CONTEXT which might be used
        /// for additional security (eg. using Security Policies) at the database level. The max payload limit for SESSION_CONTEXT is 1MB.
        /// </summary>
        /// <param name="httpContext">Current user httpContext.</param>
        /// <param name="parameters">Dictionary of parameters/value required to execute the query.</param>
        /// <returns>empty string / query to set session parameters for the connection.</returns>
        /// <seealso cref="https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-set-session-context-transact-sql?view=sql-server-ver16"/>
        public override string GetSessionParamsQuery(HttpContext? httpContext, IDictionary<string, DbConnectionParam> parameters)
        {
            if (httpContext is null || !_isSessionContextEnabled)
            {
                return string.Empty;
            }

            // Dictionary containing all the claims belonging to the user, to be used as session parameters.
            Dictionary<string, Claim> sessionParams = AuthorizationResolver.GetAllUserClaims(httpContext);

            // Counter to generate different param name for each of the sessionParam.
            IncrementingInteger counter = new();
            const string SESSION_PARAM_NAME = $"{BaseQueryStructure.PARAM_NAME_PREFIX}session_param";
            StringBuilder sessionMapQuery = new();

            foreach ((string claimType, Claim claim) in sessionParams)
            {
                string paramName = $"{SESSION_PARAM_NAME}{counter.Next()}";
                parameters.Add(paramName, new(claim.Value));
                // Append statement to set read only param value - can be set only once for a connection.
                string statementToSetReadOnlyParam = "EXEC sp_set_session_context " + $"'{claimType}', " + paramName + ", @read_only = 1;";
                sessionMapQuery = sessionMapQuery.Append(statementToSetReadOnlyParam);
            }

            return sessionMapQuery.ToString();
        }

        /// <inheritdoc/>
        public override async Task<DbResultSet> GetMultipleResultSetsIfAnyAsync(
            DbDataReader dbDataReader, List<string>? args = null)
        {
            // From the first result set, we get the count(0/1) of records with given PK.
            DbResultSet resultSetWithCountOfRowsWithGivenPk = await ExtractResultSetFromDbDataReader(dbDataReader);
            DbResultSetRow? resultSetRowWithCountOfRowsWithGivenPk = resultSetWithCountOfRowsWithGivenPk.Rows.FirstOrDefault();
            int numOfRecordsWithGivenPK;

            if (resultSetRowWithCountOfRowsWithGivenPk is not null &&
                resultSetRowWithCountOfRowsWithGivenPk.Columns.TryGetValue(MsSqlQueryBuilder.COUNT_ROWS_WITH_GIVEN_PK, out object? rowsWithGivenPK))
            {
                numOfRecordsWithGivenPK = (int)rowsWithGivenPK!;
            }
            else
            {
                throw new DataApiBuilderException(
                    message: $"Neither insert nor update could be performed.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            // The second result set holds the records returned as a result of the executed update/insert operation.
            DbResultSet? dbResultSet = await dbDataReader.NextResultAsync() ? await ExtractResultSetFromDbDataReader(dbDataReader) : null;

            if (dbResultSet is null)
            {
                // For a PUT/PATCH operation on a table/view with non-autogen PK, we would either perform an insert or an update for sure,
                // and correspondingly dbResultSet can not be null.
                // However, in case of autogen PK, we would not attempt an insert since PK is auto generated.
                // We would only attempt an update , and that too when a record exists for given PK.
                // However since the dbResultSet is null here, it indicates we didn't perform an update either.
                // This happens when count of rows with given PK = 0.

                if (args is not null && args.Count > 1)
                {
                    string prettyPrintPk = args![0];
                    string entityName = args[1];

                    throw new DataApiBuilderException(
                            message: $"Cannot perform INSERT and could not find {entityName} " +
                            $"with primary key {prettyPrintPk} to perform UPDATE on.",
                            statusCode: HttpStatusCode.NotFound,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
                }

                throw new DataApiBuilderException(
                    message: $"Neither insert nor update could be performed.",
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            if (numOfRecordsWithGivenPK == 1) // This indicates that a record existed with given PK and we attempted an update operation.
            {
                if (dbResultSet.Rows.Count == 0)
                {
                    // Record exists in the table/view but no record updated - indicates database policy failure.
                    throw new DataApiBuilderException(
                        message: DataApiBuilderException.AUTHORIZATION_FAILURE,
                        statusCode: HttpStatusCode.Forbidden,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure);
                }

                // This is used as an identifier to distinguish between update/insert operations.
                // Later helps to add location header in case of insert operation.
                dbResultSet.ResultProperties.Add(SqlMutationEngine.IS_UPDATE_RESULT_SET, true);
            }
            else if (dbResultSet.Rows.Count == 0)
            {
                // No record exists in the table/view but inserted no records - indicates database policy failure.
                throw new DataApiBuilderException(
                        message: DataApiBuilderException.AUTHORIZATION_FAILURE,
                        statusCode: HttpStatusCode.Forbidden,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure);
            }

            return dbResultSet;
        }

        /// <inheritdoc/>
        public override void PopulateDbTypeForParameter(KeyValuePair<string, DbConnectionParam> parameterEntry, DbParameter parameter)
        {
            if (parameterEntry.Value is not null && parameterEntry.Value.DbType is not null)
            {
                parameter.DbType = (DbType)parameterEntry.Value.DbType;
            }
        }
    }
}
