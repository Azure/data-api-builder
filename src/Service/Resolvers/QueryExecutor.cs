using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
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
        protected string ConnectionString { get; }
        protected DbExceptionParser DbExceptionParser { get; }
        protected ILogger<QueryExecutor<TConnection>> QueryExecutorLogger { get; }

        // The maximum number of attempts that can be made to execute the query successfully in addition to the first attempt.
        // So to say in case of transient exceptions, the query will be executed (_maxRetryCount + 1) times at max.
        private static int _maxRetryCount = 5;

        private static int _p;

        private AsyncRetryPolicy _retryPolicy;

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
                    QueryExecutorLogger.LogError(exception.Message);
                    QueryExecutorLogger.LogError(exception.StackTrace);
                });
        }

        /// <inheritdoc/>
        public virtual async Task<TResult?> ExecuteQueryAsync<TResult>(
            string sqltext,
            IDictionary<string, object?> parameters,
            Func<DbDataReader, List<string>?, Task<TResult?>>? dataReaderHandler,
            List<string>? args = null,
            HttpContext? httpContext = null)
        {
            int retryAttempt = 0;
            using TConnection conn = new()
            {
                ConnectionString = ConnectionString,
            };

            await SetManagedIdentityAccessTokenIfAnyAsync(conn);
            Dictionary<string, Claim> claimsDictionary = AuthorizationResolver.GetAllUserClaims(httpContext);

            return await _retryPolicy.ExecuteAsync(async () =>
            {
                retryAttempt++;
                try
                {
                    TResult? result =
                        await ExecuteQueryAgainstDbAsync(conn,
                            sqltext,
                            parameters,
                            dataReaderHandler,
                            args,
                            claimsDictionary);
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
        /// <param name="args">List of string arguments to the DbDataReader handler.</param>
        /// <returns>An object formed using the results of the query as returned by the given handler.</returns>
        public virtual async Task<TResult?> ExecuteQueryAgainstDbAsync<TResult>(
            TConnection conn,
            string sqltext,
            IDictionary<string, object?> parameters,
            Func<DbDataReader, List<string>?, Task<TResult?>>? dataReaderHandler,
            List<string>? args = null,
            Dictionary<string, Claim> claimsDictionary = null)
        {
            await conn.OpenAsync();
            DbCommand cmd = conn.CreateCommand();
            cmd.CommandType = CommandType.Text;
            string sessionMapQuery = GetSessionMapQuery(claimsDictionary);
            cmd.CommandText = sessionMapQuery + sqltext;
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

        private static string GetSessionMapQuery(Dictionary<string, Claim> claimsDictionary)
        {
            string sessionMapQuery = string.Empty;

            foreach ((string claimType, Claim claim) in claimsDictionary)
            {
                sessionMapQuery = sessionMapQuery + "EXEC sp_set_session_context " + $"'{claimType}'," + GetClaimValue(claim) + ";";
            }

            return sessionMapQuery;
        }

        private static string GetClaimValue(Claim claim)
        {
            switch (claim.ValueType)
            {
                case ClaimValueTypes.String:
                    return $"'{claim.Value}'";
                case ClaimValueTypes.Boolean:
                case ClaimValueTypes.Integer:
                case ClaimValueTypes.Integer32:
                case ClaimValueTypes.Integer64:
                case ClaimValueTypes.UInteger32:
                case ClaimValueTypes.UInteger64:
                case ClaimValueTypes.Double:
                    return $"{claim.Value}";
                default:
                    return "null";
            }
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
        public async Task<Tuple<Dictionary<string, object?>?, Dictionary<string, object>>?>
            ExtractRowFromDbDataReader(DbDataReader dbDataReader, List<string>? args = null)
        {
            Dictionary<string, object?> row = new();

            Dictionary<string, object> propertiesOfResult = GetResultProperties(dbDataReader).Result ?? new();

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

                            if (args is not null && !args.Contains(columnName))
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
                return new Tuple<Dictionary<string, object?>?, Dictionary<string, object>>(null, propertiesOfResult);
            }

            return new Tuple<Dictionary<string, object?>?, Dictionary<string, object>>(row, propertiesOfResult);
        }

        /// <inheritdoc />
        /// <Note>This function is a DbDataReader handler of type Func<DbDataReader, List<string>?, Task<TResult?>>
        /// The parameter args is not used but is added to conform to the signature of the DbDataReader handler
        /// function argument of ExecuteQueryAsync.</Note>
        public async Task<JsonArray?> GetJsonArrayAsync(
            DbDataReader dbDataReader,
            List<string>? args = null)
        {
            Tuple<Dictionary<string, object?>?, Dictionary<string, object>>? resultRowAndProperties;
            JsonArray resultArray = new();

            while ((resultRowAndProperties = await ExtractRowFromDbDataReader(dbDataReader)) is not null &&
                resultRowAndProperties.Item1 is not null)
            {
                JsonElement result =
                    JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(resultRowAndProperties.Item1));
                resultArray.Add(result);
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
        public async Task<Tuple<Dictionary<string, object?>?, Dictionary<string, object>>?> GetMultipleResultSetsIfAnyAsync(
            DbDataReader dbDataReader, List<string>? args = null)
        {
            Tuple<Dictionary<string, object?>?, Dictionary<string, object>>? resultRecordWithProperties
                = await ExtractRowFromDbDataReader(dbDataReader);

            /// Processes a second result set from DbDataReader if it exists.
            /// In MsSQL upsert:
            /// result set #1: result of the UPDATE operation.
            /// result set #2: result of the INSERT operation.
            if (resultRecordWithProperties is not null && resultRecordWithProperties.Item1 is not null)
            {
                resultRecordWithProperties.Item2.Add(SqlMutationEngine.IS_FIRST_RESULT_SET, true);
                return new Tuple<Dictionary<string, object?>?, Dictionary<string, object>>
                    (resultRecordWithProperties.Item1, resultRecordWithProperties.Item2);
            }
            else if (await dbDataReader.NextResultAsync())
            {
                // Since no first result set exists, we return the second result set.
                return await ExtractRowFromDbDataReader(dbDataReader);
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

            return null;
        }

        /// <inheritdoc />
        /// <Note>This function is a DbDataReader handler of type
        /// Func<DbDataReader, List<string>?, Task<TResult?>></Note>
        public Task<Dictionary<string, object>?> GetResultProperties(
            DbDataReader dbDataReader,
            List<string>? columnNames = null)
        {
            Dictionary<string, object>? propertiesOfResult = new();
            propertiesOfResult.Add(nameof(dbDataReader.RecordsAffected), dbDataReader.RecordsAffected);
            propertiesOfResult.Add(nameof(dbDataReader.HasRows), dbDataReader.HasRows);
            return Task.FromResult((Dictionary<string, object>?)propertiesOfResult);
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
