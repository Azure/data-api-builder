using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.configurations;

namespace Cosmos.GraphQL.Service.Resolvers
{
    /// <summary>
    /// Encapsulates query execution apis.
    /// </summary>
    public class QueryExecutor<ConnectionT> : IQueryExecutor
        where ConnectionT : DbConnection, new()
    {
        private readonly DatabaseConnection _databaseConnection;

        public QueryExecutor(DatabaseConnection databaseConnection)
        {
            _databaseConnection = databaseConnection;
        }

        /// <summary>
        /// Executes sql text that return result set.
        /// </summary>
        /// <param name="sqltext">Sql text to be executed.</param>
        /// <param name="parameters">The parameters used to execute the SQL text.</param>
        /// <returns>DbDataReader object for reading the result set.</returns>
        public async Task<DbDataReader> ExecuteQueryAsync(string sqltext, IDictionary<string, object> parameters)
        {
            var conn = new ConnectionT();
            conn.ConnectionString = _databaseConnection.Credentials.GetConnectionString();
            await conn.OpenAsync();
            DbCommand cmd = conn.CreateCommand();
            cmd.CommandText = sqltext;
            cmd.CommandType = CommandType.Text;
            if (parameters != null)
            {
                foreach (var parameterEntry in parameters)
                {
                    var parameter = cmd.CreateParameter();
                    parameter.ParameterName = "@" + parameterEntry.Key;
                    parameter.Value = parameterEntry.Value;
                    cmd.Parameters.Add(parameter);
                }
            }

            return await cmd.ExecuteReaderAsync(CommandBehavior.SequentialAccess | CommandBehavior.CloseConnection);
        }
    }
}
