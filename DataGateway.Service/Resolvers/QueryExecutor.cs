using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
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

        public async Task<DbDataReader> ExecuteNonQueryAsync(string sqltext, IDictionary<string, object> parameters)
        {
            ConnectionT conn = new();
            conn.ConnectionString = _datagatewayConfig.DatabaseConnection.ConnectionString;
            await conn.OpenAsync();
            DbCommand cmd = conn.CreateCommand();

            DbTransaction transaction;

            // Start a local transaction.A range lock is placed on the DataSet,
            // preventing other users from updating or inserting rows
            // into the dataset until the transaction is complete.
            transaction = conn.BeginTransaction(IsolationLevel.Serializable);

            // Must assign transaction object for a pending local transaction
            cmd.Transaction = transaction;

            try
            {
                cmd.CommandText = sqltext;
                cmd.CommandType = CommandType.Text;
                if (parameters != null)
                {
                    foreach (KeyValuePair<string, object> parameterEntry in parameters)
                    {
                        DbParameter parameter = cmd.CreateParameter();
                        parameter.ParameterName = "@" + parameterEntry.Key;
                        parameter.Value = parameterEntry.Value ?? DBNull.Value;
                        // INSERT, UPDATE operations should output changed values from params as well.
                        //parameter.Direction = ParameterDirection.InputOutput;
                        cmd.Parameters.Add(parameter);
                    }
                }

                //object result = cmd.ExecuteScalar();
                transaction.Commit();
            }
            catch (Exception ex)
            {
                Console.WriteLine("Commit Exception Type: {0}", ex.GetType());
                Console.WriteLine("  Message: {0}", ex.Message);

                // Attempt to roll back the transaction.
                try
                {
                    transaction.Rollback();
                }
                catch (Exception ex2)
                {
                    // This catch block will handle any errors that may have occurred
                    // on the server that would cause the rollback to fail, such as
                    // a closed connection.
                    Console.WriteLine("Rollback Exception Type: {0}", ex2.GetType());
                    Console.WriteLine("  Message: {0}", ex2.Message);
                    throw new DatagatewayException(
                        message: "transaction issue",
                        statusCode: 500,
                        subStatusCode: DatagatewayException.SubStatusCodes.DatabaseOperationFailed);
                }
            }

            return await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection);
        }
    }
}
