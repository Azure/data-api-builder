// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    internal class PartitionBasedSampler : ISchemaGeneratorSampler
    {
        private string? _partitionKeyPath;
        private int _numberOfRecordsPerPartition;
        private int _maxDaysPerPartition;

        private QueryExecutor _queryExecutor;

        public PartitionBasedSampler(Container container, string? partitionKeyPath, int? numberOfRecordsPerPartition, int? maxDaysPerPartition)
        {
            this._partitionKeyPath = partitionKeyPath;
            this._numberOfRecordsPerPartition = numberOfRecordsPerPartition ?? 5;
            this._maxDaysPerPartition = maxDaysPerPartition ?? 30;

            this._queryExecutor = new QueryExecutor(container);
        }

        public async Task<List<JObject>> GetSampleAsync()
        {
            List<JObject> dataArray = new ();

            if (_partitionKeyPath is null)
            {
                _partitionKeyPath = await _queryExecutor.GetPartitionKeyPath();
            }

            List<string> partitionKeyPaths = _partitionKeyPath.Split('/').Where(p => !string.IsNullOrEmpty(p)).ToList();
            // Build the query to get unique partition key values
            string selectClause = string.Join(", ", partitionKeyPaths.Select(path => $"c.{path}"));
            string query = $"SELECT DISTINCT {selectClause} FROM c";

            List<JObject> uniquePartitionKeys = await _queryExecutor.ExecuteQueryAsync<JObject>(query);

            long? timestampThreshold = null;
            if (_maxDaysPerPartition > 0)
            {
                // Calculate the timestamp threshold for the timespan
                timestampThreshold = new DateTimeOffset(DateTime.UtcNow.AddDays(-_maxDaysPerPartition)).ToUnixTimeSeconds();
            }

            // Get top latest modified items from each partition
            foreach (JObject partitionKey in uniquePartitionKeys)
            {
                // Build a filter condition based on the partition key
                string filterCondition = string.Join(" AND ", partitionKey.Properties().Select(prop => $"c.{prop.Name} = '{prop.Value}'"));

                string timestampThresholdCondition = timestampThreshold != null? $"AND c._ts >= {timestampThreshold}": string.Empty;
                string limitCondition = _numberOfRecordsPerPartition > 0 ? $"OFFSET 0 LIMIT {_numberOfRecordsPerPartition}" : string.Empty;

                string latestItemsQuery = $"SELECT * FROM c WHERE {filterCondition} {timestampThresholdCondition} ORDER BY c._ts DESC {limitCondition}";

                dataArray.AddRange(await _queryExecutor.ExecuteQueryAsync<JObject>(latestItemsQuery));
            }

            return dataArray;
        }
    }
}
