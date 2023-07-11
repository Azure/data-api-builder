// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using CommandLine;

namespace Cli.Commands
{
    [Verb("export", isDefault: false, HelpText = "Export the GraphQL schema as a file and save to disk", Hidden = false)]
    public class ExportOptions : Options
    {
        public ExportOptions(bool graphql, string outputDirectory, string? config, string? graphqlSchemaFile) : base(config)
        {
            GraphQL = graphql;
            OutputDirectory = outputDirectory;
            GraphQLSchemaFile = graphqlSchemaFile ?? "schema.graphql";
        }

        [Option("graphql", HelpText = "Export GraphQL schema")]
        public bool GraphQL { get; }

        [Option('o', "output", HelpText = "Directory to save to", Required = true)]
        public string OutputDirectory { get; }

        [Option('g', "graphql-schema-file", HelpText = "The GraphQL schema file name (default schema.graphql)")]
        public string GraphQLSchemaFile { get; }
    }
}
