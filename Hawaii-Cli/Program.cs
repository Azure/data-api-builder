using Hawaii.Cli.Classes;
using System.CommandLine;
using System.CommandLine.Invocation;

var command = args.AsQueryable().FirstOrDefault();

Console.WriteLine("Welcome to Hawaii.Cli");
if (command == "init")
{
    CommandLineHelp.ParseArguments(args);
}
else
{
    Console.WriteLine($"Could not execute because the specified command was not found.");
    Console.WriteLine("please do init to initialize the config file.");
}
