using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Cosmos.GraphQL.Service.configurations;

namespace Cosmos.GraphQL.Service.Resolvers
{
    /// <summary>
    /// Client provider for MsSql.
    /// </summary>
    public class MsSqlClientProvider : IDbConnectionService
    {
        /// <summary>
        /// Credentials provided in the config.
        /// </summary>
        private readonly MsSqlCredentials _sqlCredentials;

        /// <summary>
        /// Constructor
        /// </summary>
        public MsSqlClientProvider()
        {
            _sqlCredentials = (MsSqlCredentials)ConfigurationProvider.getInstance().Creds;
        }

        /// <summary>
        /// Gets an open connection using the connection string provided in appsettings.json.
        /// </summary>
        public DbConnection GetClient()
        {
            return GetOpenedConnection().Result;
        }

        /// <summary>
        /// Gets a connection to a database. The caller should close this connection.
        /// </summary>
        /// <param name="databaseName">Database name, optional. If not provided, the connection string
        /// from appsettings is used.</param>
        /// <returns>Opened sql connection.</returns>
        public async Task<DbConnection> GetOpenedConnection(string databaseName = "")
        {
            string connString = _sqlCredentials.ConnectionString;

            if (string.IsNullOrEmpty(connString))
            {
                connString = GetConnectionString(databaseName);
            }

            SqlConnection conn = new(connString);
            await conn.OpenAsync();
            return conn;
        }

        /// <summary>
        /// Constructs a connection string to the given database.
        /// </summary>
        /// <param name="databaseName">Database name.</param>
        /// <returns>the constructed connection string</returns>
        public string GetConnectionString(string databaseName)
        {
            SqlConnectionStringBuilder builder = new SqlConnectionStringBuilder
            {
                InitialCatalog = databaseName,
                DataSource = _sqlCredentials.Server,
            };

            builder.IntegratedSecurity = true;
            return builder.ToString();
        }
    }
}
