// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Generator.Sampler;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Generator
{
    internal class GraphQLSchemaGenerator
    {
        public static async Task<string> Generate(ISchemaGeneratorSampler sampler, Container container)
        {
            // Get Sample Data
            JArray dataArray = await sampler.GetSampleAsync(container);

            // Generate GQL Schema
            return SchemaGenerator.Run(dataArray, container.Id);
        }
    }
}
