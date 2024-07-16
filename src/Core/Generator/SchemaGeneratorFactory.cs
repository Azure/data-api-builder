// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Generator.Sampler;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Generator
{
    internal static class SchemaGeneratorFactory
    {
        public static async Task<string> Create(Container container, SamplingMode mode, int? sampleCount, string? partitionKeyPath, int? days, int? groupCount)
        {
            ISchemaGeneratorSampler schemaGeneratorSampler = mode switch
            {
                SamplingMode.TopNSampler => new TopNSampler(container, sampleCount),
                SamplingMode.PartitionBasedSampler => new PartitionBasedSampler(container, partitionKeyPath, sampleCount, days),
                SamplingMode.TimeBasedSampler => new TimeBasedSampler(container, groupCount, sampleCount, days),
                _ => throw new ArgumentException($"Invalid sampling mode: {mode}")
            };

            // Get Sample Data
            List<JObject> dataArray = await schemaGeneratorSampler.GetSampleAsync();

            // Generate GQL Schema
            return SchemaGenerator.Generate(dataArray, container.Id);
        }
    }
}
