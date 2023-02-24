// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;

namespace Azure.DataApiBuilder.Service.Resolvers
{
    /// <summary>
    /// Encapsulates query execution apis.
    /// </summary>
    public class QueryExecutor<TConnection> : IQueryExecutor
        where TConnection : DbConnection, new()
    {
        protected DbExceptionParser DbExceptionParser { get; }
        protected ILogger<IQueryExecutor> QueryExecutorLogger { get; }
        private RuntimeConfigProvider ConfigProvider { get; }

        // The maximum number of attempts that can be made to execute the query successfully in addition to the first attempt.
        // So to say in case of transient exceptions, the query will be executed (_maxRetryCount + 1) times at max.
        private static int _maxRetryCount = 5;

        private AsyncRetryPolicy _retryPolicy;

        public virtual DbConnectionStringBuilder ConnectionStringBuilder { get; set; }

        public QueryExecutor(DbExceptionParser dbExceptionParser,
                             ILogger<IQueryExecutor> logger,
                             DbConnectionStringBuilder connectionStringBuilder,
                             RuntimeConfigProvider configProvider)
        {
            DbExceptionParser = dbExceptionParser;
            QueryExecutorLogger = logger;
            ConnectionStringBuilder = connectionStringBuilder;
            ConfigProvider = configProvider;
            _retryPolicy = Polly.Policy
            .Handle<DbException>(DbExceptionParser.IsTransientException)
            .WaitAndRetryAsync(
                retryCount: _maxRetryCount,
                sleepDurationProvider: (attempt) => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (exception, backOffTime) =>
                {
                    QueryExecutorLogger.LogError(exception.Message);
                    QueryExecutorLogger.LogError(exception.StackTrace);
                });
        }

        /// <inheritdoc/>
        public virtual async Task<TResult?> ExecuteQueryAsync<TResult>(
            string sqltext,
            IDictionary<string, object?> parameters,
            Func<DbDataReader, List<string>?, Task<TResult>>? dataReaderHandler,
            HttpContext? httpContext = null,
            List<string>? args = null)
        {
            int retryAttempt = 0;
            using TConnection conn = new()
            {
                ConnectionString = ConnectionStringBuilder.ConnectionString,
            };

            await SetManagedIdentityAccessTokenIfAnyAsync(conn);

            return await _retryPolicy.ExecuteAsync(async () =>
            {
                retryAttempt++;
                try
                {
                    // When IsLateConfigured is true we are in a hosted scenario and do not reveal query information.
                    if (!ConfigProvider.IsLateConfigured)
                    {
                        QueryExecutorLogger.LogDebug($"Executing query: \n{sqltext}");
                    }

                    TResult? result =
                        await ExecuteQueryAgainstDbAsync(conn,
                            sqltext,
                            parameters,
                            dataReaderHandler,
                            httpContext,
                            args);
                    if (retryAttempt > 1)
                    {
                        // This implies that the request got successfully executed during one of retry attempts.
                        QueryExecutorLogger.LogInformation($"Request executed successfully in {retryAttempt} attempt of" +
                            $"{_maxRetryCount + 1} available attempts.");
                    }

                    return result;
                }
                catch (DbException e)
                {
                    if (DbExceptionParser.IsTransientException((DbException)e) && retryAttempt < _maxRetryCount + 1)
                    {
                        throw e;
                    }
                    else
                    {
                        QueryExecutorLogger.LogError(e.Message);
                        QueryExecutorLogger.LogError(e.StackTrace);

                        // Throw custom DABException
                        throw DbExceptionParser.Parse(e);
                    }
                }
            });
        }

        /// <summary>
        /// Method to execute sql query against the database.
        /// </summary>
        /// <param name="conn">Connection object used to connect to database.</param>
        /// <param name="sqltext">Sql text to be executed.</param>
        /// <param name="parameters">The parameters used to execute the SQL text.</param>
        /// <param name="dataReaderHandler">The function to invoke to handle the results
        /// in the DbDataReader obtained after executing the query.</param>
        /// <param name="httpContext">Current user httpContext.</param>
        /// <param name="args">List of string arguments to the DbDataReader handler.</param>
        /// <returns>An object formed using the results of the query as returned by the given handler.</returns>
        public virtual async Task<TResult?> ExecuteQueryAgainstDbAsync<TResult>(
            TConnection conn,
            string sqltext,
            IDictionary<string, object?> parameters,
            Func<DbDataReader, List<string>?, Task<TResult>>? dataReaderHandler,
            HttpContext? httpContext,
            List<string>? args = null)
        {
            await conn.OpenAsync();
            DbCommand cmd = conn.CreateCommand();
            cmd.CommandType = CommandType.Text;

            // Add query to send user data from DAB to the underlying database to enable additional security the user might have configured
            // at the database level.
            string sessionParamsQuery = GetSessionParamsQuery(httpContext, parameters);
            //"EXEC sp_set_session_context 'roles', 'Anonymous', @read_only =1 ;";

            cmd.CommandText = sessionParamsQuery + sqltext;
            if (parameters is not null)
            {
                foreach (KeyValuePair<string, object?> parameterEntry in parameters)
                {
                    DbParameter parameter = cmd.CreateParameter();
                    parameter.ParameterName = parameterEntry.Key;
                    parameter.Value = parameterEntry.Value ?? DBNull.Value;
                    cmd.Parameters.Add(parameter);
                }
            }

            try
            {
                using DbDataReader dbDataReader = await cmd.ExecuteReaderAsync(CommandBehavior.CloseConnection);
                if (dataReaderHandler is not null && dbDataReader is not null)
                {
                    return await dataReaderHandler(dbDataReader, args);
                }
                else
                {
                    return default(TResult);
                }
            }
            catch (DbException e)
            {
                QueryExecutorLogger.LogError(e.Message);
                QueryExecutorLogger.LogError(e.StackTrace);
                throw DbExceptionParser.Parse(e);
            }
        }

        /// <inheritdoc />
        public virtual string GetSessionParamsQuery(HttpContext? httpContext, IDictionary<string, object?> parameters)
        {
            return string.Empty;
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
        public async Task<DbResultSet>
            ExtractRowsFromResultSet(DbDataReader dbDataReader, List<string>? args = null)
        {
            DbResultSet dbResultSet =
                new(resultProperties: GetResultProperties(dbDataReader).Result ?? new());

            while (await ReadAsync(dbDataReader))
            {
                if (dbDataReader.HasRows)
                {
                    DbResultSetRow row = new();
                    DataTable? schemaTable = dbDataReader.GetSchemaTable();

                    if (schemaTable is not null)
                    {
                        foreach (DataRow schemaRow in schemaTable.Rows)
                        {
                            string columnName = (string)schemaRow["ColumnName"];

                            if (args is not null && !args.Contains(columnName))
                            {
                                continue;
                            }

                            int colIndex = dbDataReader.GetOrdinal(columnName);
                            if (!dbDataReader.IsDBNull(colIndex))
                            {
                                row.Columns.Add(columnName, dbDataReader[columnName]);
                            }
                            else
                            {
                                row.Columns.Add(columnName, value: null);
                            }
                        }
                    }

                    dbResultSet.Rows.Add(row);
                }
            }

            return dbResultSet;
        }

        /// <inheritdoc />
        /// <Note>This function is a DbDataReader handler of type Func<DbDataReader, List<string>?, Task<TResult?>>
        /// The parameter args is not used but is added to conform to the signature of the DbDataReader handler
        /// function argument of ExecuteQueryAsync.</Note>
        public async Task<JsonArray> GetJsonArrayAsync(
            DbDataReader dbDataReader,
            List<string>? args = null)
        {
            DbResultSet dbResultSet = await ExtractRowsFromResultSet(dbDataReader);
            JsonArray resultArray = new();

            foreach (DbResultSetRow dbResultSetRow in dbResultSet.Rows)
            {
                if (dbResultSetRow.Columns.Count > 0)
                {
                    JsonElement result =
                        JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(dbResultSetRow.Columns));
                    resultArray.Add(result);
                }
            }

            return resultArray;
        }

        /// <inheritdoc />
        /// <Note>This function is a DbDataReader handler of type Func<DbDataReader, List<string>?, Task<TResult?>>
        /// The parameter args is not used but is added to conform to the signature of the DbDataReader handler
        /// function argument of ExecuteQueryAsync.</Note>
        public async Task<TResult?> GetJsonResultAsync<TResult>(
            DbDataReader dbDataReader,
            List<string>? args = null)
        {
            TResult? jsonResult = default;

            // Parse Results into Json and return
            if (dbDataReader.HasRows)
            {
                // Make sure to get the complete json string in case of large document.
                jsonResult =
                    JsonSerializer.Deserialize<TResult>(
                        await GetJsonStringFromDbReader(dbDataReader));
            }
            else
            {
                QueryExecutorLogger.LogInformation("Did not return any rows in the JSON result.");
            }

            return jsonResult;
        }

        /// <inheritdoc />
        /// <Note>This function is a DbDataReader handler of type
        /// Func<DbDataReader, List<string>?, Task<TResult?>></Note>
        public async Task<DbResultSet> GetMultipleResultSetsIfAnyAsync(
            DbDataReader dbDataReader, List<string>? args = null)
        {
            DbResultSet dbResultSet
                = await ExtractRowsFromResultSet(dbDataReader);

            /// Processes a second result set from DbDataReader if it exists.
            /// In MsSQL upsert:
            /// result set #1: result of the UPDATE operation.
            /// result set #2: result of the INSERT operation.
            if (dbResultSet.Rows.Count > 0 && dbResultSet.Rows.FirstOrDefault()!.Columns.Count > 0)
            {
                dbResultSet.ResultProperties.Add(SqlMutationEngine.IS_FIRST_RESULT_SET, true);
                return dbResultSet;
            }
            else if (await dbDataReader.NextResultAsync())
            {
                // Since no first result set exists, we return the second result set.
                return await ExtractRowsFromResultSet(dbDataReader);
            }
            else
            {
                // This is the case where UPDATE and INSERT both return no results.
                // e.g. a situation where the item with the given PK doesn't exist so there's
                // no update and PK is auto generated so no insert can happen.
                if (args is not null && args.Count == 2)
                {
                    string prettyPrintPk = args[0];
                    string entityName = args[1];

                    throw new DataApiBuilderException(
                        message: $"Cannot perform INSERT and could not find {entityName} " +
                            $"with primary key {prettyPrintPk} to perform UPDATE on.",
                            statusCode: HttpStatusCode.NotFound,
                            subStatusCode: DataApiBuilderException.SubStatusCodes.EntityNotFound);
                }
            }

            return dbResultSet;
        }

        /// <inheritdoc />
        /// <Note>This function is a DbDataReader handler of type
        /// Func<DbDataReader, List<string>?, Task<TResult?>></Note>
        public Task<Dictionary<string, object>> GetResultProperties(
            DbDataReader dbDataReader,
            List<string>? columnNames = null)
        {
            Dictionary<string, object> resultProperties = new()
            {
                { nameof(dbDataReader.RecordsAffected), dbDataReader.RecordsAffected },
                { nameof(dbDataReader.HasRows), dbDataReader.HasRows }
            };
            return Task.FromResult(resultProperties);
        }

        private async Task<string> GetJsonStringFromDbReader(DbDataReader dbDataReader)
        {
            StringBuilder jsonString = new();
            // Even though we only return a single cell, we need this loop for
            // MS SQL. Sadly it splits FOR JSON PATH output across multiple
            // rows if the JSON consists of more than 2033 bytes:
            // Sources:
            // 1. https://docs.microsoft.com/en-us/sql/relational-databases/json/format-query-results-as-json-with-for-json-sql-server?view=sql-server-2017#output-of-the-for-json-clause
            // 2. https://stackoverflow.com/questions/54973536/for-json-path-results-in-ssms-truncated-to-2033-characters/54973676
            // 3. https://docs.microsoft.com/en-us/sql/relational-databases/json/use-for-json-output-in-sql-server-and-in-client-apps-sql-server?view=sql-server-2017#use-for-json-output-in-a-c-client-app
            while (await ReadAsync(dbDataReader))
            {
                jsonString.Append(dbDataReader.GetString(0));
            }

            return jsonString.ToString();
        }
    }
}
