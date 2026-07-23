// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Text;
using Azure.Core;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
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
        private Dictionary<string, string?> _accessTokensFromConfiguration;

        /// <summary>
        /// DatasourceName to boolean value indicating if session context should be set for db.
        /// </summary>
        private Dictionary<string, bool> _dataSourceToSessionContextUsage;

        public DefaultAzureCredential AzureCredential { get; set; } = new(); // CodeQL [SM05137]: DefaultAzureCredential will use Managed Identity if available or fallback to default.

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

        private readonly RuntimeConfigProvider _runtimeConfigProvider;

        public PostgreSqlQueryExecutor(
            RuntimeConfigProvider runtimeConfigProvider,
            DbExceptionParser dbExceptionParser,
            ILogger<IQueryExecutor> logger,
            IHttpContextAccessor httpContextAccessor,
            HotReloadEventHandler<HotReloadEventArgs>? handler = null)
            : base(
                  dbExceptionParser,
                  logger,
                  runtimeConfigProvider,
                  httpContextAccessor,
                  handler)
        {
            _dataSourceAccessTokenUsage = new Dictionary<string, bool>();
            _dataSourceToSessionContextUsage = new Dictionary<string, bool>();
            _accessTokensFromConfiguration = runtimeConfigProvider.ManagedIdentityAccessToken;
            _runtimeConfigProvider = runtimeConfigProvider;
            ConfigurePostgreSqlQueryExecutor();
        }

        /// <summary>
        /// Configure during construction or a hot-reload scenario.
        /// </summary>
        private void ConfigurePostgreSqlQueryExecutor()
        {
            IEnumerable<KeyValuePair<string, DataSource>> postgresqldbs =
                _runtimeConfigProvider.GetConfig()
                    .GetDataSourceNamesToDataSourcesIterator()
                    .Where(x => x.Value.DatabaseType == DatabaseType.PostgreSQL);

            foreach ((string dataSourceName, DataSource dataSource) in postgresqldbs)
            {
                NpgsqlConnectionStringBuilder builder = new(dataSource.ConnectionString);

                if (_runtimeConfigProvider.IsLateConfigured)
                {
                    builder.SslMode = SslMode.VerifyFull;
                }

                ConnectionStringBuilders.TryAdd(dataSourceName, builder);

                PostgreSqlOptions? sessionOptions = dataSource.GetTypedOptions<PostgreSqlOptions>();
                _dataSourceToSessionContextUsage[dataSourceName] =
                    sessionOptions is not null && sessionOptions.SetSessionContext;

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
            if (string.IsNullOrEmpty(dataSourceName))
            {
                dataSourceName = ConfigProvider.GetConfig().DefaultDataSourceName;
            }

            _dataSourceAccessTokenUsage.TryGetValue(dataSourceName, out bool setAccessToken);

            if (setAccessToken)
            {
                NpgsqlConnection sqlConn = (NpgsqlConnection)conn;

                _accessTokensFromConfiguration.TryGetValue(dataSourceName, out string? accessTokenFromController);
                string? accessToken = accessTokenFromController ??
                    (IsDefaultAccessTokenValid()
                        ? _defaultAccessToken!.Value.Token
                        : await GetAccessTokenAsync(dataSourceName));

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
        /// Sync counterpart for managed identity handling.
        /// </summary>
        public override void SetManagedIdentityAccessTokenIfAny(DbConnection conn, string dataSourceName = "")
        {
            SetManagedIdentityAccessTokenIfAnyAsync(conn, dataSourceName).GetAwaiter().GetResult();
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
        private bool IsDefaultAccessTokenValid()
        {
            return _defaultAccessToken is not null &&
                   _defaultAccessToken.Value.ExpiresOn.CompareTo(DateTimeOffset.Now) > 0;
        }

        /// <summary>
        /// Tries to get an access token using DefaultAzureCredentials.
        /// </summary>
        private async Task<string?> GetAccessTokenAsync(string dataSourceName)
        {
            bool firstAttemptAtDefaultAccessToken = _defaultAccessToken is null;

            try
            {
                _defaultAccessToken =
                    await AzureCredential.GetTokenAsync(
                        new TokenRequestContext(new[] { DATABASE_SCOPE }));
            }
            catch (Exception ex)
            {
                string messagePrefix = "{correlationId} No password detected in the connection string. Attempt to retrieve a managed identity access token using DefaultAzureCredential failed due to:\n{errorMessage}";
                string messageSuffix = firstAttemptAtDefaultAccessToken
                    ? "If authentication with DefaultAzureCrendential is not intended, this warning can be safely ignored."
                    : string.Empty;
                string message = messagePrefix + messageSuffix;

                QueryExecutorLogger.LogWarning(
                    exception: ex,
                    message: message,
                    HttpContextExtensions.GetLoggerCorrelationId(HttpContextAccessor.HttpContext),
                    ex.Message);

                if (firstAttemptAtDefaultAccessToken)
                {
                    _dataSourceAccessTokenUsage[dataSourceName] = false;
                }
            }

            return _defaultAccessToken?.Token;
        }

        /// <summary>
        /// No query prefixing for PostgreSQL. Session state is set via a dedicated command
        /// on the same open connection inside PrepareDbCommand(...).
        /// </summary>
        public override string GetSessionParamsQuery(
            HttpContext? httpContext,
            IDictionary<string, DbConnectionParam> parameters,
            string dataSourceName)
        {
            return string.Empty;
        }

        /// <summary>
        /// PostgreSQL override that first sets session settings on the already-open connection
        /// using a dedicated command, then returns the actual data command.
        /// </summary>
        public override DbCommand PrepareDbCommand(
            NpgsqlConnection conn,
            string sqltext,
            IDictionary<string, DbConnectionParam> parameters,
            HttpContext? httpContext,
            string dataSourceName)
        {
            SetSessionContext(conn, httpContext, dataSourceName);

            NpgsqlCommand cmd = conn.CreateCommand();
            cmd.CommandType = System.Data.CommandType.Text;
            cmd.CommandText = sqltext;

            if (parameters is not null)
            {
                foreach (KeyValuePair<string, DbConnectionParam> parameterEntry in parameters)
                {
                    DbParameter parameter = cmd.CreateParameter();
                    parameter.ParameterName = parameterEntry.Key;
                    parameter.Value = parameterEntry.Value.Value ?? DBNull.Value;
                    PopulateDbTypeForParameter(parameterEntry, parameter);
                    cmd.Parameters.Add(parameter);
                }
            }

            return cmd;
        }

        /// <summary>
        /// Sets processed user claims into PostgreSQL custom settings on the same open connection.
        /// This command's resultsets are consumed and ignored before the actual query command is created.
        /// </summary>
        private void SetSessionContext(
            NpgsqlConnection conn,
            HttpContext? httpContext,
            string dataSourceName)
        {
            if (string.IsNullOrEmpty(dataSourceName))
            {
                dataSourceName = ConfigProvider.GetConfig().DefaultDataSourceName;
            }

            if (httpContext is null ||
                !_dataSourceToSessionContextUsage.TryGetValue(dataSourceName, out bool enabled) ||
                !enabled)
            {
                return;
            }

            Dictionary<string, string> sessionParams = AuthorizationResolver.GetProcessedUserClaims(httpContext);
            if (sessionParams.Count == 0)
            {
                return;
            }

            using NpgsqlCommand cmd = conn.CreateCommand();
            cmd.CommandType = System.Data.CommandType.Text;

            StringBuilder sql = new();
            int i = 0;

            foreach ((string claimType, string claimValue) in sessionParams)
            {
                string parameterName = $"p{i++}";
                string sessionSettingKey = ToPostgresSessionSettingKey(claimType);

                sql.Append($"SELECT set_config('{sessionSettingKey}', @{parameterName}, false);");
                cmd.Parameters.AddWithValue(parameterName, claimValue);
            }

            cmd.CommandText = sql.ToString();

            using DbDataReader reader = cmd.ExecuteReader();

            do
            {
                while (reader.Read())
                {
                    // ignore set_config result rows
                }
            }
            while (reader.NextResult());
        }

        /// <summary>
        /// Normalize a claim name into a valid PostgreSQL custom setting key.
        /// Example: "tenant" -> "dab.claims.tenant"
        /// </summary>
        private static string ToPostgresSessionSettingKey(string claimType)
        {
            StringBuilder keyBuilder = new("dab.claims.");

            foreach (char c in claimType)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '.')
                {
                    keyBuilder.Append(char.ToLowerInvariant(c));
                }
                else
                {
                    keyBuilder.Append('_');
                }
            }

            return keyBuilder.ToString();
        }
    }
}
