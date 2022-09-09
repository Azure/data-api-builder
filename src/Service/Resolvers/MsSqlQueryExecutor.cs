using System.Data.Common;
using System.Net;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Resolvers
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
        /// The Managed Identity Access Token string obtained from the configuration controller.
        /// </summary>
        private readonly string? _managedIdentityAccessToken;

        public DefaultAzureCredential AzureCredential { get; set; } = new();

        private AccessToken? _defaultAccessToken;

        public MsSqlQueryExecutor(
            RuntimeConfigProvider runtimeConfigProvider,
            DbExceptionParser dbExceptionParser,
            ILogger<QueryExecutor<SqlConnection>> logger)
            : base(runtimeConfigProvider, dbExceptionParser, logger)
        {
            _managedIdentityAccessToken = runtimeConfigProvider.ManagedIdentityAccessToken;
        }

        /// <summary>
        /// Modifies the properties of the supplied connection to support managed identity access.
        /// In the case of MsSql, gets access token if deemed necessary and sets it on the connection.
        /// The supplied connection should already have a connection string.
        /// </summary>
        /// <param name="conn">The supplied connection to modify for managed identity access.</param>
        public override async Task HandleManagedIdentityAccessIfAnyAsync(DbConnection conn)
        {
            SqlConnection sqlConn = (SqlConnection)conn;

            if (string.IsNullOrEmpty(conn.ConnectionString))
            {
                string errMessage = "Attempt to determine managed identity access " +
                    "without supplying a connection string.";
                QueryExecutorLogger.LogError(errMessage);
                throw new DataApiBuilderException(errMessage,
                    statusCode: HttpStatusCode.InternalServerError,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.UnexpectedError);
            }

            // If the configuration controller provided a managed identity access token use that,
            // else use the default saved access token if still valid.
            // Get a new token only if the saved token is null or expired.
            string? accessToken = _managedIdentityAccessToken ??
                (IsDefaultAccessTokenValid() ?
                    ((AccessToken)_defaultAccessToken!).Token :
                    await GetAccessTokenAsync(conn.ConnectionString));

            if (accessToken is not null)
            {
                QueryExecutorLogger.LogTrace("Using access token obtained from " +
                    "DefaultAzureCredential to connect to database.");
                sqlConn.AccessToken = accessToken;
            }
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
        /// Gets an access token using DefaultAzureCredentials
        /// if none of UserID, Password or Authentication method are specified
        /// in the connection string since they have higher precedence
        /// and any attempt to use an access token in their presence would lead to
        /// a System.InvalidOperationException.
        /// </summary>
        /// <param name="connString"></param>
        /// <returns>The string representation of the access token if required and found,
        /// null otherwise.</returns>
        private async Task<string?> GetAccessTokenAsync(string connString)
        {
            SqlConnectionStringBuilder connStringBuilder = new(connString);
            if (string.IsNullOrEmpty(connStringBuilder.UserID) &&
               string.IsNullOrEmpty(connStringBuilder.Password) &&
               connStringBuilder.Authentication == SqlAuthenticationMethod.NotSpecified)
            {
                _defaultAccessToken =
                    await AzureCredential.GetTokenAsync(
                        new TokenRequestContext(new[] { DATABASE_SCOPE }));

                return _defaultAccessToken?.Token;
            }

            return null;
        }
    }
}
