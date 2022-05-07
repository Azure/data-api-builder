using System.Text.Json;
using Azure.DataGateway.Config;
using static Hawaii.Cli.Classes.Utils;

namespace Hawaii.Cli.Classes
{
    public class ConfigGenerator
    {
        public static void GenerateConfig(string fileName, string database_type, string connection_string)
        {

            DatabaseType dbType = Enum.Parse<DatabaseType>(database_type);
            DataSource dataSource = new DataSource(dbType, connection_string);

            string file = fileName + ".json";

            string schema = Directory.GetCurrentDirectory().Replace("\\", "/") + "/" + file;

            RuntimeConfig runtimeConfig = new RuntimeConfig(schema, dataSource, null, null, null, null, GetRuntimeSettings(), new Dictionary<string, Entity>());

            string JSONresult = JsonSerializer.Serialize(runtimeConfig, GetSerializationOptions());

            if (File.Exists(file)) {
                File.Delete(file);
            }
            File.WriteAllText(file, JSONresult);

        }

        public static void AddEntitiesToConfig(string fileName, string entity,
                                             object source, string permissions,
                                             object? rest, object? graphQL, string? fieldsToInclude, string? fieldsToExclude)
        {
            string file = fileName + ".json";

            if (!File.Exists(file))
            {
                Console.WriteLine($"Couldn't find config  file: {file}.");
                Console.WriteLine($"Please do hawaii init <options> to create a new config file.");
                return;
            }
            string[] permission_array = permissions.Split(":");
            string role = permission_array[0];
            string actions = permission_array[1];
            PermissionSetting[] permissionSettings = CreatePermissions(role, actions, fieldsToInclude, fieldsToExclude);
            Entity entity_details = new Entity(source, GetRestDetails(rest), GetGraphQLDetails(graphQL), permissionSettings, null, null);

            

            string jsonString = File.ReadAllText(file);
            var options = GetSerializationOptions();

            RuntimeConfig? runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(jsonString, options);

            if(runtimeConfig.Entities.ContainsKey(entity)) {
                Console.WriteLine($"Entity:{entity} is already present. No new changes are added to Config.");
                return;
            }

            runtimeConfig.Entities.Add(entity, entity_details);

            string JSONresult = JsonSerializer.Serialize(runtimeConfig, options);
            File.WriteAllText(file, JSONresult);

        }

        public static void UpdateEntity(string fileName, string entity,
                                             object? source, string? permissions,
                                             object? rest, object? graphQL, string? fieldsToInclude, string? fieldsToExclude) {
            
            string file = fileName + ".json";
            string jsonString = File.ReadAllText(file);
            var options = GetSerializationOptions();

            RuntimeConfig? runtimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(jsonString, options);
            if(!runtimeConfig.Entities.ContainsKey(entity)) {
                Console.WriteLine($"Entity:{entity} is not present. No new changes are added to Config.");
                return;
            }
            
            Entity updatedEntity = runtimeConfig.Entities[entity];
            if(source!=null) {
                updatedEntity = new Entity(source, updatedEntity.Rest, updatedEntity.GraphQL, updatedEntity.Permissions, updatedEntity.Relationships, updatedEntity.Mappings);
            }
            if(rest!=null) {
                updatedEntity = new Entity(updatedEntity.Source, GetRestDetails(rest), updatedEntity.GraphQL, updatedEntity.Permissions, updatedEntity.Relationships, updatedEntity.Mappings);
            }
            if(graphQL!=null) {
                updatedEntity = new Entity(updatedEntity.Source, updatedEntity.Rest, GetGraphQLDetails(graphQL), updatedEntity.Permissions, updatedEntity.Relationships, updatedEntity.Mappings);
            }
            runtimeConfig.Entities[entity] = updatedEntity;
            string JSONresult = JsonSerializer.Serialize(runtimeConfig, options);
            File.WriteAllText(file, JSONresult);

        }

    }
}