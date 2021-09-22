using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.Resolvers
{
    /// <summary>
    /// Encapsulates query execution apis.
    /// </summary>
    public class QueryExecutor : IQueryExecutor
    {
        /// <summary>
        /// The clientProvider useful for connecting to the backend.
        /// </summary>
        private readonly IDbConnectionService _clientProvider;

        /// <summary>
        /// Initializes a new instance of the <see cref="QueryExecutor"/> class.
        /// Constructor for QueryExecutor.
        /// </summary>
        /// <param name="clientProvider">ClientProvider for setting up connection.</param>
        public QueryExecutor(IDbConnectionService clientProvider)
        {
            _clientProvider = clientProvider;
        }

        /// <summary>
        /// Executes sql text that return result set.
        /// </summary>
        /// <param name="sqltext">Sql text to be executed.</param>
        /// <param name="databaseName">Database to execute the SQL text.</param>
        /// <returns>DbDataReader object for reading the result set.</returns>
        public async Task<DbDataReader> ExecuteQueryAsync(string sqltext, string databaseName, List<IDataParameter> parameters)
        {
            DbConnection conn = await _clientProvider.GetOpenedConnectionAsync(databaseName);
            DbCommand cmd = conn.CreateCommand();
            cmd.CommandText = sqltext;
            cmd.CommandType = CommandType.Text;

            if (parameters != null)
            {
                foreach (IDataParameter parameter in parameters)
                {
                    cmd.Parameters.Add(parameter);
                }
            }

            return await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.CloseConnection);
        }
    }
}
