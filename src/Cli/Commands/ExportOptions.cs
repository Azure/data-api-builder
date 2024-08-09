// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Generator;
using CommandLine;

namespace Cli.Commands
{
    [Verb("export", isDefault: false, HelpText = "Export the GraphQL schema as a file and save it to disk.", Hidden = false)]
    public class ExportOptions : Options
    {
        public ExportOptions(bool graphql, string outputDirectory, string? graphqlSchemaFile,
                bool? generate, string? samplingMode, int? numberOfRecords, string? partitionKeyPath, int? maxDays, int? groupCount, string? config) : base(config)
        {
            GraphQL = graphql;
            OutputDirectory = outputDirectory;
            GraphQLSchemaFile = graphqlSchemaFile ?? "schema.gql";

            Generate = generate ?? false;
            SamplingMode = samplingMode ?? SamplingModes.TopNSampler.ToString();
            NumberOfRecords = numberOfRecords;
            PartitionKeyPath = partitionKeyPath;
            MaxDays = maxDays;
            GroupCount = groupCount;
        }

        [Option("graphql", HelpText = "Export GraphQL schema")]
        public bool GraphQL { get; }

        [Option('o', "output", HelpText = "Specify the directory where the schema file will be saved. This option is required.", Required = true)]
        public string OutputDirectory { get; }

        [Option('g', "graphql-schema-file", HelpText = "Specify the filename for the exported GraphQL schema.(Default is:'schema.gql').")]
        public string GraphQLSchemaFile { get; }

        [Option("generate", HelpText = "Generates a schema file from the specified Azure Cosmos DB database.")]
        public bool Generate { get; }

        [Option('m', "sampling-mode", HelpText = "Specifies the sampling mode to use. Available modes include:\n" +
                                                 "- TopNSampler: It retrieves a specified number of recent records from an Azure Cosmos DB container, optionally filtering by a maximum number of days.\n" +
                                                 "- PartitionBasedSampler: It retrieves a specified number of records from an Azure Cosmos DB container by fetching records from each partition using a given partition key.The number of records per partition and the time range are configurable.\n" +
                                                 "- TimeBasedSampler:.It retrieves a specified number of records by dividing the container data and time range into subranges, then selecting the top N records from each subrange based on a given configuration.\n")]
        public string SamplingMode { get; } = SamplingModes.TopNSampler.ToString();

        [Option('n', "sampling-count", HelpText = "Specify the total number of samples to retrieve for each sampling modes:\n" +
                                                  "- TopNSampler: Total number of records to select.\n" +
                                                  "- PartitionBasedSampler: Number of records to retrieve from each partition.\n" +
                                                  "- TimeBasedSampler: Number of records to retrieve per time range group.")]
        public int? NumberOfRecords { get; }

        [Option("sampling-partition-key-path", HelpText = "Specify the partition key path. This option is applicable only when the 'PartitionBasedSampler' mode is selected.")]
        public string? PartitionKeyPath { get; }

        [Option('d', "sampling-days", HelpText = "Specify the number of days to fetch data. Sampling modes include: \n" +
                                                 "- TopNSampler: Limits records to the most recent days.\n" +
                                                 "- PartitionBasedSampler: Limits records from each partition to the specified number of days.\n" +
                                                 "- TimeBasedSampler: Gathers data over the specified number of days and divides it into subranges.")]
        public int? MaxDays { get; }

        [Option("sampling-group-count", HelpText = "Specify the number of groups for sampling. This option is applicable only when the 'TimeBasedSampler' mode is selected.")]
        public int? GroupCount { get; }
    }
}
