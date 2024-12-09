// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    /// <summary>
    /// The TopNExtractor class is responsible for retrieving a specified number of recent records 
    /// from an Azure Cosmos DB container, optionally filtering by a maximum number of days.
    /// </summary>
    public class TopNExtractor : ISchemaGeneratorSampler
    {
        // Default Configuration
        private const int NUMBER_OF_RECORDS = 10;
        internal const int MAX_DAYS = 0;

        // Query
        public const string SELECT_QUERY = "SELECT TOP {0} * FROM c {1} ORDER BY c._ts desc";

        private int _numberOfRecords;
        private int _maxDays;
        private ILogger _logger;

        private CosmosExecutor _cosmosExecutor;

        /// <summary>
        /// Initializes a new instance of the TopNExtractor class.
        /// </summary>
        /// <param name="container">The Azure Cosmos DB container from which to retrieve data.</param>
        /// <param name="numberOfRecords">Optional. The number of records to retrieve. Defaults to 10.</param>
        /// <param name="maxDays">Optional. The maximum number of days in the past from which to retrieve data. Defaults to 0 (no limit).</param>
        /// <param name="logger">The logger to use for logging information.</param>
        public TopNExtractor(Container container, int? numberOfRecords, int? maxDays, ILogger logger)
        {
            this._numberOfRecords = numberOfRecords ?? NUMBER_OF_RECORDS;
            this._maxDays = maxDays ?? MAX_DAYS;

            this._logger = logger;

            this._cosmosExecutor = new CosmosExecutor(container, logger);
        }

        /// <summary>
        /// Retrieves the top N records from the Azure Cosmos DB container, ordered by timestamp.
        /// If a maximum number of days is specified, only records within that timeframe are included.
        /// </summary>
        /// <returns>A list of JsonDocument objects representing the retrieved records.</returns>
        public async Task<List<JsonDocument>> GetSampleAsync()
        {
            _logger.LogInformation("Sampling Configuration is Count: {0}", _numberOfRecords);
            string daysFilterClause = string.Empty;

            if (_maxDays > 0)
            {
                _logger.LogInformation("Max number of days are configured as: {0}", _maxDays);
                daysFilterClause = $"WHERE c._ts >= {GetTimeStampThreshold()}";
            }

            string query = string.Format(SELECT_QUERY, _numberOfRecords, daysFilterClause);

            return await _cosmosExecutor.ExecuteQueryAsync<JsonDocument>(query);
        }

        /// <summary>
        /// Calculates the timestamp threshold based on the current UTC time and the maximum number of days.
        /// </summary>
        /// <returns>A Unix timestamp representing the earliest allowed record time.</returns>
        public virtual long GetTimeStampThreshold()
        {
            return new DateTimeOffset(DateTime.UtcNow.AddDays(-_maxDays)).ToUnixTimeSeconds();
        }
    }
}
