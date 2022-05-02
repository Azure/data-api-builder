using System;
using System.Collections.Generic;
using CommandLine;
using CommandLine.Text;

namespace Hawaii.Cli.Classes
{
    public class CommandLineHelp
    {

        /// <summary>
        /// For displaying help with a command argument of --help
        /// </summary>
        public static void DisplayHelp<T>(ParserResult<T> result, IEnumerable<Error> errs)
        {

            var help = HelpText.AutoBuild(result, helpText =>
            {
                helpText.AdditionalNewLineAfterOption = false;
                helpText.Heading = "Get your subscription duration";

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
            Parser parser = new CommandLine.Parser(with => with.HelpWriter = null);

            ParserResult<CommandLineOptions> results = parser.ParseArguments<CommandLineOptions>(args);

            // results.WithParsed<CommandLineOptions>(Operations.RunWork).WithNotParsed(errors =>
            //     CommandLineHelp.DisplayHelp(results, errors));

            var command = args.AsQueryable().FirstOrDefault();
            string entity = "";

            switch (command)
            {
                case "init":
                    results.WithParsed<CommandLineOptions>(Operations.Init)
                           .WithNotParsed(errors => CommandLineHelp.DisplayHelp(results, errors));
                    break;

                case "add":
                    entity = args.AsQueryable().ElementAt(1);
                    results.WithParsed<CommandLineOptions>(opt => Operations.Add(entity, opt))
                            .WithNotParsed(errors => CommandLineHelp.DisplayHelp(results, errors));
                    break;

                case "update":
                    entity = args.AsQueryable().ElementAt(1);
                    results.WithParsed<CommandLineOptions>(opt => Operations.Update(entity, opt))
                            .WithNotParsed(errors => CommandLineHelp.DisplayHelp(results, errors));
                    break;

                default:
                    Console.WriteLine($"Could not execute because the specified command was not found.");
                    Console.WriteLine("please do init to initialize the config file.");
                    break;

            }

        }
    }



}
