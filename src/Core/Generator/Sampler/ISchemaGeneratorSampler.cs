// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    public interface ISchemaGeneratorSampler
    {
        public Task<JArray> GetSampleAsync(Container container);
    }
}
