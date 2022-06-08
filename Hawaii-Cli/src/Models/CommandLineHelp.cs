using System;
using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;

namespace Hawaii.Cli.Models
{
    /// <summary>
    /// Contains methods to parse the Command line Arguments.
    /// </summary>
    public class CommandLineHelp
    {

        /// <summary>
        /// For displaying help with a command argument of --help
        /// </summary>
        public static void DisplayHelp<T>(ParserResult<T> result, IEnumerable<Error> errs)
        {

            string help = HelpText.AutoBuild(result, helpText =>
            {
                helpText.AdditionalNewLineAfterOption = false;
                _ = helpText.AddPreOptionsLine("\nCommands:");
                string[] commandList = {
                    "init    :   this command is used to initialize the configuration file.",
                    "add     :   this command is used to add a new entity.",
                    "update  :   this command is used to update an entity."};
                _ = helpText.AddPreOptionsLines(commandList);
                _ = helpText.AddPreOptionsLine("\nOptions:");
                return HelpText.DefaultParsingErrorsHandler(result, helpText);

            }, e => e);

            Console.WriteLine(help);
            Environment.Exit(0);
        }

        /// <summary>
        /// Parse command line for expected arguments
        /// </summary>
        /// <param name="args">incoming program arguments</param>
        public static void ParseArguments(string[] args)
        {
            Parser? parser = new();

            ParserResult<CommandLineOptions>? results = parser.ParseArguments<CommandLineOptions>(args);

            string? command, entity;
            _ = results.WithParsed(options =>
            {
                command = options.Command;
                switch (command)
                {
                    case "init":
                        Operations.Init(options);
                        break;

                    case "add":
                        entity = options.Entity;
                        if (entity is null)
                        {
                            Console.WriteLine("Please provide a valid Entity.");
                            break;
                        }

                        Operations.Add(entity, options);
                        break;

                    case "update":
                        entity = options.Entity;
                        if (entity is null)
                        {
                            Console.WriteLine("Please provide a valid Entity.");
                            break;
                        }

                        Operations.Update(entity, options);
                        break;

                    default:
                        Console.WriteLine($"ERROR: Could not execute because the specified command was not found.");
                        Console.WriteLine("please do init to initialize the config file.");
                        break;
                }

            }).WithNotParsed(errors => CommandLineHelp.DisplayHelp(results, errors));
        }
    }
}
