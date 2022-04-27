using System;
using CommandLine;

namespace Hawaii.Cli.Classes
{
    public sealed class CommandLineOptions
    {
        [Option('n', "name", Required = true, HelpText = "file name")]
        public String? name { get; set; }

        [Option("database_type", Required = true, HelpText = "Type of database to connect")]
        public String? databaseType { get; set; }

        [Option("connection_string", Required = false, HelpText = "Type of database to connect")]
        public String? connectionString { get; set; }

        [Option(Default = false, HelpText = "Prints all messages to standard output.")]
        public bool Verbose { get; set; }

    }
}
