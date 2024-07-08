// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Generator.Sampler;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Generator
{
    internal class GraphQlSchemaGenerator
    {
        public static async Task GenerateSchema(ISchemaGeneratorSampler sampler, Container container)
        {
            JArray dataArray = await sampler.GetSampleAsync(container);

            string schema = SchemaGenerator.Run(dataArray);

            Console.WriteLine(schema);
        }
    }
}
