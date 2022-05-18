using System;
using CommandLine;

namespace Hawaii.Cli.Classes
{
    public sealed class CommandLineOptions
    {
        [Option('n', "name", Required = false, HelpText = "Specify the file name, Default value = hawaii-config")]
        public string? name { get; set; }

        [Option("database-type", Required = false, HelpText = "Type of database to connect")]
        public string? databaseType { get; set; }

        [Option("connection-string", Required = false, HelpText = "Connection details to connect to database")]
        public string? connectionString { get; set; }

        //TODO: Link options with Specidied commands
        // we need to make sure certain options are only required with certain commands.
        // for example: source is required only with add/update and not init

        [Option('s', "source", Required = false, HelpText = "Name of the table")]
        public string? source { get; set; }

        [Option("rest", Required = false, HelpText = "Route for rest api")]
        public string? restRoute { get; set; }

        [Option("graphql", Required = false, HelpText = "Type of graphQL")]
        public string? graphQLType { get; set; }

        [Option("permission", Required = false, HelpText = "Permission required to acess source table")]
        public string? permission { get; set; }

        [Option("fields.include", Required = false, HelpText = "Fields that are allowed access to permission")]
        public string? fieldsToInclude { get; set; }

        [Option("fields.exclude", Required = false, HelpText = "Fields that are excluded from the action lists")]
        public string? fieldsToExclude { get; set; }

        [Option("relationship", Required = false, HelpText = "Specify relationship between two entities")]
        public string? relationship { get; set; }

        [Option("target.entity", Required = false, HelpText = "Specify relationship between two entities")]
        public string? targetEntity { get; set; }

        [Option("cardinality", Required = false, HelpText = "Specify cardinality between two entities")]
        public string? cardinality { get; set; }

        [Option("mapping.fields", Required = false, HelpText = "Specify fields to be used for mapping the entities")]
        public string? mappingFields { get; set; }

        [Option(Default = false, HelpText = "Prints all messages to standard output.")]
        public bool Verbose { get; set; }

    }
}
