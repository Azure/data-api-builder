using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;
using GraphQL.Execution;

namespace Cosmos.GraphQL.Service.Resolvers
{
    /// <summary>
    /// Interface for query execution.
    /// </summary>
    public interface IQueryExecutor
    {
        /// <summary>
        /// Execute sql text with parameters and return result set.
        /// </summary>
        /// <param name="sqltext">SQL text to be executed.</param>
        /// <param name="database">Database to execute the SQL text.</param>
        /// <param name="parameters">The parameters used to execute the SQL text.</param>
        /// <returns>DbDataReader object for reading the result set.</returns>
        public Task<DbDataReader> ExecuteQueryAsync(string sqltext, IDictionary<string, ArgumentValue> parameters);
    }
}
