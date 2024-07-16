// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    public interface ISchemaGeneratorSampler
    {
        public Task<List<JObject>> GetSampleAsync();
    }
}
