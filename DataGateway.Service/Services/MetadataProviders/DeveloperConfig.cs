using System.Collections.Generic;
using System.Text.Json.Serialization;
using Azure.DataGateway.Service.Configurations;

namespace Azure.DataGateway.Service.Services
{
    // Define the backend database and the related connection info
    // Define global/runtime configuration
    // Define what entities are exposed
    // Define the security rules(AuthZ) needed to access those identities
    // Define name mapping rules
    // Define relationships between entities(if not inferrable from the underlying database)
    // Define special/specific behavior related to the chosen backend database
    public class DeveloperConfig
    {
        // Schema used for validation
        [JsonPropertyName("$schema")]
        public string Schema { get; set; }

        // Backend database type and connection string
        [JsonPropertyName("data-source")]
        public DataSource DataSource { get; set; }

        // Backend database types
        public Dictionary<string, object>? Cosmos { get; set; }
        public Dictionary<string, object>? MsSql { get; set; }
        public Dictionary<string, object>? PostgresSql { get; set; }
        public Dictionary<string, object>? MySql { get; set; }

        [JsonPropertyName("runtime")]
        public Dictionary<RuntimeType, object> RuntimeSettings { get; set; }
        public Dictionary<string, DataGatewayEntity> Entities { get; set; }
        public Dictionary<string, DataGatewayRelationship> Relationships { get; set; }
        public DataGatewayPermission[] Permissions { get; set; }
    }

    /// <summary>
    /// Defines the backend database type
    /// and holds the connection string
    /// </summary>
    public class DataSource
    {
        public DatabaseType DatabaseType { get; set; }
        public string ConnectionString { get; set; }
    }

    /// <summary>
    /// Defines the Entities that are exposed
    /// </summary>
    public class DataGatewayEntity
    {
        public string Source { get; set; }
        public DataGatewayPermission[] Permissions { get; set; }
    }

    /// <summary>
    /// Defines the relationships between entities
    /// that can not be infered
    /// </summary>
    public class DataGatewayRelationship
    {
        public string Cardinality { get; set; }
        [JsonPropertyName("target.entity")]
        public string TargetEntity { get; set; }
        [JsonPropertyName("source.fields")]
        public string[] SourceFields { get; set; }
        [JsonPropertyName("target.fields")]
        public string[] TargetFields { get; set; }
        [JsonPropertyName("linking.[object]")]
        public string? LinkingObject { get; set; }
        [JsonPropertyName("linking.[entity]")]
        public string? LinkingEntity { get; set; }
        [JsonPropertyName("linking.source.fields")]
        public string[] LinkingSourceFields { get; set; }
        [JsonPropertyName("linking.target.fields")]
        public string[] LinkingTargetFields { get; set; }
    }

    /// <summary>
    /// Defines the security rules
    /// </summary>
    public class DataGatewayPermission
    {
        public string? Role { get; set; }
        public string[]? Actions { get; set; }
        public string[]? Policies { get; set; }
    }

    /// <summary>
    /// Different runtime configuration types
    /// </summary>
    public enum RuntimeType
    {
        Rest,
        GraphQL,
        Host

    }
}
