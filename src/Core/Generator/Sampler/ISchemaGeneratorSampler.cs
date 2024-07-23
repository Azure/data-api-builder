// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    public interface ISchemaGeneratorSampler
    {
        public Task<List<JsonDocument>> GetSampleAsync();

        public long GetTimeStampThreshold();
    }
}
