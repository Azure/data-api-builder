// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Data.Common;
using System.Text.Json;
using System.Text.Json.Nodes;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Microsoft.AnalysisServices.AdomdClient;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Resolvers
{
    /// <summary>
    /// Query executor for Semantic Models (Analysis Services / Power BI / Fabric) using ADOMD.NET.
    /// Executes DAX queries against XMLA endpoints or local Analysis Services instances.
    /// </summary>
    public class SemanticModelQueryExecutor : IQueryExecutor
    {
        private readonly RuntimeConfigProvider _runtimeConfigProvider;
        private readonly DbExceptionParser _dbExceptionParser;
        private readonly ILogger<IQueryExecutor> _logger;

        public SemanticModelQueryExecutor(
            RuntimeConfigProvider runtimeConfigProvider,
            DbExceptionParser dbExceptionParser,
            ILogger<IQueryExecutor> logger)
        {
            _runtimeConfigProvider = runtimeConfigProvider;
            _dbExceptionParser = dbExceptionParser;
            _logger = logger;
        }

        /// <summary>
        /// Gets the connection string for the specified data source.
        /// </summary>
        private string GetConnectionString(string dataSourceName)
        {
            DataSource dataSource = _runtimeConfigProvider.GetConfig().GetDataSourceFromDataSourceName(dataSourceName);
            return dataSource.ConnectionString;
        }

        /// <summary>
        /// Creates a new AdomdConnection for the specified data source.
        /// </summary>
        private AdomdConnection CreateConnection(string dataSourceName)
        {
            string connectionString = GetConnectionString(dataSourceName);
            return new AdomdConnection(connectionString);
        }

        /// <inheritdoc/>
        public async Task<TResult?> ExecuteQueryAsync<TResult>(
            string sqltext,
            IDictionary<string, DbConnectionParam> parameters,
            Func<DbDataReader, List<string>?, Task<TResult>>? dataReaderHandler,
            string dataSourceName,
            HttpContext? httpContext = null,
            List<string>? args = null)
        {
            using AdomdConnection connection = CreateConnection(dataSourceName);

            // AdomdConnection.Open is synchronous; wrap in Task.Run to avoid blocking
            await Task.Run(() => connection.Open());

            using AdomdCommand command = connection.CreateCommand();
            command.CommandText = sqltext;

            try
            {
                using AdomdDataReader reader = command.ExecuteReader();

                if (dataReaderHandler is not null)
                {
                    // AdomdDataReader does NOT extend DbDataReader.
                    // We read the results directly and bypass the handler.
                    JsonArray jsonArray = ReadAdomdResultsToJsonArray(reader);
                    string json = jsonArray.ToJsonString();
                    return JsonSerializer.Deserialize<TResult>(json);
                }

                return default;
            }
            catch (AdomdException ex)
            {
                _logger.LogError(ex, "Error executing DAX query against semantic model.");
                throw;
            }
        }

        /// <inheritdoc/>
        public TResult? ExecuteQuery<TResult>(
            string sqltext,
            IDictionary<string, DbConnectionParam> parameters,
            Func<DbDataReader, List<string>?, TResult>? dataReaderHandler,
            HttpContext? httpContext = null,
            List<string>? args = null,
            string dataSourceName = "")
        {
            using AdomdConnection connection = CreateConnection(dataSourceName);
            connection.Open();

            using AdomdCommand command = connection.CreateCommand();
            command.CommandText = sqltext;

            try
            {
                using AdomdDataReader reader = command.ExecuteReader();

                if (dataReaderHandler is not null)
                {
                    // AdomdDataReader does NOT extend DbDataReader.
                    // We read the results directly and bypass the handler.
                    JsonArray jsonArray = ReadAdomdResultsToJsonArray(reader);
                    string json = jsonArray.ToJsonString();
                    return JsonSerializer.Deserialize<TResult>(json);
                }

                return default;
            }
            catch (AdomdException ex)
            {
                _logger.LogError(ex, "Error executing DAX query against semantic model.");
                throw;
            }
        }

        /// <summary>
        /// Reads all rows from an AdomdDataReader into a JsonArray.
        /// AdomdDataReader does not extend DbDataReader, so we use this
        /// method instead of the standard DbDataReader-based handlers.
        /// Column names are cleaned by stripping table prefixes (e.g., 'table'[Column] → Column)
        /// and bracket notation (e.g., [Column] → Column).
        /// </summary>
        private static JsonArray ReadAdomdResultsToJsonArray(AdomdDataReader reader)
        {
            JsonArray jsonArray = new();
            while (reader.Read())
            {
                JsonObject row = new();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    string columnName = CleanColumnName(reader.GetName(i));
                    object value = reader.IsDBNull(i) ? null! : reader.GetValue(i);
                    row.Add(columnName, JsonValue.Create(value));
                }

                jsonArray.Add(row);
            }

            return jsonArray;
        }

        /// <summary>
        /// Cleans a DAX column name by stripping table prefix and bracket notation.
        /// Examples: "'customer'[CustomerName]" → "CustomerName", "[City]" → "City", "Status" → "Status"
        /// </summary>
        private static string CleanColumnName(string rawName)
        {
            // Strip table prefix: 'tablename'[ColumnName] → [ColumnName]
            int bracketStart = rawName.IndexOf('[');
            if (bracketStart >= 0)
            {
                rawName = rawName[bracketStart..];
            }

            // Strip bracket notation: [ColumnName] → ColumnName
            if (rawName.StartsWith('[') && rawName.EndsWith(']'))
            {
                return rawName[1..^1];
            }

            return rawName;
        }

        /// <inheritdoc/>
        public async Task<JsonArray> GetJsonArrayAsync(
            DbDataReader dbDataReader,
            List<string>? args = null)
        {
            JsonArray jsonArray = new();

            while (await dbDataReader.ReadAsync())
            {
                JsonObject row = new();
                for (int i = 0; i < dbDataReader.FieldCount; i++)
                {
                    string columnName = dbDataReader.GetName(i);
                    object value = dbDataReader.IsDBNull(i) ? null! : dbDataReader.GetValue(i);
                    row.Add(columnName, JsonValue.Create(value));
                }

                jsonArray.Add(row);
            }

            return jsonArray;
        }

        /// <inheritdoc/>
        public async Task<TResult?> GetJsonResultAsync<TResult>(
            DbDataReader dbDataReader,
            List<string>? args = null)
        {
            JsonArray resultArray = await GetJsonArrayAsync(dbDataReader, args);
            string json = resultArray.ToJsonString();
            return JsonSerializer.Deserialize<TResult>(json);
        }

        /// <inheritdoc/>
        public async Task<DbResultSet> ExtractResultSetFromDbDataReaderAsync(
            DbDataReader dbDataReader,
            List<string>? args = null)
        {
            DbResultSet resultSet = new(
                resultProperties: new Dictionary<string, object>
                {
                    { nameof(dbDataReader.RecordsAffected), dbDataReader.RecordsAffected },
                    { nameof(dbDataReader.HasRows), dbDataReader.HasRows }
                });

            while (await dbDataReader.ReadAsync())
            {
                DbResultSetRow row = new();
                for (int i = 0; i < dbDataReader.FieldCount; i++)
                {
                    string columnName = dbDataReader.GetName(i);
                    object? value = dbDataReader.IsDBNull(i) ? null : dbDataReader.GetValue(i);
                    row.Columns.Add(columnName, value);
                }

                resultSet.Rows.Add(row);
            }

            return resultSet;
        }

        /// <inheritdoc/>
        public DbResultSet ExtractResultSetFromDbDataReader(
            DbDataReader dbDataReader,
            List<string>? args = null)
        {
            DbResultSet resultSet = new(
                resultProperties: new Dictionary<string, object>
                {
                    { nameof(dbDataReader.RecordsAffected), dbDataReader.RecordsAffected },
                    { nameof(dbDataReader.HasRows), dbDataReader.HasRows }
                });

            while (dbDataReader.Read())
            {
                DbResultSetRow row = new();
                for (int i = 0; i < dbDataReader.FieldCount; i++)
                {
                    string columnName = dbDataReader.GetName(i);
                    object? value = dbDataReader.IsDBNull(i) ? null : dbDataReader.GetValue(i);
                    row.Columns.Add(columnName, value);
                }

                resultSet.Rows.Add(row);
            }

            return resultSet;
        }

        /// <inheritdoc/>
        public async Task<DbResultSet> GetMultipleResultSetsIfAnyAsync(
            DbDataReader dbDataReader,
            List<string>? args = null)
        {
            // Semantic models don't support multiple result sets
            return await ExtractResultSetFromDbDataReaderAsync(dbDataReader, args);
        }

        /// <inheritdoc/>
        public async Task<Dictionary<string, object>> GetResultPropertiesAsync(
            DbDataReader dbDataReader,
            List<string>? args = null)
        {
            await Task.CompletedTask;
            return new Dictionary<string, object>
            {
                { nameof(dbDataReader.RecordsAffected), dbDataReader.RecordsAffected },
                { nameof(dbDataReader.HasRows), dbDataReader.HasRows }
            };
        }

        /// <inheritdoc/>
        public Dictionary<string, object> GetResultProperties(
            DbDataReader dbDataReader,
            List<string>? args = null)
        {
            return new Dictionary<string, object>
            {
                { nameof(dbDataReader.RecordsAffected), dbDataReader.RecordsAffected },
                { nameof(dbDataReader.HasRows), dbDataReader.HasRows }
            };
        }

        /// <inheritdoc/>
        public async Task<bool> ReadAsync(DbDataReader reader)
        {
            try
            {
                return await reader.ReadAsync();
            }
            catch (AdomdException ex)
            {
                _logger.LogError(ex, "Error reading from ADOMD data reader.");
                throw;
            }
        }

        /// <inheritdoc/>
        public bool Read(DbDataReader reader)
        {
            try
            {
                return reader.Read();
            }
            catch (AdomdException ex)
            {
                _logger.LogError(ex, "Error reading from ADOMD data reader.");
                throw;
            }
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Semantic model connections may use Entra ID tokens. This is handled
        /// via the connection string properties (e.g., Password=<token>) rather
        /// than modifying the connection after creation.
        /// </remarks>
        public Task SetManagedIdentityAccessTokenIfAnyAsync(DbConnection conn, string dataSourceName)
        {
            // ADOMD.NET handles authentication through the connection string.
            // For Entra ID, the token is typically provided via connection string properties.
            return Task.CompletedTask;
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Semantic models do not support session context parameters.
        /// </remarks>
        public string GetSessionParamsQuery(HttpContext? httpContext, IDictionary<string, DbConnectionParam> parameters, string dataSourceName)
        {
            return string.Empty;
        }

        /// <inheritdoc/>
        public void PopulateDbTypeForParameter(KeyValuePair<string, DbConnectionParam> parameterEntry, DbParameter parameter)
        {
            // ADOMD.NET does not use parameterized queries in the same way as SQL databases.
            // DAX queries use inline values rather than DbParameters.
        }
    }
}
