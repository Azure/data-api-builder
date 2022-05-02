using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Hawaii.Cli.Classes
{
    public class Operations
    {
        public static void Init(CommandLineOptions options)
        {
            var fileName = options.name;
            var databaseType = options.databaseType;
            var connectionString = options.connectionString;

            if (fileName == null || databaseType == null || connectionString == null)
            {
                Console.WriteLine(@"Please check if any required arguments are not missing.
                Required options: --name, --database_type, --connection_string");
                return;
            }
            ConfigGenerator.generateConfig(fileName, databaseType, connectionString);
            Console.WriteLine($"generating config with file name: {fileName}, database type: {databaseType}, and connectionString: {connectionString}");
        }

        public static void Add(string entity, CommandLineOptions options)
        {
            var fileName = "";
            var source = options.source;
            var restRoute = options.restRoute;
            var graphQLType = options.graphQLType;
            var permissions = options.permissions;

            if (source == null || permissions == null)
            {
                Console.WriteLine(@"Please check if any required arguments are not missing.
                                Required options: --source, --permissions");
                return;
            }

            ConfigGenerator.addEntitiesToConfig(fileName: fileName, entity: entity, source: source, permissions: permissions,
                                                 rest_route: restRoute, graphQL_type: graphQLType);
            Console.WriteLine($"adding source: {source} to config: {fileName} with permissions: {permissions}.");
        }

        public static void Update(string entity, CommandLineOptions options)
        {
            Console.WriteLine($"Updating config.");
        }
    }
}
