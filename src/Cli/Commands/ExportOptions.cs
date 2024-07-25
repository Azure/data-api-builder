// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Generator;
using CommandLine;

namespace Cli.Commands
{
    [Verb("export", isDefault: false, HelpText = "Export the GraphQL schema as a file and save to disk", Hidden = false)]
    public class ExportOptions : Options
    {
        public ExportOptions(bool graphql, string outputDirectory, string? graphqlSchemaFile,
                bool? generate, string? samplingMode, int? numberOfRecords, string? partitionKeyPath, int? maxDays, int? groupCount, string? config) : base(config)
        {
            GraphQL = graphql;
            OutputDirectory = outputDirectory;
            GraphQLSchemaFile = graphqlSchemaFile ?? "schema.graphql";

            Generate = generate ?? false;
            SamplingMode = samplingMode ?? SamplingModes.TopNSampler.ToString();
            NumberOfRecords = numberOfRecords;
            PartitionKeyPath = partitionKeyPath;
            MaxDays = maxDays;
            GroupCount = groupCount;
        }

        [Option("graphql", HelpText = "Export GraphQL schema")]
        public bool GraphQL { get; }

        [Option('o', "output", HelpText = "Directory to save to", Required = true)]
        public string OutputDirectory { get; }

        [Option('g', "graphql-schema-file", HelpText = "The GraphQL schema file name (default schema.graphQL)")]
        public string GraphQLSchemaFile { get; }

        [Option("generate", HelpText = "To generate schema file from CosmosDB database")]
        public bool Generate { get; }

        [Option("sampling-mode", HelpText = "Sampling Modes: TopNSampler, PartitionBasedSampler, TimeBasedSampler")]
        public string SamplingMode { get; } = SamplingModes.TopNSampler.ToString();

        [Option('n', "sampling-count", HelpText = "Sampling Count, For TopNSampler: Total number of Samples, PartitionBasedSampler: Total number of Samples from each partition, TimeBasedSampler : Total number of sample in each time range group.")]
        public int? NumberOfRecords { get; }

        [Option("sampling-partitionKeyPath", HelpText = "Applicable only when 'PartitionBasedSampler' is selected")]
        public string? PartitionKeyPath { get; }

        [Option('d', "sampling-days", HelpText = "Data should be fetched for number of days, TopNSampling: filter on number of days, PartitionBasedSampler: Data fetched for a number of days from each partition, TimeBasedSampler: Decide the data range which will be divided into subranges.")]
        public int? MaxDays { get; }

        [Option("sampling-group-count", HelpText = "Applicable only when 'TimeBasedSampler' is selected")]
        public int? GroupCount { get; }

    }
}
