// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    /// <summary>
    /// Interface for schema generator samplers, which define methods for sampling data from a data source.
    /// Implementing classes should provide functionality to retrieve a subset of data, typically for the purpose of generating schema information.
    /// </summary>
    public interface ISchemaGeneratorSampler
    {
        /// <summary>
        /// Asynchronously retrieves a sample of data.
        /// Implementations of this method should define the criteria for sampling, such as the number of records, time range, or partitioning.
        /// </summary>
        /// <returns>A task that represents the asynchronous operation. The task result contains a list of <see cref="JsonDocument"/> objects, each representing a sampled data record.</returns>
        public Task<List<JsonDocument>> GetSampleAsync();
    }
}
