// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data;
using System.Data.Common;
using System.Net;
using System.Security.Claims;
using System.Text;
using Azure.Core;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Product;
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
        /// Key: datasource name, Value: access token for this datasource.
        /// </summary>
        private Dictionary<string, string?> _accessTokensFromConfiguration;

        /// <summary>
        /// The MsSql specific connection string builders.
        /// Key: datasource name, Value: connection string builder for this datasource.
        /// </summary>
        public override IDictionary<string, DbConnectionStringBuilder> ConnectionStringBuilders
            => base.ConnectionStringBuilders;

        public DefaultAzureCredential AzureCredential { get; set; } = new();  // CodeQL [SM05137] DefaultAzureCredential will use Managed Identity if available or fallback to default.

        /// <summary>
        /// The saved cached access token obtained from DefaultAzureCredentials
        /// representing a managed identity. 
        /// </summary>
        private AccessToken? _defaultAccessToken;

        /// <summary>
        /// DatasourceName to boolean value indicating if access token should be set for db.
        /// </summary>
        private Dictionary<string, bool> _dataSourceAccessTokenUsage;

        /// <summary>
        /// DatasourceName to boolean value indicating if session context should be set for db.
        /// </summary>
        private Dictionary<string, bool> _dataSourceToSessionContextUsage;

        /// <summary>
        /// DatasourceName to UserDelegatedAuthOptions for user-delegated authentication.
        /// Only populated for data sources with user-delegated-auth enabled.
        /// </summary>
        private Dictionary<string, UserDelegatedAuthOptions> _dataSourceUserDelegatedAuth;

        /// <summary>
        /// DatasourceName to base Application Name for OBO per-user pooling.
        /// Only populated for data sources with user-delegated-auth enabled.
        /// Used as a prefix when constructing user-specific Application Names.
        /// </summary>
        private Dictionary<string, string> _dataSourceBaseAppName;

        /// <summary>
        /// Optional OBO token provider for user-delegated authentication.
        /// </summary>
        private readonly IOboTokenProvider? _oboTokenProvider;

        private readonly RuntimeConfigProvider _runtimeConfigProvider;

        private const string QUERYIDHEADER = "QueryIdentifyingIds";

        public MsSqlQueryExecutor(
            RuntimeConfigProvider runtimeConfigProvider,
            DbExceptionParser dbExceptionParser,
            ILogger<IQueryExecutor> logger,
            IHttpContextAccessor httpContextAccessor,
            HotReloadEventHandler<HotReloadEventArgs>? handler = null,
            IOboTokenProvider? oboTokenProvider = null)
            : base(dbExceptionParser,
                  logger,
                  runtimeConfigProvider,
                  httpContextAccessor,
                  handler)
        {
            _dataSourceAccessTokenUsage = new Dictionary<string, bool>();
            _dataSourceToSessionContextUsage = new Dictionary<string, bool>();
            _dataSourceUserDelegatedAuth = new Dictionary<string, UserDelegatedAuthOptions>();
            _dataSourceBaseAppName = new Dictionary<string, string>();
            _accessTokensFromConfiguration = runtimeConfigProvider.ManagedIdentityAccessToken;
            _runtimeConfigProvider = runtimeConfigProvider;
            _oboTokenProvider = oboTokenProvider;
            ConfigureMsSqlQueryExecutor();
        }

        /// <summary>
        /// Creates a SQLConnection to the data source of given name. This method also adds an event handler to
        /// the connection's InfoMessage to extract the statement ID from the request and add it to httpcontext.
        /// </summary>
        /// <param name="dataSourceName">The name of the data source.</param>
        /// <returns>The SQLConnection</returns>
        /// <exception cref="DataApiBuilderException">Exception thrown if datasource is not found.</exception>
        public override SqlConnection CreateConnection(string dataSourceName)
        {
            if (!ConnectionStringBuilders.ContainsKey(dataSourceName))
            {
                throw new DataApiBuilderException("Query execution failed. Could not find datasource to execute query against", HttpStatusCode.BadRequest, DataApiBuilderException.SubStatusCodes.DataSourceNotFound);
            }

            string connectionString = GetConnectionStringForCurrentUser(dataSourceName);

            SqlConnection conn = new()
            {
                ConnectionString = connectionString,
            };

            // Extract info message from SQLConnection
            conn.InfoMessage += (object sender, SqlInfoMessageEventArgs e) =>
            {
                try
                {
                    // Log the statement ids returned by the SQL engine when we executed the batch.
                    // This helps in correlating with SQL engine telemetry.

                    // If the info message has an error code that matches the well-known codes used for returning statement ID,
                    // then we can be certain that the message contains no PII.
                    IEnumerable<SqlError> errorsReceived = e.Errors.Cast<SqlError>();

                    IEnumerable<SqlInformationalCodes> allInfoCodesKnown = Enum.GetValues(typeof(SqlInformationalCodes)).Cast<SqlInformationalCodes>();

                    IEnumerable<string> infoErrorMessagesReceived = errorsReceived.Join(allInfoCodesKnown, error => error.Number, code => (int)code, (error, code) => error.Message);

                    foreach (string infoErrorMessageReceived in infoErrorMessagesReceived)
                    {
                        // Add statement ID to request
                        AddStatementIDToMiddlewareContext(infoErrorMessageReceived);
                    }
                }
                catch (Exception ex)
                {
                    QueryExecutorLogger.LogError($"Error in info message handler while extracting query-identifying ID from SQLConnection. Error: {ex.Message}");
                }
            };

            return conn;
        }

        /// <summary>
        /// Gets the connection string for the current user. For OBO-enabled data sources,
        /// this returns a connection string with a user-specific Application Name to isolate
        /// connection pools per user identity.
        /// </summary>
        /// <param name="dataSourceName">The name of the data source.</param>
        /// <returns>The connection string to use for the current request.</returns>
        private string GetConnectionStringForCurrentUser(string dataSourceName)
        {
            string baseConnectionString = ConnectionStringBuilders[dataSourceName].ConnectionString;

            // Per-user pooling is automatic when OBO is enabled.
            // _dataSourceBaseAppName is only populated for data sources with user-delegated-auth enabled.
            if (!_dataSourceBaseAppName.TryGetValue(dataSourceName, out string? baseAppName))
            {
                // OBO not enabled for this data source, use the standard connection string
                return baseConnectionString;
            }

            // Extract user pool key from current HTTP context (prefers oid, falls back to sub)
            string? poolKeyHash = GetUserPoolKeyHash(dataSourceName);
            if (string.IsNullOrEmpty(poolKeyHash))
            {
                // For OBO-enabled data sources, we must have a user context for actual requests.
                // Null poolKeyHash is only acceptable during startup/metadata phase when there's no HttpContext.
                // If we have an HttpContext with a User but missing required claims, fail-safe to prevent
                // potential cross-user connection pool contamination.
                if (HttpContextAccessor?.HttpContext?.User?.Identity?.IsAuthenticated == true)
                {
                    throw new DataApiBuilderException(
                        message: "User-delegated authentication requires 'iss' and user identifier (oid/sub) claims for connection pool isolation.",
                        statusCode: System.Net.HttpStatusCode.Unauthorized,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.OboAuthenticationFailure);
                }

                // No user context (startup/metadata phase), use base connection string
                return baseConnectionString;
            }

            // Create a user-specific connection string by appending to the existing Application Name.
            // baseAppName preserves the customer's original Application Name.
            // We append |obo:{hash} to create a separate connection pool for each unique user.
            // Format: {existingAppName}|obo:{hash}
            // SQL Server limits Application Name to 128 characters. To avoid SQL Server silently
            // truncating the value (which could cut off part of the hash and compromise per-user
            // pooling), we ensure the full |obo:{hash} suffix is preserved and, if necessary,
            // truncate only the baseAppName portion.
            string oboSuffix = $"|obo:{poolKeyHash}";
            const int maxApplicationNameLength = 128;
            int allowedBaseAppNameLength = Math.Max(0, maxApplicationNameLength - oboSuffix.Length);
            string effectiveBaseAppName = baseAppName.Length > allowedBaseAppNameLength
                ? baseAppName[..allowedBaseAppNameLength]
                : baseAppName;

            SqlConnectionStringBuilder userBuilder = new(baseConnectionString)
            {
                ApplicationName = $"{effectiveBaseAppName}{oboSuffix}",
                Pooling = true
            };

            return userBuilder.ConnectionString;
        }

        /// <summary>
        /// Generates a pool key hash from the current user's claims for OBO per-user pooling.
        /// Uses iss|(oid||sub) to ensure each unique user identity gets its own connection pool.
        /// Prefers 'oid' (stable GUID) but falls back to 'sub' for guest/B2B users.
        /// </summary>
        /// <param name="dataSourceName">The data source name for logging purposes.</param>
        /// <returns>A URL-safe Base64-encoded hash, or null if no user context is available.</returns>
        private string? GetUserPoolKeyHash(string dataSourceName)
        {
            if (HttpContextAccessor?.HttpContext?.User is null)
            {
                return null;
            }

            ClaimsPrincipal user = HttpContextAccessor.HttpContext.User;

            // Extract issuer claim - required for tenant isolation and connection pool security.
            // The "iss" claim must be present along with a user identifier (oid/sub) for per-user pooling.
            // Callers are responsible for enforcing fail-safe behavior when claims are missing.
            string? iss = user.FindFirst("iss")?.Value;

            // Prefer oid (stable GUID), fall back to sub for guest/B2B users
            string? userKey = user.FindFirst("oid")?.Value
                ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/objectidentifier")?.Value
                ?? user.FindFirst("sub")?.Value
                ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (string.IsNullOrEmpty(iss) || string.IsNullOrEmpty(userKey))
            {
                // Cannot create a pool key without both claims
                QueryExecutorLogger.LogDebug(
                    "Cannot create per-user pool key for data source {DataSourceName}: missing {MissingClaim} claim.",
                    dataSourceName,
                    string.IsNullOrEmpty(iss) ? "iss" : "user identifier (oid/sub)");
                return null;
            }

            // Create the pool key as iss|userKey and hash it to keep connection string small
            string poolKey = $"{iss}|{userKey}";
            return HashPoolKey(poolKey);
        }

        /// <summary>
        /// Hashes the pool key using SHA512 to create a compact, URL-safe identifier.
        /// This keeps the Application Name reasonably short while ensuring uniqueness.
        /// </summary>
        /// <param name="key">The pool key to hash (format: iss|oid or iss|sub).</param>
        /// <returns>A URL-safe Base64-encoded hash of the key.</returns>
        private static string HashPoolKey(string key)
        {
            using var sha = System.Security.Cryptography.SHA512.Create();
            byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(key));
            return Convert.ToBase64String(bytes)
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
        }

        /// <summary>
        /// Configure during construction or a hot-reload scenario.
        /// </summary>
        private void ConfigureMsSqlQueryExecutor()
        {
            IEnumerable<KeyValuePair<string, DataSource>> mssqldbs = _runtimeConfigProvider.GetConfig().GetDataSourceNamesToDataSourcesIterator().Where(x => x.Value.DatabaseType is DatabaseType.MSSQL || x.Value.DatabaseType is DatabaseType.DWSQL);

            foreach ((string dataSourceName, DataSource dataSource) in mssqldbs)
            {
                SqlConnectionStringBuilder builder = new(dataSource.ConnectionString);

                if (_runtimeConfigProvider.IsLateConfigured)
                {
                    builder.Encrypt = SqlConnectionEncryptOption.Mandatory;
                    builder.TrustServerCertificate = false;
                }

                ConnectionStringBuilders.TryAdd(dataSourceName, builder);
                MsSqlOptions? msSqlOptions = dataSource.GetTypedOptions<MsSqlOptions>();
                _dataSourceToSessionContextUsage[dataSourceName] = msSqlOptions is null ? false : msSqlOptions.SetSessionContext;
                _dataSourceAccessTokenUsage[dataSourceName] = ShouldManagedIdentityAccessBeAttempted(builder);

                // Track user-delegated authentication settings
                if (dataSource.IsUserDelegatedAuthEnabled)
                {
                    _dataSourceUserDelegatedAuth[dataSourceName] = dataSource.UserDelegatedAuth!;

                    // Per-user pooling: Keep pooling enabled but store the base Application Name.
                    // At connection time, we'll append the user's iss:sub hash to create isolated pools per user.
                    // This is automatic for OBO to prevent connection exhaustion while ensuring pool isolation.
                    // Note: ApplicationName is typically already set by RuntimeConfigLoader (e.g., "CustomerApp,dab_oss_2.0.0")
                    // but we use GetDataApiBuilderUserAgent() as fallback for consistency.
                    _dataSourceBaseAppName[dataSourceName] = builder.ApplicationName ?? ProductInfo.GetDataApiBuilderUserAgent();
                    builder.Pooling = true;
                }
            }
        }

        /// <summary>
        /// Modifies the properties of the supplied connection to support managed identity access
        /// or user-delegated (OBO) authentication.
        /// In the case of MsSql, gets access token if deemed necessary and sets it on the connection.
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

            SqlConnection sqlConn = (SqlConnection)conn;

            // Check if user-delegated authentication is enabled for this data source
            if (_dataSourceUserDelegatedAuth.TryGetValue(dataSourceName, out UserDelegatedAuthOptions? userDelegatedAuth))
            {
                // Check if we're in an HTTP request context (not startup/metadata phase)
                bool isInRequestContext = HttpContextAccessor?.HttpContext is not null;

                if (isInRequestContext)
                {
                    // At runtime with an HTTP request - attempt OBO flow
                    // Note: DatabaseAudience is validated at startup by RuntimeConfigValidator
                    string? oboToken = await GetOboAccessTokenAsync(userDelegatedAuth.DatabaseAudience!);
                    if (oboToken is not null)
                    {
                        sqlConn.AccessToken = oboToken;
                        return;
                    }

                    // OBO is enabled but we couldn't get a token (e.g., missing Bearer token in request)
                    // This is an error during request processing - we must not fall back to managed identity
                    throw new DataApiBuilderException(
                        message: DataApiBuilderException.OBO_MISSING_USER_CONTEXT,
                        statusCode: HttpStatusCode.Unauthorized,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.OboAuthenticationFailure);
                }

                // At startup/metadata phase (no HTTP context) - fall through to use the configured
                // connection string authentication (e.g., Managed Identity, SQL credentials, etc.)
                // This allows DAB to read schema metadata at startup, while OBO is used for actual requests.
                QueryExecutorLogger.LogDebug("No HTTP context available - using configured connection string authentication for startup/metadata operations.");
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
                    sqlConn.AccessToken = accessToken;
                }
            }
        }

        /// <summary>
        /// Acquires an access token using On-Behalf-Of (OBO) flow for user-delegated authentication.
        /// </summary>
        /// <param name="databaseAudience">The target database audience.</param>
        /// <returns>The OBO access token, or null if OBO cannot be performed.</returns>
        private async Task<string?> GetOboAccessTokenAsync(string databaseAudience)
        {
            if (_oboTokenProvider is null || HttpContextAccessor?.HttpContext is null)
            {
                return null;
            }

            HttpContext httpContext = HttpContextAccessor.HttpContext;
            ClaimsPrincipal? principal = httpContext.User;

            // Extract the incoming JWT assertion from the Authorization header
            string? authHeader = httpContext.Request.Headers["Authorization"].FirstOrDefault();
            if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                QueryExecutorLogger.LogWarning(DataApiBuilderException.OBO_MISSING_USER_CONTEXT);
                return null;
            }

            string incomingJwt = authHeader.Substring("Bearer ".Length).Trim();

            return await _oboTokenProvider.GetAccessTokenOnBehalfOfAsync(
                principal!,
                incomingJwt,
                databaseAudience);
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
        private static bool ShouldManagedIdentityAccessBeAttempted(SqlConnectionStringBuilder builder)
        {
            return string.IsNullOrEmpty(builder.UserID) &&
                string.IsNullOrEmpty(builder.Password) &&
                builder.Authentication == SqlAuthenticationMethod.NotSpecified &&
                !builder.IntegratedSecurity;
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
        /// since since this is best effort.
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
                    message: "{correlationId} Failed to retrieve a managed identity access token using DefaultAzureCredential due to:\n{errorMessage}",
                    correlationId,
                    ex.Message);
            }

            return _defaultAccessToken?.Token;
        }

        /// <summary>
        /// Method to generate the query to send user data to the underlying database via SESSION_CONTEXT which might be used
        /// for additional security (eg. using Security Policies) at the database level. The max payload limit for SESSION_CONTEXT is 1MB.
        /// </summary>
        /// <param name="httpContext">Current user httpContext.</param>
        /// <param name="parameters">Dictionary of parameters/value required to execute the query.</param>
        /// <param name="dataSourceName">Name of datasource for which to set access token. Default dbName taken from config if null</param>
        /// <returns>empty string / query to set session parameters for the connection.</returns>
        /// <seealso cref="https://learn.microsoft.com/en-us/sql/relational-databases/system-stored-procedures/sp-set-session-context-transact-sql?view=sql-server-ver16"/>
        public override string GetSessionParamsQuery(HttpContext? httpContext, IDictionary<string, DbConnectionParam> parameters, string dataSourceName)
        {
            if (string.IsNullOrEmpty(dataSourceName))
            {
                dataSourceName = ConfigProvider.GetConfig().DefaultDataSourceName;
            }

            if (httpContext is null || !_dataSourceToSessionContextUsage[dataSourceName])
            {
                return string.Empty;
            }

            // Dictionary containing all the claims belonging to the user, to be used as session parameters.
            Dictionary<string, string> sessionParams = AuthorizationResolver.GetProcessedUserClaims(httpContext);

            // Counter to generate different param name for each of the sessionParam.
            IncrementingInteger counter = new();
            const string SESSION_PARAM_NAME = $"{BaseQueryStructure.PARAM_NAME_PREFIX}session_param";
            StringBuilder sessionMapQuery = new();

            foreach ((string claimType, string claimValue) in sessionParams)
            {
                string paramName = $"{SESSION_PARAM_NAME}{counter.Next()}";
                parameters.Add(paramName, new(claimValue));
                // Append statement to set read only param value - can be set only once for a connection.
                string statementToSetReadOnlyParam = "EXEC sp_set_session_context " + $"'{claimType}', " + paramName + ", @read_only = 0;";
                sessionMapQuery = sessionMapQuery.Append(statementToSetReadOnlyParam);
            }

            return sessionMapQuery.ToString();
        }

        /// <inheritdoc/>
        public override async Task<DbResultSet> GetMultipleResultSetsIfAnyAsync(
            DbDataReader dbDataReader, List<string>? args = null)
        {
            // From the first result set, we get the count(0/1) of records with given PK.
            DbResultSet resultSetWithCountOfRowsWithGivenPk = await ExtractResultSetFromDbDataReaderAsync(dbDataReader);
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
            DbResultSet? dbResultSet = await dbDataReader.NextResultAsync() ? await ExtractResultSetFromDbDataReaderAsync(dbDataReader) : null;

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
                            subStatusCode: DataApiBuilderException.SubStatusCodes.ItemNotFound);
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

        /// <inheritdoc />
        public override SqlCommand PrepareDbCommand(
            SqlConnection conn,
            string sqltext,
            IDictionary<string, DbConnectionParam> parameters,
            HttpContext? httpContext,
            string dataSourceName)
        {
            SqlCommand cmd = conn.CreateCommand();
            cmd.CommandType = CommandType.Text;

            // Add query to send user data from DAB to the underlying database to enable additional security the user might have configured
            // at the database level.
            string sessionParamsQuery = GetSessionParamsQuery(httpContext, parameters, dataSourceName);

            cmd.CommandText = sessionParamsQuery + sqltext;
            if (parameters is not null)
            {
                foreach (KeyValuePair<string, DbConnectionParam> parameterEntry in parameters)
                {
                    SqlParameter parameter = cmd.CreateParameter();
                    parameter.ParameterName = parameterEntry.Key;
                    parameter.Value = parameterEntry.Value.Value ?? DBNull.Value;
                    PopulateDbTypeForParameter(parameterEntry, parameter);
                    cmd.Parameters.Add(parameter);
                }
            }

            return cmd;
        }

        /// <inheritdoc/>
        public static void PopulateDbTypeForParameter(KeyValuePair<string, DbConnectionParam> parameterEntry, SqlParameter parameter)
        {
            if (parameterEntry.Value is not null)
            {
                if (parameterEntry.Value.DbType is not null)
                {
                    parameter.DbType = (DbType)parameterEntry.Value.DbType;
                }

                if (parameterEntry.Value.SqlDbType is not null)
                {
                    parameter.SqlDbType = (SqlDbType)parameterEntry.Value.SqlDbType;
                }
            }
        }

        private void AddStatementIDToMiddlewareContext(string statementId)
        {
            HttpContext? httpContext = HttpContextAccessor?.HttpContext;
            if (httpContext != null)
            {
                // locking is because we could have multiple queries in a single http request and each query will be processed in parallel leading to concurrent access of the httpContext.Items.
                lock (_httpContextLock)
                {
                    if (httpContext.Items.TryGetValue(QUERYIDHEADER, out object? currentValue) && currentValue is not null)
                    {
                        try
                        {
                            httpContext.Items[QUERYIDHEADER] = (string)currentValue + ";" + statementId;
                        }
                        catch
                        {
                            QueryExecutorLogger.LogWarning("Could not cast query identifying ID to string. The ID was not added to httpcontext");
                            return;
                        }
                    }
                    else
                    {
                        httpContext.Items[QUERYIDHEADER] = statementId;
                    }
                }
            }
        }
    }
}
