// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    /// <summary>
    /// This Sampler goes through each logical partition and fetches top N records in a time range.
    /// </summary>
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

        private ILogger _logger;

        private CosmosExecutor _cosmosExecutor;

        public PartitionBasedSampler(Container container, string? partitionKeyPath, int? numberOfRecordsPerPartition, int? maxDaysPerPartition, ILogger logger)
        {
            this._partitionKeyPath = partitionKeyPath;
            this._numberOfRecordsPerPartition = numberOfRecordsPerPartition ?? RECORDS_PER_PARTITION;
            this._maxDaysPerPartition = maxDaysPerPartition ?? MAX_DAYS_PER_PARTITION;

            this._logger = logger;

            this._cosmosExecutor = new CosmosExecutor(container, logger);
        }

        /// <summary>
        /// This Function return sampled data after going through below steps
        /// 1. If partition key path is not provided, get it from Cosmos DB
        /// 2. Once We have list of partitions, get unique partition key values
        /// 3. Fire query to get top N records in a time range (i.e. max days), in the order of time, from each partition
        /// </summary>
        /// <returns></returns>
        public async Task<List<JsonDocument>> GetSampleAsync()
        {
            _logger.LogInformation($"Sampling Configuration is numberOfRecordsPerPartition: {_numberOfRecordsPerPartition}, maxDaysPerPartition: {_maxDaysPerPartition}, partitionKeyPath: {_partitionKeyPath}");

            // Get Available Partition Key Paths
            List<string> partitionKeyPaths = await GetPartitionKeyPaths();

            _logger.LogDebug($"Partition Key Paths: {string.Join(',', partitionKeyPaths)}");

            // Get Unique Partition Key Values
            List<JsonDocument> uniquePartitionKeys = await GetUniquePartitionKeyValues(partitionKeyPaths);

            _logger.LogDebug($"{uniquePartitionKeys.Count} unique partition keys found.");

            List<JsonDocument> dataArray = new();
            // Get Data from each partition
            foreach (JsonDocument partitionKey in uniquePartitionKeys)
            {
                List<JsonDocument> data = await GetData(partitionKey.RootElement);

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

        internal async Task<List<JsonDocument>> GetUniquePartitionKeyValues(List<string> partitionKeyPaths)
        {
            List<JsonDocument> uniquePartitionKeyValues = new();
            string selectClause = string.Join(", ", partitionKeyPaths.Select(path => $"c.{path}"));
            string query = string.Format(DISTINCT_QUERY, selectClause);

            return await _cosmosExecutor.ExecuteQueryAsync<JsonDocument>(query);
        }

        internal async Task<List<JsonDocument>> GetData(JsonElement partitionKey)
        {
            long? timestampThreshold = null;
            if (_maxDaysPerPartition > 0)
            {
                // Calculate the timestamp threshold for the timespan
                timestampThreshold = GetTimeStampThreshold();
            }

            // Build a filter condition based on the partition key
            string filterCondition = string.Join(" AND ", partitionKey.EnumerateObject().Select(prop => $"c.{prop.Name} = '{prop.Value}'"));

            string timestampThresholdCondition = timestampThreshold != null ? $"AND c._ts >= {timestampThreshold}" : string.Empty;
            string limitCondition = _numberOfRecordsPerPartition > 0 ? $"OFFSET 0 LIMIT {_numberOfRecordsPerPartition}" : string.Empty;

            string latestItemsQuery = string.Format(SELECT_QUERY, filterCondition, timestampThresholdCondition, limitCondition);
            return await _cosmosExecutor.ExecuteQueryAsync<JsonDocument>(latestItemsQuery);
        }

        public virtual long GetTimeStampThreshold()
        {
            return new DateTimeOffset(DateTime.UtcNow.AddDays(-_maxDaysPerPartition)).ToUnixTimeSeconds();
        }
    }
}
