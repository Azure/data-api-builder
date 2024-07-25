// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    /// <summary>
    /// It Returns the Top N records from K days,from the Cosmos DB.
    /// If K is 0 or null, it will return the Top N records from the Cosmos DB.
    /// </summary>
    public class TopNSampler : ISchemaGeneratorSampler
    {
        // Default Configuration
        public const int NUMBER_OF_RECORDS = 10;
        public const int MAX_DAYS = 0;

        // Query
        public const string SELECT_QUERY = "SELECT TOP {0} * FROM c {1} ORDER BY c._ts desc";

        private int _numberOfRecords;
        private int _maxDays;
        private ILogger _logger;

        private CosmosExecutor _cosmosExecutor;

        public TopNSampler(Container container, int? numberOfRecords, int? maxDays, ILogger logger)
        {
            this._numberOfRecords = numberOfRecords ?? NUMBER_OF_RECORDS;
            this._maxDays = maxDays ?? MAX_DAYS;

            this._logger = logger;

            this._cosmosExecutor = new CosmosExecutor(container, logger);
        }

        /// <summary>
        /// This Function return TOP N records, in the order of time, from the Cosmos DB
        /// </summary>
        /// <returns></returns>
        public async Task<List<JsonDocument>> GetSampleAsync()
        {
            _logger.LogInformation($"Sampling Configuration is Count: {_numberOfRecords}, Days: {_maxDays}");
            string daysFilterClause = string.Empty;

            if (_maxDays > 0)
            {
                daysFilterClause = $"WHERE c._ts >= {GetTimeStampThreshold()}";
            }

            string query = string.Format(SELECT_QUERY, _numberOfRecords, daysFilterClause);

            return await _cosmosExecutor.ExecuteQueryAsync<JsonDocument>(query);
        }

        public virtual long GetTimeStampThreshold()
        {
            return new DateTimeOffset(DateTime.UtcNow.AddDays(-_maxDays)).ToUnixTimeSeconds();
        }
    }
}
