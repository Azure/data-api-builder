// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    /// <summary>
    /// This Sampler divide the time range into subranges and get top N records from each subrange.
    /// </summary>
    public class TimeBasedSampler : ISchemaGeneratorSampler
    {
        // Default Configuration
        private const int GROUP_COUNT = 10;
        private const int RECORDS_PER_GROUP = 10;
        private const int MAX_DAYS = 10;

        // Query
        private const string MIN_TIMESTAMP_QUERY = "SELECT VALUE MIN(c._ts) FROM c";
        private const string MAX_TIMESTAMP_QUERY = "SELECT VALUE MAX(c._ts) FROM c";
        private const string SELECT_TOP_QUERY = "SELECT TOP {0} * FROM c WHERE c._ts >= {1} AND c._ts <= {2} ORDER by c._ts desc";

        private int _groupCount;
        private int _numberOfRecordsPerGroup;
        private int _maxDays;

        private ILogger _logger;

        private CosmosExecutor _cosmosExecutor;

        public TimeBasedSampler(Container container, int? groupCount, int? numberOfRecordsPerGroup, int? maxDays, ILogger logger)
        {
            this._groupCount = groupCount ?? GROUP_COUNT;
            this._numberOfRecordsPerGroup = numberOfRecordsPerGroup ?? RECORDS_PER_GROUP;
            this._maxDays = maxDays ?? MAX_DAYS;

            this._logger = logger;

            this._cosmosExecutor = new CosmosExecutor(container, logger);
        }

        /// <summary>
        /// This Function return sampled data after going through below steps:
        /// 1) Get the highest and lowest timestamps.
        /// 2) Divide this time range into subranges (or groups).
        /// 3) Get top N records, order by timestamp, from each subrange (or group).
        /// </summary>
        /// <returns></returns>
        public async Task<List<JsonDocument>> GetSampleAsync()
        {
            _logger.LogInformation($"Sampling Configuration is numberOfRecordsPerGroup: {_numberOfRecordsPerGroup}, maxDays: {_maxDays}, groupCount: {_groupCount}");

            // Get the highest and lowest timestamps
            (long minTimestamp, long maxTimestamp) = await GetHighestAndLowestTimestampsAsync();

            _logger.LogDebug($"Min Timestamp: {minTimestamp}, Max Timestamp: {maxTimestamp}");

            // Divide the range into subranges and get data
            return await GetDataFromSubranges(minTimestamp, maxTimestamp, _groupCount, _numberOfRecordsPerGroup);
        }

        private async Task<(long minTimestamp, long maxTimestamp)> GetHighestAndLowestTimestampsAsync()
        {
            List<long> maxTimestamp = await this._cosmosExecutor.ExecuteQueryAsync<long>(MAX_TIMESTAMP_QUERY);
            List<long> minTimestamp = new(capacity: 1);
            if (_maxDays > 0)
            {
                // Calculate the timestamp threshold for the timespan
                minTimestamp.Add(GetTimeStampThreshold());
            }
            else
            {
                minTimestamp = await this._cosmosExecutor.ExecuteQueryAsync<long>(MIN_TIMESTAMP_QUERY);
            }

            return (minTimestamp[0], maxTimestamp[0]);
        }

        private async Task<List<JsonDocument>> GetDataFromSubranges(long minTimestamp, long maxTimestamp, int numberOfSubranges, int itemsPerSubrange)
        {
            List<JsonDocument> dataArray = new();

            long rangeSize = (maxTimestamp - minTimestamp) / numberOfSubranges;

            for (int i = 0; i < numberOfSubranges; i++)
            {
                long rangeStart = minTimestamp + (i * rangeSize);
                long rangeEnd = (i == numberOfSubranges - 1) ? maxTimestamp : rangeStart + rangeSize - 1;

                _logger.LogDebug($"Fetching data for subrange {i + 1} from {rangeStart} to {rangeEnd}");

                string query = string.Format(SELECT_TOP_QUERY, itemsPerSubrange, rangeStart, rangeEnd);

                dataArray.AddRange(await this._cosmosExecutor.ExecuteQueryAsync<JsonDocument>(query));
            }

            return dataArray;
        }

        public virtual long GetTimeStampThreshold()
        {
            return new DateTimeOffset(DateTime.UtcNow.AddDays(-_maxDays)).ToUnixTimeSeconds();
        }

    }
}
