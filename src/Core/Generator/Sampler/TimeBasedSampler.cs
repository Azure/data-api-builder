// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    internal class TimeBasedSampler : ISchemaGeneratorSampler
    {
        private int _groupCount;
        private int _numberOfRecordsPerGroup;
        private int _maxDaysPerGroup;

        private QueryExecutor _queryExecutor;

        public TimeBasedSampler(Container container, int? groupCount, int? numberOfRecordsPerGroup, int? maxDaysPerGroup)
        {
            this._groupCount = groupCount ?? 10;
            this._numberOfRecordsPerGroup = numberOfRecordsPerGroup ?? 5;
            this._maxDaysPerGroup = maxDaysPerGroup ?? 10;

            this._queryExecutor = new QueryExecutor(container);
        }

        public async Task<List<JObject>> GetSampleAsync()
        {
            try
            {
                // Get the highest and lowest timestamps
                (int minTimestamp, int maxTimestamp) = await GetHighestAndLowestTimestampsAsync();

                // Divide the range into subranges and get data
                return await GetDataFromSubranges(minTimestamp, maxTimestamp, _groupCount, _numberOfRecordsPerGroup);
            }
            catch (CosmosException ex)
            {
                Console.WriteLine($"Cosmos DB error: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
            }

            return new List<JObject>();
        }

        private async Task<(int minTimestamp, int maxTimestamp)> GetHighestAndLowestTimestampsAsync()
        {
            List<int> maxTimestampQuery = await this._queryExecutor.ExecuteQueryAsync<int>("SELECT VALUE MAX(c._ts) FROM c");
            List<int> minTimestampQuery = await this._queryExecutor.ExecuteQueryAsync<int>("SELECT VALUE MIN(c._ts) FROM c");

            return (minTimestampQuery[0], maxTimestampQuery[0]);
        }

        private async Task<List<JObject>> GetDataFromSubranges(int minTimestamp, int maxTimestamp, int numberOfSubranges, int itemsPerSubrange)
        {
            List<JObject> dataArray = new();

            int rangeSize = (maxTimestamp - minTimestamp) / numberOfSubranges;

            for (int i = 0; i < numberOfSubranges; i++)
            {
                int rangeStart = minTimestamp + (i * rangeSize);
                int rangeEnd = (i == numberOfSubranges - 1) ? maxTimestamp : rangeStart + rangeSize - 1;

                string query = $"SELECT TOP {itemsPerSubrange} * FROM c WHERE c._ts >= {rangeStart} AND c._ts <= {rangeEnd}";

                dataArray.AddRange(await this._queryExecutor.ExecuteQueryAsync<JObject>(query));
            }

            return dataArray;
        }

    }
}
