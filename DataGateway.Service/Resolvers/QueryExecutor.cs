using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Microsoft.Extensions.Options;

namespace Azure.DataGateway.Service.Resolvers
{
    /// <summary>
    /// Encapsulates query execution apis.
    /// </summary>
    public class QueryExecutor<ConnectionT> : IQueryExecutor
        where ConnectionT : DbConnection, new()
    {
        private readonly string _connectionString;
        private readonly DbExceptionParserBase _dbExceptionParser;

        public QueryExecutor(IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath, DbExceptionParserBase dbExceptionParser)
        {
            Console.WriteLine("bla-bla-11");
            runtimeConfigPath.CurrentValue.
                ExtractConfigValues(
                    out _,
                    out _connectionString,
                    out _);
            Console.WriteLine("bla-bla-12");
            _dbExceptionParser = dbExceptionParser;
            Console.WriteLine("bla-bla-13");
        }

        /// <summary>
        /// Executes sql text that return result set.
        /// </summary>
        /// <param name="sqltext">Sql text to be executed.</param>
        /// <param name="parameters">The parameters used to execute the SQL text.</param>
        /// <returns>DbDataReader object for reading the result set.</returns>
        public async Task<DbDataReader> ExecuteQueryAsync(string sqltext, IDictionary<string, object?> parameters)
        {
            ConnectionT conn = new()
            {
                ConnectionString = _connectionString
            };
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
                Console.Error.WriteLine(e);
                throw _dbExceptionParser.Parse(e);
            }
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
                Console.Error.WriteLine(e);
                throw _dbExceptionParser.Parse(e);
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
