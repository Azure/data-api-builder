using Hawaii.Cli.Classes;
using System.CommandLine;
using System.CommandLine.Invocation;

var command = args.AsQueryable().FirstOrDefault();

Console.WriteLine("Welcome to Hawaii.Cli");
CommandLineHelp.ParseArguments(args);
