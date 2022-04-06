using System.Collections.Generic;
using System.Text.Json;
using Azure.DataGateway.Service.Configurations;

namespace Azure.DataGateway.Service.Services
{
    //Define the backend database and the related connection info
    //Define global/runtime configuration
    //Define what entities are exposed
    //Define the security rules(AuthZ) needed to access those identities
    //Define name mapping rules
    //Define relationships between entities(if not inferrable from the underlying database)
    //Define special/specific behavior related to the chosen backend database
    public class DeveloperConfig
    {
        // Add keys from JSON to make parsing easier as classes
        DataSource DataSource { get; }
        IEnumerable<DataGatewayEntity> Entities { get; }
        RuntimeSettings RuntimeSettings { get; }
        DataGatewayRelationships Relationships { get; }
        DataGatewayPermissions Permissions { get; }

        public DeveloperConfig(string jsonString)
        {
            ReadConfigFile(jsonString);
        }

        private static void ReadConfigFile(string jsonString)
        {
            JsonDocument devConfig = JsonSerializer.Deserialize<JsonDocument>(jsonString);
            devConfig.Equals(string.Empty);
        }
    }
    public class DataSource
    {
        DatabaseType DbType { get; }
        string ConnectionString { get; }
        Dictionary<string, object> DbSettings { get; }
    }

    public class DataGatewayEntity
    {
        string _name;
        JsonDocument _entity;
    }

    public class RuntimeSettings
    {
        IEnumerable<Dictionary<string, object>> Settings { get; }
    }

    public class DataGatewayRelationships
    {
        Dictionary<string, JsonDocument>? Relationships { get; }
    }

    public class DataGatewayPermissions
    {
        IEnumerable<Dictionary<string, JsonDocument>> Permissions { get; }
    }
}
