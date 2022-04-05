using System.Collections.Generic;
using System.Text.Json;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Models;

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
        DatabaseType DbType { get; }
        string ConnectionString { get; }
        Dictionary<string, object> DbSettings { get; }
        IEnumerable<DataGatewayEntity> Entities { get; }
        IEnumerable<Dictionary<string, object>> RuntimeSettings { get; }
        Dictionary<string, JsonDocument> Relationships { get; }
        IEnumerable<Dictionary<string, JsonDocument>> Permissions { get; }

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
}
