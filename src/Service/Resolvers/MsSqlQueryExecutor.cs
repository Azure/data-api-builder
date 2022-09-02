using System.Data.Common;
using System.Threading.Tasks;
using Azure.Core;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    public class MsSqlQueryExecutor : QueryExecutor<SqlConnection>
    {
        public const string DATABASE_SCOPE = @"https://database.windows.net/.default";

        public MsSqlQueryExecutor(
            RuntimeConfigProvider runtimeConfigProvider,
            DbExceptionParser dbExceptionParser,
            ILogger<QueryExecutor<SqlConnection>> logger)
            : base(runtimeConfigProvider, dbExceptionParser, logger)
        {
        }

        /// <summary>
        /// Modified the properties of the supplied connection to support managed identity access.
        /// In the case of MsSql, gets access token if deemed necessary and sets it on the connection.
        /// </summary>
        /// <param name="conn">The supplied connection to modify for managed identity access.</param>
        public override async Task HandleManagedIdentityAccessIfAny(DbConnection conn)
        {
            SqlConnection sqlConn = (SqlConnection)conn;
            string? accessToken = await TryGetAccessTokenAsync(ConnectionString);
            if (accessToken is not null)
            {
                QueryExecutorLogger.LogTrace("Using access token obtained from DefaultAzureCredential to connect to database.");
                sqlConn.AccessToken = accessToken;
            }
        }

        /// <summary>
        /// Determines if access token needs to be obtained or not based on the
        /// properties specified in the connection string.
        /// </summary>
        /// <param name="connString"></param>
        /// <returns>True when access token should be obtained, false otherwise.</returns>
        public static async Task<string?> TryGetAccessTokenAsync(string connString)
        {
            SqlConnectionStringBuilder connStringBuilder = new(connString);
            if (string.IsNullOrEmpty(connStringBuilder.UserID) &&
               string.IsNullOrEmpty(connStringBuilder.Password) &&
               connStringBuilder.Authentication == SqlAuthenticationMethod.NotSpecified)
            {
                DefaultAzureCredential credential = new();
                AccessToken defaultAccessToken =
                    await credential.GetTokenAsync(
                        new TokenRequestContext(new[] { DATABASE_SCOPE }));

                return defaultAccessToken.Token;
            }

            return null;
        }
    }
}
