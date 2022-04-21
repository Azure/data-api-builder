using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Azure.DataGateway.Service.Models
{
    /// <summary>
    /// Define the backend database and the related connection info
    /// Define global/runtime configuration
    /// Define what entities are exposed
    /// Define the security rules(AuthZ) needed to access those identities
    /// Define name mapping rules
    /// Define relationships between entities(if not inferrable from the underlying database)
    /// Define special/specific behavior related to the chosen backend database
    /// </summary>
    public class RuntimeConfig
    {
        // Schema used for validation
        // will also contain version information
        // may replace with versioning only
        // or remove entirely from this class
        [JsonPropertyName("$schema")]
        public string Schema { get; set; }

        // DataSource contains information about which
        // back end database type and its connection string
        [JsonPropertyName("data-source")]
        public DataSource DataSource { get; set; }

        // The different back end database types.
        // We have each type as its own dictionary for
        // ease of deserialization.
        public Dictionary<string, CosmosOptions>? Cosmos { get; set; }
        public Dictionary<string, MsSqlOptions>? MsSql { get; set; }
        public Dictionary<string, PostgresSqlOptions>? PostgresSql { get; set; }
        public Dictionary<string, MySqlOptions>? MySql { get; set; }

        // These settings are used to set runtime behavior and
        // any exposed entity.
        [JsonPropertyName("runtime")]
        public Dictionary<GlobalSettingsType, GlobalSettings>? RuntimeSettings { get; set; }

        // Entities represents the mapping between database
        // objects and an exposed endpoint, along with property
        // mapping and permission definition.
        // Each exposed entity is enclosed in a dedicated section,
        // that has the name of the entity to be exposed.
        public Dictionary<string, DataGatewayEntity> Entities { get; set; }
    }

    /// <summary>
    /// Defines the backend database type
    /// and holds the connection string
    /// </summary>
    public class DataSource
    {
        public Database DatabaseType { get; set; }
        public string ConnectionString { get; set; }
    }

    /// <summary>
    /// Options for Cosmos database.
    /// </summary>
    public class CosmosOptions
    {
        public string Databaase { get; set; }
    }

    /// <summary>
    /// Options for MsSql database.
    /// </summary>
    public class MsSqlOptions
    {
        public bool SetSessionContext { get; set; }
    }

    /// <summary>
    /// Options for PostgresSql database.
    /// </summary>
    public class PostgresSqlOptions
    {
    }

    /// <summary>
    /// Options for MySql database.
    /// </summary>
    public class MySqlOptions
    {
    }

    /// <summary>
    /// Holds the settings used at runtime.
    /// </summary>
    public class GlobalSettings
    {
        // For GraphQL and REST
        public bool? Enabled { get; set; }
        //For GraphQL and REST
        public string? Path { get; set; }
        // For GraphQL
        public bool? AllowIntrospection { get; set; }
        // For Host
        public object? HostObject { get; set; }
    }
    /// <summary>
    /// Defines the Entities that are exposed
    /// </summary>
    public class DataGatewayEntity
    {
        // Describes the object in the backend mapped to this entity
        public string Source { get; set; }

        // REST can be bool or RestSetting type so we use object
        public object Rest { get; set; }

        // GraphQL can be bool or GraphQLSettings type so we use object
        public object GraphQL { get; set; }

        // The permissions assigned to this object
        public DataGatewayPermission[] Permissions { get; set; }

        // Relationships defines how an entity is related to other exposed
        // entities and optionally provide details on what underlying database
        // objects can be used to support such relationships.
        public DataGatewayRelationship Relationships { get; set; }
    }

    /// <summary>
    /// Defines the relationships between entities
    /// that can not be infered.
    /// </summary>
    public class DataGatewayRelationship
    {
        // One or Many
        public Cardinality Cardinality { get; set; }

        // An exposed entity
        [JsonPropertyName("target.entity")]
        public string TargetEntity { get; set; }

        // Can be used to designate which columns
        // to be used in the source entity.
        [JsonPropertyName("source.fields")]
        public string[]? SourceFields { get; set; }

        // Can be used to designate which columns
        // to be used in the target entity we connect to.
        [JsonPropertyName("target.fields")]
        public string[]? TargetFields { get; set; }

        // Database object (if not exposed via Hawaii) that is used
        // in the backend database to support the M:N relationship.
        [JsonPropertyName("linking.[object]")]
        public string? LinkingObject { get; set; }

        // Database entity (if exposed via Hawaii) that is used
        // to support the M:N relationship.
        [JsonPropertyName("linking.[entity]")]
        public string? LinkingEntity { get; set; } // missing from schema?

        // Database fields in the linking object or entity that
        // will be used to connect to the related item in the source entity.
        [JsonPropertyName("linking.source.fields")]
        public string[] LinkingSourceFields { get; set; }

        // Database fields in the linking object or entity that
        // will be used to connect to the related item in the target entity
        [JsonPropertyName("linking.target.fields")]
        public string[] LinkingTargetFields { get; set; }
    }

    /// <summary>
    /// Defines the security rules
    /// </summary>
    public class DataGatewayPermission
    {
        // Role contains the name of the role to which the defined permission will apply.
        public string Role { get; set; }
        // Mixed-type array that details what actions are allowed to related roles.
        // In a simple case, value is one of the following: create, read, update, delete
        public object[] Actions { get; set; }
        // Policy contains details about item-level security rules.
        public Policy[]? Policies { get; set; }
    }

    /// <summary>
    /// RestSettings defines if the entity is exposed as a REST endpoint or not.
    /// If exposed, it can also define the name of the url path at which the entity will be available.
    /// </summary>
    public class RestSettings
    {
        // Object because either string or singularPlural object
        public object Route { get; set; }
    }

    /// <summary>
    /// GraphQLSettings defines if the entity is exposed as a GraphQL type or not.
    /// If exposed, it can also define the name of the GraphQL type that will be used for that entity.
    /// </summary>
    public class GraphQLSettings
    {
        // Object because either string or singularPlural object.
        public object Type { get; set; }
    }

    /// <summary>
    /// Defines a name or route as singular (required) or
    /// plural (optional)
    /// </summary>
    public class SingularPlural
    {
        public string Singular { get; set; }
        public string? Plural { get; set; }

    }

    /// <summary>
    /// Policy contains detail about item-level security rules.
    /// </summary>
    public class Policy
    {
        public string Request { get; set; }
        public string Database { get; set; }
    }

    /// <summary>
    /// Enum for possibly database types, name
    /// will change to DatabaseType but for now
    /// is Database to avoid collisions until
    /// this config is used.
    /// </summary>
    public enum Database
    {
        mssql,
        postgresql,
        mysql,
        mariadb,
        cosmosdb
    }

    /// <summary>
    /// Different runtime configuration types
    /// </summary>
    public enum GlobalSettingsType
    {
        Rest,
        GraphQL,
        Host
    }

    /// <summary>
    /// Kinds of relationship cardinality.
    /// </summary>
    public enum Cardinality
    {
        One,
        Many
    }
}
