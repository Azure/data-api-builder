using System.Data.Common;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.Resolvers
{
    /// <summary>
    /// Interface representing services for managing the database connection.
    /// </summary>
    public interface IDbConnectionService : IClientProvider<DbConnection>
    {
        /// <summary>
        /// Gets an open connection to the given database.
        /// The caller is responsible for closing the connection.
        /// </summary>
        /// <param name="databaseName">Database name.</param>
        /// <returns>Opened sql connection.</returns>
        public Task<DbConnection> GetOpenedConnection(string databaseName);
    }
}
