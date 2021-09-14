using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;

namespace Microsoft.Sql.Rest.QueryHandler
{
    /// <summary>
    /// Interface for query execution.
    /// </summary>
    public interface IQueryExecutor
    {
        /// <summary>
        /// Execute sql text that return result set.
        /// </summary>
        /// <param name="sqltext">SQL text to be executed.</param>
        /// <param name="database">Database to execute the SQL text.</param>
        /// <returns>DbDataReader object for reading the result set.</returns>
        public Task<DbDataReader> ExecuteQueryAsync(string sqltext, string database);

        /// <summary>
        /// Execute sql text with parameters and return result set.
        /// </summary>
        /// <param name="sqltext">SQL text to be executed.</param>
        /// <param name="database">Database to execute the SQL text.</param>
        /// <param name="parameters">The parameters used to execute the SQL text.</param>
        /// <returns>DbDataReader object for reading the result set.</returns>
        public Task<DbDataReader> ExecuteQueryAsync(string sqltext, string database, List<IDataParameter> parameters);

        /// <summary>
        /// Execute sql text returns no result set. For example, insert statement.
        /// </summary>
        /// <param name="sqltext">SQL text to be executed.</param>
        /// <param name="database">Database to execute the SQL text.</param>
        /// <returns>Task representing the async operation.</returns>
        public Task ExecuteNonQueryAsync(string sqltext, string database);
    }
}