// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Generator.Sampler;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Generator
{
    internal class GraphQlSchemaGenerator
    {
        public static async Task<string> GenerateSchema(ISchemaGeneratorSampler sampler, Container container)
        {
            JArray dataArray = await sampler.GetSampleAsync(container);

            return SchemaGenerator.Run(dataArray);
        }
    }
}
