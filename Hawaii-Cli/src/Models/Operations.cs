using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Azure.DataGateway.Config;

namespace Hawaii.Cli.Models
{
    /// <summary>
    /// Contains the methods related to commands for the CLI tool.
    /// Contains error or success messages.
    /// </summary>
    public class Operations
    {

        /// <summary>
        /// This method will be triggered when "init" command is used.
        /// It will generate the initial config file.
        /// </summary>
        public static void Init(CommandLineOptions options)
        {
            string? fileName = options.name;
            string? databaseType = options.databaseType;
            string? connectionString = options.connectionString;
            string? resolverConfigFile = options.resolverConfigFile;

            if(fileName == null) {
                Console.WriteLine("Using default file hawaii-config");
                fileName = RuntimeConfigPath.CONFIGFILE_NAME;
            }

            if (databaseType == null || connectionString == null)
            {
                Console.WriteLine(@"Please check if any required arguments are not missing.");
                Console.WriteLine("Required options: --database-type, --connection-string");
                return;
            }
            
            bool isSuccess = ConfigGenerator.GenerateConfig(fileName, resolverConfigFile, databaseType, connectionString);
            if(isSuccess) {
                Console.WriteLine($"Config generated with file name: {fileName}, database type: {databaseType}, and connectionString: {connectionString}");
                Console.WriteLine($"SUGGESTION: Use 'hawaii add <options>' to add new entities in your config.");
            } else {
                Console.WriteLine($"ERROR: Could not generate config with file name: {fileName}, database type: {databaseType}, and connectionString: {connectionString}");
            }
        }


        /// <summary>
        /// This method will be triggered when "add" command is used.
        /// It will add a new entity to the current list of entities.
        /// </summary>
        public static void Add(string entity, CommandLineOptions options)
        {
            string? fileName = options.name;
            string? source = options.source;
            string? rest = options.restRoute;
            string? graphQL = options.graphQLType;
            string? permissions = options.permission;
            string? fieldsToInclude = options.fieldsToInclude;
            string? fieldsToExclude = options.fieldsToExclude;

            if(fileName == null) {
                Console.WriteLine("Using default file hawaii-config");
                fileName = RuntimeConfigPath.CONFIGFILE_NAME;
            }

            if (source == null || permissions == null)
            {
                Console.WriteLine(@"Please check if any required arguments are not missing.");
                Console.WriteLine(@"Required options: --source, --permissions");
                return;
            }

            bool isSuccess = ConfigGenerator.AddEntitiesToConfig(fileName, entity, source, permissions, rest, graphQL, fieldsToInclude, fieldsToExclude);
            if(isSuccess) {
                Console.WriteLine($"Added new entity:{entity} with source: {source} to config: {fileName} with permissions: {permissions}.");
                Console.WriteLine($"SUGGESTION: Use 'hawaii update <options>' to update any entities in your config.");
            } else {
                Console.WriteLine($"ERROR: Could not add entity:{entity} source: {source} to config: {fileName} with permissions: {permissions}.");
            }
        }

        /// <summary>
        /// This method will be triggered when "update" command is used.
        /// It will update an excisting entity.
        /// </summary>
        public static void Update(string entity, CommandLineOptions options)
        {
            string? fileName = options.name;
            string? source = options.source;
            string? rest = options.restRoute;
            string? graphQL = options.graphQLType;
            string? permission = options.permission;
            string? fieldsToInclude = options.fieldsToInclude;
            string? fieldsToExclude = options.fieldsToExclude;
            string? relationship = options.relationship;
            string? targetEntity = options.targetEntity;
            string? cardinality = options.cardinality;
            string? mappingFields = options.mappingFields;

            if(fileName == null) {
                Console.WriteLine("Using default file hawaii-config");
                fileName = RuntimeConfigPath.CONFIGFILE_NAME;
            }
            bool isSuccess = ConfigGenerator.UpdateEntity(fileName, entity, source, permission, rest, graphQL, fieldsToInclude, fieldsToExclude, relationship, targetEntity, cardinality, mappingFields);
            if(isSuccess) {
                Console.WriteLine($"Updated entity:{entity} in the config.");
            } else {
                Console.WriteLine($"Could not update entity:{entity}.");
            }
        }
    }
}
