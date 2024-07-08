// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    internal class TimeBasedSampler : ISchemaGeneratorSampler
    {
        private int _groupSize;
        private int _numberOfRecordsPerGroup;
        private int _maxDaysPerPartition;

        public TimeBasedSampler(int groupSize, int numberOfRecordsPerGroup, int maxDaysPerPartition)
        {
            this._groupSize = groupSize;
            this._numberOfRecordsPerGroup = numberOfRecordsPerGroup;
            this._maxDaysPerPartition = maxDaysPerPartition;
        }

        public async Task<JArray> GetSampleAsync(Container container)
        {
            try
            {
                // Get the highest and lowest timestamps
                (int minTimestamp, int maxTimestamp) = await GetHighestAndLowestTimestampsAsync(container);

                // Divide the range into subranges and get data
                return await GetDataFromSubranges(container, minTimestamp, maxTimestamp, _groupSize, _numberOfRecordsPerGroup);
            }
            catch (CosmosException ex)
            {
                Console.WriteLine($"Cosmos DB error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            return new JArray();
        }

        private static async Task<(int minTimestamp, int maxTimestamp)> GetHighestAndLowestTimestampsAsync(Container container)
        {
            string maxTimestampQuery = "SELECT VALUE MAX(c._ts) FROM c";
            string minTimestampQuery = "SELECT VALUE MIN(c._ts) FROM c";

            var maxTimestampIterator = container.GetItemQueryIterator<int>(new QueryDefinition(maxTimestampQuery));
            var minTimestampIterator = container.GetItemQueryIterator<int>(new QueryDefinition(minTimestampQuery));

            int maxTimestamp = 0;
            int minTimestamp = 0;

            if (maxTimestampIterator.HasMoreResults)
            {
                foreach (var maxTs in await maxTimestampIterator.ReadNextAsync())
                {
                    maxTimestamp = maxTs;
                }
            }

            if (minTimestampIterator.HasMoreResults)
            {
                foreach (var minTs in await minTimestampIterator.ReadNextAsync())
                {
                    minTimestamp = minTs;
                }
            }

            return (minTimestamp, maxTimestamp);
        }

        private static async Task<JArray> GetDataFromSubranges(Container container, int minTimestamp, int maxTimestamp, int numberOfSubranges, int itemsPerSubrange)
        {
            JArray dataArray = new();

            int rangeSize = (maxTimestamp - minTimestamp) / numberOfSubranges;

            for (int i = 0; i < numberOfSubranges; i++)
            {
                int rangeStart = minTimestamp + (i * rangeSize);
                int rangeEnd = (i == numberOfSubranges - 1) ? maxTimestamp : rangeStart + rangeSize - 1;

                string query = $"SELECT TOP {itemsPerSubrange} * FROM c WHERE c._ts >= {rangeStart} AND c._ts <= {rangeEnd}";

                var queryIterator = container.GetItemQueryIterator<dynamic>(new QueryDefinition(query));

                Console.WriteLine($"Fetching data for range {rangeStart} to {rangeEnd}:");
                while (queryIterator.HasMoreResults)
                {
                    foreach (var item in await queryIterator.ReadNextAsync())
                    {
                        dataArray.Add(item);
                    }
                }
            }

            return dataArray;
        }

    }
}
