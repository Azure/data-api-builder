// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    internal class PartitionBasedSampler : ISchemaGeneratorSampler
    {
        private string _partitionKeyPath;
        private int _numberOfRecordsPerPartition;
        private int _maxDaysPerPartition;

        public PartitionBasedSampler(string partitionKeyPath, int? numberOfRecordsPerPartition, int? maxDaysPerPartition)
        {
            this._partitionKeyPath = partitionKeyPath;
            this._numberOfRecordsPerPartition = numberOfRecordsPerPartition ?? 5;
            this._maxDaysPerPartition = maxDaysPerPartition ?? 30;
        }

        public async Task<JArray> GetSampleAsync(Container container)
        {
            JArray dataArray = new ();

            // Get container properties
            ContainerProperties containerProperties = await container.ReadContainerAsync();
            var partitionKeyPaths = containerProperties.PartitionKeyPath.Split('/').Where(p => !string.IsNullOrEmpty(p)).ToList();

            // Build the query to get unique partition key values
            string selectClause = string.Join(", ", partitionKeyPaths.Select(path => $"c.{path}"));
            string query = $"SELECT DISTINCT {selectClause} FROM c";
            Console.WriteLine(query);
            QueryDefinition queryDefinition = new(query);
            FeedIterator<dynamic> queryResultSetIterator = container.GetItemQueryIterator<dynamic>(queryDefinition);

            List<dynamic> uniquePartitionKeys = new();

            while (queryResultSetIterator.HasMoreResults)
            {
                FeedResponse<dynamic> currentResultSet = await queryResultSetIterator.ReadNextAsync();
                foreach (var item in currentResultSet)
                {
                    uniquePartitionKeys.Add(item);
                }
            }

            long? timestampThreshold = null;
            if (_maxDaysPerPartition > 0)
            {
                // Calculate the timestamp threshold for the timespan
                timestampThreshold = new DateTimeOffset(DateTime.UtcNow.AddDays(-_maxDaysPerPartition)).ToUnixTimeSeconds();
            }

            // Get top latest modified items from each partition
            foreach (var partitionKey in uniquePartitionKeys)
            {
                // Build a filter condition based on the partition key
                string filterCondition = string.Join(" AND ", partitionKeyPaths.Select(path => $"c.{path} = '{partitionKey[path]}'"));

                string timestampThresholdCondition = timestampThreshold != null? $"AND c._ts >= {timestampThreshold}": string.Empty;
                string limitCondition = _numberOfRecordsPerPartition > 0 ? $"OFFSET 0 LIMIT {_numberOfRecordsPerPartition}" : string.Empty;

                string latestItemsQuery = $"SELECT * FROM c WHERE {filterCondition} {timestampThresholdCondition} ORDER BY c._ts DESC {limitCondition}";

                Console.WriteLine(latestItemsQuery);

                QueryDefinition latestItemsQueryDefinition = new (latestItemsQuery);
                FeedIterator<dynamic> latestItemsIterator = container.GetItemQueryIterator<dynamic>(latestItemsQueryDefinition);

                Console.WriteLine($"{_numberOfRecordsPerPartition} items for last {_maxDaysPerPartition} days for partition {partitionKey}:");

                while (latestItemsIterator.HasMoreResults)
                {
                    FeedResponse<dynamic> latestItemsResultSet = await latestItemsIterator.ReadNextAsync();
                    foreach (var item in latestItemsResultSet)
                    {
                        dataArray.Add(item);
                    }
                }
            }

            return dataArray;
        }
    }
}
