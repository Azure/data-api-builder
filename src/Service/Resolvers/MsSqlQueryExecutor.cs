using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Identity;
using Azure.DataApiBuilder.Service.Configurations;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    public class MsSqlQueryExecutor : QueryExecutor<SqlConnection>
    {
        private const string DATABASE_SCOPE = @"https://database.windows.net/.default";

        public MsSqlQueryExecutor(
            RuntimeConfigProvider runtimeConfigProvider,
            DbExceptionParser dbExceptionParser,
            ILogger<QueryExecutor<SqlConnection>> logger)
            : base(runtimeConfigProvider, dbExceptionParser, logger)
        {
        }

        /// <summary>
        /// Executes sql text that return result set.
        /// </summary>
        /// <param name="sqltext">Sql text to be executed.</param>
        /// <param name="parameters">The parameters used to execute the SQL text.</param>
        /// <returns>DbDataReader object for reading the result set.</returns>
        public override async Task<DbDataReader> ExecuteQueryAsync(
            string sqltext,
            IDictionary<string, object?> parameters)
        {
            SqlConnectionStringBuilder connStringBuilder = new(ConnectionString);
            using (SqlConnection conn = new(connStringBuilder.ConnectionString))
            {
                if (string.IsNullOrEmpty(connStringBuilder.UserID))
                {
                    DefaultAzureCredential credential = new();
                    AccessToken accessToken =
                        await credential.GetTokenAsync(
                            new TokenRequestContext(new[] { DATABASE_SCOPE }));
                    conn.AccessToken = accessToken.Token;
                }

                await conn.OpenAsync();
                DbCommand cmd = conn.CreateCommand();
                cmd.CommandText = sqltext;
                cmd.CommandType = CommandType.Text;
                if (parameters != null)
                {
                    foreach (KeyValuePair<string, object?> parameterEntry in parameters)
                    {
                        DbParameter parameter = cmd.CreateParameter();
                        parameter.ParameterName = "@" + parameterEntry.Key;
                        parameter.Value = parameterEntry.Value ?? DBNull.Value;
                        cmd.Parameters.Add(parameter);
                    }
                }

                try
                {
                    return await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection);
                }
                catch (DbException e)
                {
                    QueryExecutorLogger.LogError(e.Message);
                    QueryExecutorLogger.LogError(e.StackTrace);
                    throw DbExceptionParser.Parse(e);
                }
            }
        }
    }
}
