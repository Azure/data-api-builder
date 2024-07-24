// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    /// <summary>
    /// Sampler interface for generating schema.
    /// </summary>
    public interface ISchemaGeneratorSampler
    {
        /// <summary>
        /// Returns the sampled data.
        /// </summary>
        /// <returns></returns>
        public Task<List<JsonDocument>> GetSampleAsync();
    }
}
