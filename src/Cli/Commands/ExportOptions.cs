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
            SamplingMode = samplingMode ?? SamplingModes.TopNExtractor.ToString();
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
                                                 "- TopNExtractor: Retrieves a specified number of recent records from an Azure Cosmos DB container, optionally filtering by a maximum number of days.\n" +
                                                 "- EligibleDataSampler: Retrieves a specified number of records, using a given partition key, from an Azure Cosmos DB container. The number of records per partition and the time range are configurable.\n" +
                                                 "- TimePartitionedSampler: Retrieves a specified number of records by dividing the container data and time range into subranges, then selects the top N records from each subrange based on a given configuration.\n")]
        public string SamplingMode { get; } = SamplingModes.TopNExtractor.ToString();

        [Option('n', "sampling-count", HelpText = "Specify the total number of samples to retrieve for each sampling modes:\n" +
                                                  "- TopNExtractor: Total number of records to select.\n" +
                                                  "- EligibleDataSampler: Number of records to retrieve from each partition.\n" +
                                                  "- TimePartitionedSampler: Number of records to retrieve per time range group.")]
        public int? NumberOfRecords { get; }

        [Option("sampling-partition-key-path", HelpText = "Specify the partition key path. This option is applicable only when the 'EligibleDataSampler' mode is selected.")]
        public string? PartitionKeyPath { get; }

        [Option('d', "sampling-days", HelpText = "Specify the number of days to fetch data. Sampling modes include: \n" +
                                                 "- TopNExtractor: Limits records to the most recent days.\n" +
                                                 "- EligibleDataSampler: Limits records from each partition to the specified number of days.\n" +
                                                 "- TimePartitionedSampler: Gathers data over the specified number of days and divides it into subranges.")]
        public int? MaxDays { get; }

        [Option("sampling-group-count", HelpText = "Specify the number of groups for sampling. This option is applicable only when the 'TimePartitionedSampler' mode is selected.")]
        public int? GroupCount { get; }
    }
}
