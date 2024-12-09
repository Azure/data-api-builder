// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    /// <summary>
    /// The TimePartitionedSampler class is responsible for dividing a time range into subranges 
    /// and retrieving the top N records from each subrange based on a specified configuration.
    /// </summary>
    public class TimePartitionedSampler : ISchemaGeneratorSampler
    {
        // Default Configuration
        private const int GROUP_COUNT = 10;
        private const int RECORDS_PER_GROUP = 10;
        internal const int MAX_DAYS = 10;

        // Query
        private const string MIN_TIMESTAMP_QUERY = "SELECT VALUE MIN(c._ts) FROM c";
        private const string MAX_TIMESTAMP_QUERY = "SELECT VALUE MAX(c._ts) FROM c";
        private const string SELECT_TOP_QUERY = "SELECT TOP {0} * FROM c WHERE c._ts >= {1} AND c._ts <= {2} ORDER by c._ts desc";

        private int _groupCount;
        private int _numberOfRecordsPerGroup;
        private int _maxDays;

        private ILogger _logger;

        private CosmosExecutor _cosmosExecutor;

        /// <summary>
        /// Initializes a new instance of the TimePartitionedSampler class.
        /// </summary>
        /// <param name="container">The Azure Cosmos DB container from which to retrieve data.</param>
        /// <param name="groupCount">Optional. The number of subranges (or groups) to divide the time range into. Defaults to 10.</param>
        /// <param name="numberOfRecordsPerGroup">Optional. The number of records to retrieve from each subrange. Defaults to 10.</param>
        /// <param name="maxDays">Optional. The maximum number of days in the past from which to consider records. Defaults to 10.</param>
        /// <param name="logger">The logger to use for logging information.</param>
        public TimePartitionedSampler(Container container, int? groupCount, int? numberOfRecordsPerGroup, int? maxDays, ILogger logger)
        {
            this._groupCount = groupCount ?? GROUP_COUNT;
            this._numberOfRecordsPerGroup = numberOfRecordsPerGroup ?? RECORDS_PER_GROUP;
            this._maxDays = maxDays ?? MAX_DAYS;

            this._logger = logger;

            this._cosmosExecutor = new CosmosExecutor(container, logger);
        }

        /// <summary>
        /// Retrieves sampled data by performing the following steps:
        /// 1. Obtains the highest and lowest timestamps from the data.
        /// 2. Divides the entire time range into a specified number of subranges (or groups).
        /// 3. Fetches the top N records from each subrange, ordered by timestamp.
        /// </summary>
        /// <returns>A list of JsonDocument objects representing the sampled data.</returns>
        public async Task<List<JsonDocument>> GetSampleAsync()
        {
            _logger.LogInformation("Sampling Configuration is Count(per group): {0}, Days (records considered for grouping): {1}, Group Count: {2}", _numberOfRecordsPerGroup, _maxDays, _groupCount);

            // Step 1: Get the highest and lowest timestamps
            (long minTimestamp, long maxTimestamp) = await GetHighestAndLowestTimestampsAsync();

            _logger.LogDebug("Min Timestamp(UTC): {0}, Max Timestamp(UTC): {1}", DateTimeOffset.FromUnixTimeSeconds(minTimestamp).UtcDateTime, DateTimeOffset.FromUnixTimeSeconds(maxTimestamp).UtcDateTime);

            // Step 2 & 3: Divide the range into subranges and get data
            return await GetDataFromSubranges(minTimestamp, maxTimestamp, _groupCount, _numberOfRecordsPerGroup);
        }

        /// <summary>
        /// Fetches the minimum and maximum timestamps from the data within the specified time range.
        /// </summary>
        /// <returns>A tuple containing the minimum and maximum timestamps.</returns>
        private async Task<(long minTimestamp, long maxTimestamp)> GetHighestAndLowestTimestampsAsync()
        {
            List<long> maxTimestamp = await this._cosmosExecutor.ExecuteQueryAsync<long>(MAX_TIMESTAMP_QUERY);
            List<long> minTimestamp = new(capacity: 1); // There is always one minimum timestamp for a container.
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

        /// <summary>
        /// Divides the time range between the minimum and maximum timestamps into the specified number of subranges,
        /// and retrieves the top N records from each subrange.
        /// </summary>
        /// <param name="minTimestamp">The minimum timestamp for the range.</param>
        /// <param name="maxTimestamp">The maximum timestamp for the range.</param>
        /// <param name="numberOfSubranges">The number of subranges to divide the time range into.</param>
        /// <param name="itemsPerSubrange">The number of items to retrieve from each subrange.</param>
        /// <returns>A list of JsonDocument objects representing the data retrieved from each subrange.</returns>
        private async Task<List<JsonDocument>> GetDataFromSubranges(long minTimestamp, long maxTimestamp, int numberOfSubranges, int itemsPerSubrange)
        {
            List<JsonDocument> dataArray = new();

            long rangeSize = (maxTimestamp - minTimestamp) / numberOfSubranges;

            for (int i = 0; i < numberOfSubranges; i++)
            {
                long rangeStart = minTimestamp + (i * rangeSize);
                long rangeEnd = (i == numberOfSubranges - 1) ? maxTimestamp : rangeStart + rangeSize - 1;

                _logger.LogDebug("Fetching data for subrange {0} from {1} to {2}", i + 1, rangeStart, rangeEnd);

                string query = string.Format(SELECT_TOP_QUERY, itemsPerSubrange, rangeStart, rangeEnd);

                dataArray.AddRange(await this._cosmosExecutor.ExecuteQueryAsync<JsonDocument>(query));
            }

            return dataArray;
        }

        /// <summary>
        /// Calculates the timestamp threshold for the maximum number of days specified.
        /// </summary>
        /// <returns>A Unix timestamp representing the earliest allowed record time.</returns>
        public virtual long GetTimeStampThreshold()
        {
            return new DateTimeOffset(DateTime.UtcNow.AddDays(-_maxDays)).ToUnixTimeSeconds();
        }

    }
}
