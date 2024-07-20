// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    public class PartitionBasedSampler : ISchemaGeneratorSampler
    {
        // Default Configuration
        private const int RECORDS_PER_PARTITION = 5;
        private const int MAX_DAYS_PER_PARTITION = 30;

        // Query
        private const string DISTINCT_QUERY = "SELECT DISTINCT {0} FROM c";
        private const string SELECT_QUERY = "SELECT * FROM c WHERE {0} {1} ORDER BY c._ts DESC {2}";

        private string? _partitionKeyPath;
        private int _numberOfRecordsPerPartition;
        private int _maxDaysPerPartition;

        private CosmosExecutor _cosmosExecutor;

        public PartitionBasedSampler(Container container, string? partitionKeyPath, int? numberOfRecordsPerPartition, int? maxDaysPerPartition)
        {
            this._partitionKeyPath = partitionKeyPath;
            this._numberOfRecordsPerPartition = numberOfRecordsPerPartition ?? RECORDS_PER_PARTITION;
            this._maxDaysPerPartition = maxDaysPerPartition ?? MAX_DAYS_PER_PARTITION;

            this._cosmosExecutor = new CosmosExecutor(container);
        }

        /// <summary>
        /// This Function return sampled data after going through below steps
        /// 1. If partition key path is not provided, get it from Cosmos DB
        /// 2. Once We have list of partitions, get unique partition key values
        /// 3. Fire query to get top N records in a time range (i.e. max days), in the order of time, from each partition
        /// </summary>
        /// <returns></returns>
        public async Task<List<JObject>> GetSampleAsync()
        {
            // Get Available Partition Key Paths
            List<string> partitionKeyPaths = await GetPartitionKeyPaths();

            // Get Unique Partition Key Values
            List<JObject> uniquePartitionKeys = await GetUniquePartitionKeyValues(partitionKeyPaths);

            List<JObject> dataArray = new();
            // Get Data from each partition
            foreach (JObject partitionKey in uniquePartitionKeys)
            {
                List<JObject> data = await GetData(partitionKey);

                dataArray.AddRange(data);
            }

            return dataArray;
        }

        internal async Task<List<string>> GetPartitionKeyPaths()
        {
            if (_partitionKeyPath is null)
            {
                _partitionKeyPath = await _cosmosExecutor.GetPartitionKeyPath();
            }

            // Get List of Partition Key paths
            List<string> partitionKeyPaths = _partitionKeyPath.Split('/').Where(p => !string.IsNullOrEmpty(p)).ToList();
            // Build the query to get unique partition key values

            return partitionKeyPaths;
        }

        internal async Task<List<JObject>> GetUniquePartitionKeyValues(List<string> partitionKeyPaths)
        {
            string selectClause = string.Join(", ", partitionKeyPaths.Select(path => $"c.{path}"));
            string query = string.Format(DISTINCT_QUERY, selectClause);
            List<JObject> uniquePartitionKeys = await _cosmosExecutor.ExecuteQueryAsync<JObject>(query);
            return uniquePartitionKeys;
        }

        internal async Task<List<JObject>> GetData(JObject partitionKey)
        {
            long? timestampThreshold = null;
            if (_maxDaysPerPartition > 0)
            {
                // Calculate the timestamp threshold for the timespan
                timestampThreshold = GetTimeStampThreshold();
            }

            // Build a filter condition based on the partition key
            string filterCondition = string.Join(" AND ", partitionKey.Properties().Select(prop => $"c.{prop.Name} = '{prop.Value}'"));

            string timestampThresholdCondition = timestampThreshold != null ? $"AND c._ts >= {timestampThreshold}" : string.Empty;
            string limitCondition = _numberOfRecordsPerPartition > 0 ? $"OFFSET 0 LIMIT {_numberOfRecordsPerPartition}" : string.Empty;

            string latestItemsQuery = string.Format(SELECT_QUERY, filterCondition, timestampThresholdCondition, limitCondition);
            return await _cosmosExecutor.ExecuteQueryAsync<JObject>(latestItemsQuery);
        }

        public virtual long GetTimeStampThreshold()
        {
            return new DateTimeOffset(DateTime.UtcNow.AddDays(-_maxDaysPerPartition)).ToUnixTimeSeconds();
        }
    }
}
