using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hawaii.Cli.Classes
{
    public class Operations
    {
        public static void RunWork(CommandLineOptions options)
        {
           var fileName = options.name;
           var databaseType = options.databaseType;
           var connectionString = options.connectionString;

           if (fileName == null || databaseType == null || connectionString == null)
           {
               Console.WriteLine(@"Please check if any required arguments are not missing.
                                Required options: -name, -database_type, -connection_string");
               return;
           }
           ConfigGenerator.generateConfig(fileName, databaseType, connectionString);
           Console.WriteLine($"generating config with file name: {fileName}, database type: {databaseType}, and connectionString: {connectionString}");
        }
    }
}
