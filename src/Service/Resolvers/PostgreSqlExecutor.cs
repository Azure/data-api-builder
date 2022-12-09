using System;
using System.Data.Common;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Specialized QueryExecutor for PostgreSql mainly providing methods to
    /// handle connecting to the database with a managed identity.
    /// for more info: https://learn.microsoft.com/EN-us/azure/postgresql/single-server/how-to-connect-with-managed-identity
    /// /// </summary>
    public class PostgreSqlQueryExecutor : QueryExecutor<NpgsqlConnection>
    {
        // This is the same scope for any Azure SQL database that is
        // required to request a default azure credential access token
        // for a managed identity.
        public const string DATABASE_SCOPE = @"https://ossrdbms-aad.database.windows.net/.default";

        /// <summary>
        /// The managed identity Access Token string obtained
        /// from the configuration controller.
        /// </summary>
        private readonly string? _accessTokenFromController;

        public DefaultAzureCredential AzureCredential { get; set; } = new();

        /// <summary>
        /// The saved cached access token obtained from DefaultAzureCredentials
        /// representing a managed identity.
        /// </summary>
        private AccessToken? _defaultAccessToken;

        private bool _attemptToSetAccessToken;

        public PostgreSqlQueryExecutor(
            RuntimeConfigProvider runtimeConfigProvider,
            DbExceptionParser dbExceptionParser,
            ILogger<QueryExecutor<NpgsqlConnection>> logger)
            : base(runtimeConfigProvider, dbExceptionParser, logger)
        {
            _accessTokenFromController = runtimeConfigProvider.ManagedIdentityAccessToken;
            _attemptToSetAccessToken =
                ShouldManagedIdentityAccessBeAttempted(runtimeConfigProvider.GetRuntimeConfiguration().ConnectionString);
        }

        /// <summary>
        /// Modifies the properties of the supplied connection string to support managed identity access.
        /// In the case of Postgres, if a default managed identity needs to be used, the password in the
        /// connection needs to be replaced with the default access token.
        /// </summary>
        /// <param name="conn">The supplied connection to modify for managed identity access.</param>
        public override async Task SetManagedIdentityAccessTokenIfAnyAsync(DbConnection conn)
        {
            // Only attempt to get the access token if the connection string is in the appropriate format
            if (_attemptToSetAccessToken)
            {
                NpgsqlConnection sqlConn = (NpgsqlConnection)conn;

                // If the configuration controller provided a managed identity access token use that,
                // else use the default saved access token if still valid.
                // Get a new token only if the saved token is null or expired.
                string? accessToken = _accessTokenFromController ??
                    (IsDefaultAccessTokenValid() ?
                        ((AccessToken)_defaultAccessToken!).Token :
                        await GetAccessTokenAsync());

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
        private static bool ShouldManagedIdentityAccessBeAttempted(string connString)
        {
            NpgsqlConnectionStringBuilder connStringBuilder = new(connString);
            return string.IsNullOrEmpty(connStringBuilder.Password);
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
            // because there can be scenarios where password is not specified but
            // default managed identity is not the intended method of authentication
            // so a bunch of different exceptions could occur in that scenario
            catch (Exception ex)
            {
                QueryExecutorLogger.LogWarning($"Attempt to retrieve a managed identity access token using DefaultAzureCredential" +
                    $" failed due to: \n{ex}");
            }

            return _defaultAccessToken?.Token;
        }
    }
}
