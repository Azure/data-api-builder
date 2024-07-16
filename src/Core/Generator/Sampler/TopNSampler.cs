// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    internal class TopNSampler : ISchemaGeneratorSampler
    {
        // Default Configuration
        public const int NUMBER_OF_RECORDS = 10;
        public const int MAX_DAYS = 0;

        // Query
        public const string SELECT_QUERY = "SELECT TOP {0} * FROM c {1} ORDER BY c._ts desc";

        private int _numberOfRecords;
        private int _maxDays;

        private CosmosExecutor _cosmosExecutor;

        public TopNSampler(Container container, int? numberOfRecords, int? maxDays)
        {
            this._numberOfRecords = numberOfRecords ?? NUMBER_OF_RECORDS;
            this._maxDays = maxDays ?? MAX_DAYS;

            this._cosmosExecutor = new CosmosExecutor(container);
        }

        /// <summary>
        /// This Function return TOP N records, in the order of time, from the Cosmos DB
        /// </summary>
        /// <returns></returns>
        public async Task<List<JObject>> GetSampleAsync()
        {
            string daysFilterClause = string.Empty;

            if (_maxDays > 0)
            {
                long timestampThreshold = new DateTimeOffset(DateTime.UtcNow.AddDays(_maxDays)).ToUnixTimeSeconds();

                daysFilterClause = $"WHERE c._ts <= {timestampThreshold}";
            }
            
            string query = string.Format(SELECT_QUERY, _numberOfRecords, daysFilterClause);

            return await _cosmosExecutor.ExecuteQueryAsync<JObject>(query);
        }
    }
}
