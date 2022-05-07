using System;
using CommandLine;

namespace Hawaii.Cli.Classes
{
    public sealed class CommandLineOptions
    {
        [Option('n', "name", Required = false, HelpText = "file name")]
        public String? name { get; set; }

        [Option("database_type", Required = false, HelpText = "Type of database to connect")]
        public String? databaseType { get; set; }

        [Option("connection_string", Required = false, HelpText = "Connection details to connect to database")]
        public String? connectionString { get; set; }

        //TODO: Link options with Specidied commands
        // we need to make sure certain options are only required with certain commands.
        // for example: source is required only with add/update and not init

        [Option('s', "source", Required = false, HelpText = "name of the table")]
        public String? source { get; set; }

        [Option("rest", Required = false, HelpText = "route for rest api")]
        public String? restRoute { get; set; }

        [Option("graphql", Required = false, HelpText = "Type of graphQL")]
        public String? graphQLType { get; set; }

        [Option("permissions", Required = false, HelpText = "permission required to acess source table")]
        public String? permissions { get; set; }

        [Option("fields.include", Required = false, HelpText = "fields that are allowed access to permission")]
        public String? fieldsToInclude { get; set; }

        [Option("fields.exclude", Required = false, HelpText = "fields that are excluded from the action lists")]
        public String? fieldsToExclude { get; set; }

        [Option(Default = false, HelpText = "Prints all messages to standard output.")]
        public bool Verbose { get; set; }

    }
}
