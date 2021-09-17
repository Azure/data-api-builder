using Npgsql;
using Cosmos.GraphQL.Service.configurations;
using System.Data.Common;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.Resolvers
{
    /// <summary>
    /// Creates, returns, and maintains NpgsqlConnection for all resources the make SQL database calls.
    /// </summary>
    public class PostgresClientProvider : IDbConnectionService
    {
        private string _connstring;
        private void init()
        {
            _connstring = ConfigurationProvider.getInstance().Creds.ConnectionString;
        }

        public async Task<DbConnection> GetOpenedConnection(string databaseName)
        {
            var connString = ConfigurationProvider.getInstance().Creds.ConnectionString;
            var conn = new NpgsqlConnection(connString);
            await conn.OpenAsync();
            return conn;
        }
    }
}

