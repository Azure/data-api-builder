using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Microsoft.Extensions.Logging;
using Polly;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Encapsulates query execution apis.
    /// </summary>
    public class QueryExecutor<TConnection> : IQueryExecutor
        where TConnection : DbConnection, new()
    {
        protected string ConnectionString { get; }
        protected DbExceptionParser DbExceptionParser { get; }
        protected ILogger<QueryExecutor<TConnection>> QueryExecutorLogger { get; }

        private static int _maxRetryCount = 5;

        private static TimeSpan _maxBackOffTime = TimeSpan.FromSeconds(Math.Pow(2, _maxRetryCount));

        private Polly.Retry.AsyncRetryPolicy _retryPolicy;

        public QueryExecutor(RuntimeConfigProvider runtimeConfigProvider,
                             DbExceptionParser dbExceptionParser,
                             ILogger<QueryExecutor<TConnection>> logger)
        {
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetRuntimeConfiguration();

            ConnectionString = runtimeConfig.ConnectionString;
            DbExceptionParser = dbExceptionParser;
            QueryExecutorLogger = logger;
            _retryPolicy = Polly.Policy
            .Handle<DbException>(DbExceptionParser.IsTransientException)
            .WaitAndRetryAsync(
                retryCount: _maxRetryCount,
                sleepDurationProvider: (attempt) => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (exception, backOffTime) =>
                {
                    if (backOffTime == _maxBackOffTime)
                    {
                        QueryExecutorLogger.LogError(exception.Message);
                        QueryExecutorLogger.LogError(exception.StackTrace);
                        throw DbExceptionParser.Parse((DbException)exception);
                    }
                });
        }

        /// <summary>
        /// Executes sql text that return result set.
        /// </summary>
        /// <param name="sqltext">Sql text to be executed.</param>
        /// <param name="parameters">The parameters used to execute the SQL text.</param>
        /// <returns>DbDataReader object for reading the result set.</returns>
        public virtual async Task<DbDataReader> ExecuteQueryAsync(string sqltext, IDictionary<string, object?> parameters)
        {
            TConnection conn = new()
            {
                ConnectionString = ConnectionString,
            };
            await SetManagedIdentityAccessTokenIfAnyAsync(conn);

            return await _retryPolicy.ExecuteAsync(async () =>
            {
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
                    if (DbExceptionParser.IsTransientException((DbException)e))
                    {
                        throw e;
                    }
                    else
                    {
                        QueryExecutorLogger.LogError(e.Message);
                        QueryExecutorLogger.LogError(e.StackTrace);
                        throw DbExceptionParser.Parse((DbException)e);
                    }
                }
            });
        }

        /// <inheritdoc />
        public virtual async Task SetManagedIdentityAccessTokenIfAnyAsync(DbConnection conn)
        {
            // no-op in the base class.
            await Task.Yield();
        }

        /// <inheritdoc />
        public async Task<bool> ReadAsync(DbDataReader reader)
        {
            try
            {
                return await reader.ReadAsync();
            }
            catch (DbException e)
            {
                QueryExecutorLogger.LogError(e.Message);
                QueryExecutorLogger.LogError(e.StackTrace);
                throw DbExceptionParser.Parse(e);
            }
        }

        /// <inheritdoc />
        public async Task<Dictionary<string, object?>?> ExtractRowFromDbDataReader(DbDataReader dbDataReader, List<string>? onlyExtract = null)
        {
            Dictionary<string, object?> row = new();

            if (await ReadAsync(dbDataReader))
            {
                if (dbDataReader.HasRows)
                {
                    DataTable? schemaTable = dbDataReader.GetSchemaTable();

                    if (schemaTable != null)
                    {
                        foreach (DataRow schemaRow in schemaTable.Rows)
                        {
                            string columnName = (string)schemaRow["ColumnName"];

                            if (onlyExtract != null && !onlyExtract.Contains(columnName))
                            {
                                continue;
                            }

                            int colIndex = dbDataReader.GetOrdinal(columnName);
                            if (!dbDataReader.IsDBNull(colIndex))
                            {
                                row.Add(columnName, dbDataReader[columnName]);
                            }
                            else
                            {
                                row.Add(columnName, value: null);
                            }
                        }
                    }
                }
            }

            // no row was read
            if (row.Count == 0)
            {
                return null;
            }

            return row;
        }
    }
}
