using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hawaii.Cli.Classes
{
    public class Operations
    {
        public static string DEFAULT_CONFIG_FILENAME = "hawaii-config";
        public static void Init(CommandLineOptions options)
        {
            var fileName = options.name;
            var databaseType = options.databaseType;
            var connectionString = options.connectionString;

            if(fileName == null) {
                Console.WriteLine("Using default file hawaii-config");
                fileName = DEFAULT_CONFIG_FILENAME;
            }

            if (databaseType == null || connectionString == null)
            {
                Console.WriteLine(@"Please check if any required arguments are not missing.
                Required options: --database_type, --connection_string");
                return;
            }
            ConfigGenerator.GenerateConfig(fileName, databaseType, connectionString);
            Console.WriteLine($"generating config with file name: {fileName}, database type: {databaseType}, and connectionString: {connectionString}");
        }

        public static void Add(string entity, CommandLineOptions options)
        {
            var fileName = options.name;
            var source = options.source;
            var rest = options.restRoute;
            var graphQL = options.graphQLType;
            var permissions = options.permissions;
            var fieldsToInclude = options.fieldsToInclude;
            var fieldsToExclude = options.fieldsToExclude;

            if(fileName == null) {
                Console.WriteLine("Using default file hawaii-config");
                fileName = DEFAULT_CONFIG_FILENAME;
            }

            if (source == null || permissions == null)
            {
                Console.WriteLine(@"Please check if any required arguments are not missing.
                                Required options: --source, --permissions");
                return;
            }

            ConfigGenerator.AddEntitiesToConfig(fileName, entity, source, permissions, rest, graphQL, fieldsToInclude, fieldsToExclude);
            Console.WriteLine($"adding source: {source} to config: {fileName} with permissions: {permissions}.");
        }

        public static void Update(string entity, CommandLineOptions options)
        {
            var fileName = options.name;
            var source = options.source;
            var rest = options.restRoute;
            var graphQL = options.graphQLType;
            var permissions = options.permissions;
            var fieldsToInclude = options.fieldsToInclude;
            var fieldsToExclude = options.fieldsToExclude;

            if(fileName == null) {
                Console.WriteLine("Using default file hawaii-config");
                fileName = DEFAULT_CONFIG_FILENAME;
            }
            ConfigGenerator.UpdateEntity(fileName, entity, source, permissions, rest, graphQL, fieldsToInclude, fieldsToExclude);
            Console.WriteLine($"Updating config.");
        }
    }
}
