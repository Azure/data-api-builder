// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Generator.Sampler;
using Microsoft.Azure.Cosmos;

namespace Azure.DataApiBuilder.Core.Generator
{
    internal static class SchemaGeneratorFactory
    {
        public static async Task<string> Create(Container container, SamplingMode mode, int? sampleCount, string? partitionKeyPath, int? days, int? groupCount)
        {
            ISchemaGeneratorSampler schemaGeneratorSampler = mode switch
            {
                SamplingMode.TopNSampler => new TopNSampler(sampleCount),
                SamplingMode.PartitionBasedSampler => new PartitionBasedSampler(partitionKeyPath, sampleCount, days),
                SamplingMode.TimeBasedSampler => new TimeBasedSampler(groupCount, sampleCount, days),
                _ => throw new ArgumentException($"Invalid sampling mode: {mode}")
            };

            return await GraphQLSchemaGenerator.Generate(schemaGeneratorSampler, container);
        }
    }
}
