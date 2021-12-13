using System.Collections.Generic;
using System.Data.Common;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Interface for query execution for Sql like databases (eg. MsSql, PostgreSql).
    /// </summary>
    public interface IQueryExecutor
    {
        /// <summary>
        /// Execute sql text with parameters and return result set.
        /// </summary>
        /// <param name="sqltext">SQL text to be executed.</param>
        /// <param name="parameters">The parameters used to execute the SQL text.</param>
        /// <returns>DbDataReader object for reading the result set.</returns>
        public Task<DbDataReader> ExecuteQueryAsync(string sqltext, IDictionary<string, object> parameters);
    }
}
