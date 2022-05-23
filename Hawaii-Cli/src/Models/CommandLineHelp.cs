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
                helpText.Heading = "See all the available options";

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
            Parser parser = new CommandLine.Parser();

            ParserResult<CommandLineOptions> results = parser.ParseArguments<CommandLineOptions>(args);

            string? command, entity;
            results.WithParsed(options => {
                command = options.command;
                switch (command)
                {
                    case "init":
                        Operations.Init(options);
                        break;

                    case "add":
                        entity = options.entity;
                        if(entity==null) {
                            Console.WriteLine("Please provide a valid Entity.");
                            break;
                        }

                        Operations.Add(entity, options);
                        break;

                    case "update":
                        entity = options.entity;
                        if(entity==null) {
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
