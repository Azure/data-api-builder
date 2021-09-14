using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace Microsoft.Sql.Rest.Utils
{
    /// <summary>
    /// Interface representing services for managing the database connection.
    /// </summary>
    public interface IDbConnectionService
    {
        /// <summary>
        /// Get a connection to a database. Cache the connection string after the first access.
        /// The caller is responsible for closing the connection.
        /// </summary>
        /// <param name="databaseName">Database name.</param>
        /// <returns>Opened sql connection.</returns>
        public Task<SqlConnection> GetOpenedConnection(string databaseName);
    }
}
