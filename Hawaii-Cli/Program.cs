using Hawaii.Cli.Classes;

var command = args.AsQueryable().FirstOrDefault();

Console.WriteLine("Welcome to Hawaii.Cli");
CommandLineHelp.ParseArguments(args);

