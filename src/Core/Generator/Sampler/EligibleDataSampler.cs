// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    /// <summary>
    /// The EligibleDataSampler class is designed to sample data from an Azure Cosmos DB container 
    /// by fetching records from each partition based on a specified partition key. 
    /// The sampling is configurable by the number of records per partition and the time range considered.
    /// </summary>
    public class EligibleDataSampler : ISchemaGeneratorSampler
    {
        // Default Configuration
        private const int RECORDS_PER_PARTITION = 5;
        internal const int MAX_DAYS_PER_PARTITION = 30;

        // Query
        private const string DISTINCT_QUERY = "SELECT DISTINCT {0} FROM c";
        private const string SELECT_QUERY = "SELECT * FROM c WHERE {0} {1} ORDER BY c._ts DESC {2}";

        private string? _partitionKeyPath;
        private int _numberOfRecordsPerPartition;
        private int _maxDaysPerPartition;

        private ILogger _logger;

        private CosmosExecutor _cosmosExecutor;

        /// <summary>
        /// Initializes a new instance of the EligibleDataSampler class.
        /// </summary>
        /// <param name="container">The Azure Cosmos DB container from which to sample data.</param>
        /// <param name="partitionKeyPath">Optional. The path to the partition key. If null, it will be fetched from the Azure Cosmos DB metadata.</param>
        /// <param name="numberOfRecordsPerPartition">Optional. The number of records to retrieve per partition. Defaults to 5.</param>
        /// <param name="maxDaysPerPartition">Optional. The maximum number of days in the past to consider per partition. Defaults to 30.</param>
        /// <param="logger">The logger to use for logging information.</param>
        public EligibleDataSampler(Container container, string? partitionKeyPath, int? numberOfRecordsPerPartition, int? maxDaysPerPartition, ILogger logger)
        {
            this._partitionKeyPath = partitionKeyPath;
            this._numberOfRecordsPerPartition = numberOfRecordsPerPartition ?? RECORDS_PER_PARTITION;
            this._maxDaysPerPartition = maxDaysPerPartition ?? MAX_DAYS_PER_PARTITION;

            this._logger = logger;

            this._cosmosExecutor = new CosmosExecutor(container, logger);
        }

        /// <summary>
        /// Retrieves sampled data by following these steps:
        /// 1. Retrieves the partition key path if not provided.
        /// 2. Retrieves unique partition key values.
        /// 3. Fetches the top N records within a specified time range from each partition.
        /// </summary>
        /// <returns>A list of JsonDocument objects representing the sampled data.</returns>
        public async Task<List<JsonDocument>> GetSampleAsync()
        {
            _logger.LogInformation("Sampling Configuration is Count (per partition): {0}, Days (per partition): {1}, Partition Key Path: {2}", _numberOfRecordsPerPartition, _maxDaysPerPartition, _partitionKeyPath);

            // Step 1: Get Available Partition Key Paths
            List<string> partitionKeyPaths = await GetPartitionKeyPaths();

            _logger.LogDebug("Partition Key Paths: {0}", string.Join(',', partitionKeyPaths));

            // Step 2: Get Unique Partition Key Values
            List<JsonDocument> uniquePartitionKeys = await GetUniquePartitionKeyValues(partitionKeyPaths);

            _logger.LogDebug("{0} unique partition keys found.", uniquePartitionKeys.Count);

            List<JsonDocument> dataArray = new();
            // Step 3: Get Data from each partition
            foreach (JsonDocument partitionKey in uniquePartitionKeys)
            {
                List<JsonDocument> data = await GetData(partitionKey.RootElement);

                dataArray.AddRange(data);
            }

            return dataArray;
        }

        /// <summary>
        /// Retrieves the partition key paths from the Azure Cosmos DB container. 
        /// If the partition key path is not provided, it fetches the path from the container's metadata.
        /// </summary>
        /// <returns>A list of partition key paths as strings.</returns>
        internal async Task<List<string>> GetPartitionKeyPaths()
        {
            if (_partitionKeyPath is null)
            {
                _partitionKeyPath = await _cosmosExecutor.GetPartitionKeyPath();
            }

            // Splits the partition key path into individual keys
            List<string> partitionKeyPaths = _partitionKeyPath.Split('/').Where(p => !string.IsNullOrEmpty(p)).ToList();
            return partitionKeyPaths;
        }

        /// <summary>
        /// Retrieves unique partition key values from the Azure Cosmos DB container.
        /// </summary>
        /// <param name="partitionKeyPaths">A list of partition key paths used to query for unique values.</param>
        /// <returns>A list of JsonDocument objects representing unique partition key values.</returns>
        internal async Task<List<JsonDocument>> GetUniquePartitionKeyValues(List<string> partitionKeyPaths)
        {
            string selectClause = string.Join(", ", partitionKeyPaths.Select(path => $"c.{path}"));
            string query = string.Format(DISTINCT_QUERY, selectClause);

            return await _cosmosExecutor.ExecuteQueryAsync<JsonDocument>(query);
        }

        /// <summary>
        /// Retrieves the top N records from the specified partition based on the partition key value and the time range.
        /// </summary>
        /// <param name="partitionKey">A JsonElement representing the partition key value.</param>
        /// <returns>A list of JsonDocument objects containing the data from the partition.</returns>
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

        /// <summary>
        /// Calculates the timestamp threshold based on the current UTC time and the maximum number of days per partition.
        /// </summary>
        /// <returns>A Unix timestamp representing the earliest allowed record time.</returns>
        public virtual long GetTimeStampThreshold()
        {
            return new DateTimeOffset(DateTime.UtcNow.AddDays(-_maxDaysPerPartition)).ToUnixTimeSeconds();
        }
    }
}
