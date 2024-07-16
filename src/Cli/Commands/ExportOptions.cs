// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Generator;
using CommandLine;

namespace Cli.Commands
{
    [Verb("export", isDefault: false, HelpText = "Export the GraphQL schema as a file and save to disk", Hidden = false)]
    public class ExportOptions : Options
    {
        public ExportOptions(bool graphql, string outputDirectory, string? config, string? graphqlSchemaFile,
                bool? generate, string? samplingMode, int? numberOfRecords, string? partitionKeyPath, int? maxDays, int? groupCount) : base(config)
        {
            GraphQL = graphql;
            OutputDirectory = outputDirectory;
            GraphQLSchemaFile = graphqlSchemaFile ?? "schema.graphql";

            Generate = generate ?? false;
            Sampling = samplingMode ?? SamplingMode.TopNSampler.ToString();
            NumberOfRecords = numberOfRecords;
            PartitionKeyPath = partitionKeyPath;
            MaxDays = maxDays ?? 0;
            GroupCount = groupCount ?? 0;
        }

        [Option("graphql", HelpText = "Export GraphQL schema")]
        public bool GraphQL { get; }

        [Option('o', "output", HelpText = "Directory to save to", Required = true)]
        public string OutputDirectory { get; }

        [Option('g', "graphql-schema-file", HelpText = "The GraphQL schema file name (default schema.graphql)")]
        public string GraphQLSchemaFile { get; }

        [Option("generate", HelpText = "To generate schema file from the database")]
        public bool Generate { get; }

        [Option("sampling-mode", HelpText = "Sampling Modes: TopNSampler, PartitionBasedSampler, TimeBasedSampler")]
        public string Sampling { get; } = SamplingMode.TopNSampler.ToString();

        [Option("sampling-count", HelpText = "Sampling Count")]
        public int? NumberOfRecords { get; }

        [Option("partitionKeyPath", HelpText = "Applicable only when 'PartitionBasedSampler' is selected")]
        public string? PartitionKeyPath { get; }

        [Option("days", HelpText = "TopNSampling: filter on number ")]
        public int MaxDays { get; }

        [Option("group-count", HelpText = "Applicable only when 'TimeBasedSampler' is selected")]
        public int GroupCount { get; }

    }
}
