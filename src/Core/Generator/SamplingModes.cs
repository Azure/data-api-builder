// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Generator
{
    /// <summary>
    /// Defines the different modes of sampling data from Azure Cosmos DB database.
    /// </summary>
    public enum SamplingModes
    {
        /// <summary>
        /// Represents the mode where the top N records are sampled.
        /// This mode selects a specified number of records in descending order based on a timestamp or other sorting criteria.
        /// </summary>
        TopNExtractor,

        /// <summary>
        /// Represents the mode where data is sampled based on partitions.
        /// In this mode, data is fetched from each partition with a specified limit per partition, which helps in distributing the sampling across different data segments.
        /// </summary>
        EligibleDataSampler,

        /// <summary>
        /// Represents the mode where data is sampled by dividing the time range into subranges.
        /// This mode samples a specified number of records from each time-based subrange, allowing for temporal segmentation in the sampling process.
        /// </summary>
        TimePartitionedSampler
    }
}
