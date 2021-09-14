using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.Sql.Rest.Utils;

namespace Microsoft.Sql.Rest.QueryHandler
{
    /// <summary>
    /// Execute the query and return the response.
    /// </summary>
    public class QueryExecutor : IQueryExecutor
    {
        private readonly IDbConnectionService _dbConnectionService;

        /// <summary>
        /// Initializes a new instance of the <see cref="QueryExecutor"/> class.
        /// Constructor for QueryExecutor. We are using .NET dependency injection to inject IDbConnectionService.
        /// </summary>
        /// <param name="dbConnectionService">DbConnectionService for setting up connection.</param>
        public QueryExecutor(IDbConnectionService dbConnectionService)
        {
            _dbConnectionService = dbConnectionService;
        }

        /// <summary>
        /// Execute sql text that return result set.
        /// </summary>
        /// <param name="sqltext">SQL text to be executed.</param>
        /// <param name="databaseName">Database to execute the SQL text.</param>
        /// <returns>DbDataReader object for reading the result set.</returns>
        public async Task<DbDataReader> ExecuteQueryAsync(string sqltext, string databaseName)
        {
            return await ExecuteQueryAsync(sqltext, databaseName, null);
        }

        /// <summary>
        /// Execute sql text that return result set.
        /// </summary>
        /// <param name="sqltext">SQL text to be executed.</param>
        /// <param name="databaseName">Database to execute the SQL text.</param>
        /// <returns>DbDataReader object for reading the result set.</returns>
        public async Task<DbDataReader> ExecuteQueryAsync(string sqltext, string databaseName, List<IDataParameter> parameters)
        {
            SqlConnection conn = await _dbConnectionService.GetOpenedConnection(databaseName);
            SqlCommand cmd = new SqlCommand(sqltext, conn);

            if (parameters != null)
            {
                foreach(SqlParameter parameter in parameters)
                {
                    cmd.Parameters.Add(parameter);
                }
            }

            return await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.CloseConnection);
        }

        /// <summary>
        /// Execute sql text returns no result set. For example, insert statement.
        /// </summary>
        /// <param name="sqltext">SQL text to be executed.</param>
        /// <param name="database">Database to execute the SQL text.</param>
        /// <returns>Task representing the async operation.</returns>
        public Task ExecuteNonQueryAsync(string sqltext, string database)
        {
            throw new NotImplementedException();
        }
    }
}