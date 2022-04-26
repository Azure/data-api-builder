This tool will generate a JSON config from the argument values passed through the CLI tool.

hawaii init --name todo-001 --database-type mssql --connection-string "localhost:5001" 

Above command will generate the below JSON Config file with name todo-001.json:

{
  "data_source": {
    "database_type": "mssql",
    "connection_string": "localhost:5001"
  }
}

we will have a class called CommandLineOptions which will contain all the options available to users:

public sealed class CommandLineOptions
{
    [Option('n', "name", Required = true, HelpText = "file name")]
    public String? name { get; set; }

    [Option("database_type", Required = true, HelpText = "Type of database to connect")]
    public String? databaseType { get; set; }

    [Option("connection_string", Required = false, HelpText = "Type of database to connect")]
    public String? connectionString { get; set; }
}

we can even add a default value and even choose if a particular option should be required or not.

One Class called as CommandLineHelp will parse the command line parameters and validate the commands and options. It will display errors and Help.
    if (fileName == null || databaseType == null || connectionString == null)
    {
        Console.WriteLine(@"Please check if any required arguments are not missing.
                        Required options: -name, -database_type, -connection_string");
        return;
    }
If parsing is succesful. Class Operations is called. This class will contain the different operations that will be conducted on the arguments based on the command : init, add, update

there will be a Config class that will contain keys to be present in the config.
    public class DataSource {
        public string database_type = "";
        public string connection_string = "";
    }
    
    public class Config {
        public DataSource data_source = new DataSource();
    }

Operations class will populate Config class variables from the recieved arguments and then create the Config.

We can use NewtonSoft.JSON, It is a popular high-performance JSON framework for .NET that can be installed from nuget. We can use it for serializing and deserializing JSON.

we can incrementally update this JSON config file using the same framework.