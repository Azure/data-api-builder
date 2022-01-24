using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Configurations;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Encapsulates query execution apis.
    /// </summary>
    public class QueryExecutor<ConnectionT> : IQueryExecutor
        where ConnectionT : DbConnection, new()
    {
        private readonly DataGatewayConfig _datagatewayConfig;

        public QueryExecutor(IOptions<DataGatewayConfig> dataGatewayConfig)
        {
            _datagatewayConfig = dataGatewayConfig.Value;
        }

        /// <summary>
        /// Executes sql text that return result set.
        /// </summary>
        /// <param name="sqltext">Sql text to be executed.</param>
        /// <param name="parameters">The parameters used to execute the SQL text.</param>
        /// <returns>DbDataReader object for reading the result set.</returns>
        public async Task<DbDataReader> ExecuteQueryAsync(string sqltext, IDictionary<string, object> parameters)
        {
            ConnectionT conn = new();
            conn.ConnectionString = _datagatewayConfig.DatabaseConnection.ConnectionString;
            await conn.OpenAsync();
            DbCommand cmd = conn.CreateCommand();
            cmd.CommandText = sqltext;
            cmd.CommandType = CommandType.Text;
            if (parameters != null)
            {
                foreach (KeyValuePair<string, object> parameterEntry in parameters)
                {
                    DbParameter parameter = cmd.CreateParameter();
                    parameter.ParameterName = "@" + parameterEntry.Key;
                    parameter.Value = parameterEntry.Value ?? DBNull.Value;
                    cmd.Parameters.Add(parameter);
                }
            }

            return await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection);
        }
    }
}
