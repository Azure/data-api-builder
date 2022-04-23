using System.Text.Json.Serialization;

namespace Azure.DataGateway.Config
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
        public CosmosOptions? Cosmos { get; set; }
        public MsSqlOptions? MsSql { get; set; }
        public PostgreSqlOptions? PostgreSql { get; set; }
        public MySqlOptions? MySql { get; set; }

        // These settings are used to set runtime behavior and
        // any exposed entity.
        [JsonPropertyName("runtime")]
        public Dictionary<GlobalSettingsType, object>? RuntimeSettings { get; set; }

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
        public string Database { get; set; }
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
    public class PostgreSqlOptions
    {
    }

    /// <summary>
    /// Options for MySql database.
    /// </summary>
    public class MySqlOptions
    {
    }

    public abstract class GlobalSettings
    {
    }

    public abstract class ApiSettings
    {
        public bool Enabled { get; set; } = true;
        public abstract string Path { get; set; }
    }

    /// <summary>
    /// Holds the settings used at runtime.
    /// </summary>
    public class RestGlobalSettings : ApiSettings
    {
        public override string Path { get; set; } = "/api";
    }

    public class HostGlobalSettings : GlobalSettings
    {
        public Host? HostObject { get; set; }
    }

    public class GraphQLGlobalSettings : ApiSettings
    {
        public bool? AllowIntrospection { get; set; }
        public override string Path { get; set; } = "/graphql";
    }

    /// <summary>
    /// Defines the Entities that are exposed
    /// </summary>
    public class DataGatewayEntity
    {
        // Describes the object in the backend mapped to this entity
        public string Source { get; set; }

        // REST can be bool or RestSetting type so we use object for now
        // can be 3 things, need class(es) to support that
        // these are entity specific settings so need to differentiate from the global settings, call
        // something like RestEntitySettings and then inhereit in a way that it can be
        // bool, string, or singularplural

        public object? Rest { get; set; }

        // GraphQL can be bool or GraphQLSettings type so we use object
        // same as above
        public object? GraphQL { get; set; }

        // The permissions assigned to this object
        public DataGatewayPermission[] Permissions { get; set; }

        // Relationships defines how an entity is related to other exposed
        // entities and optionally provide details on what underlying database
        // objects can be used to support such relationships.
        public DataGatewayRelationship? Relationships { get; set; }

        // Define mappings between database fields and GraphQL and REST fields
        public Dictionary<string, string>? Mappings { get; set; }
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

        // Database object that is used in the backend
        // database to support the M:N relationship.
        [JsonPropertyName("linking.[object]")]
        public string? LinkingObject { get; set; }

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
        // Either a string or a mixed-type array that details what actions are allowed to related roles.
        // In a simple case, value is one of the following: create, read, update, delete.
        // Use object since can be string or array.
        public object Actions { get; set; }
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

    public class Host
    {
        public Cors? Cors { get; set; }
        public Auth? Auth { get; set; }
    }

    public class Cors
    {
        public string[]? Origins { get; set; }
        public bool? Credential { get; set; }
    }

    public class Auth
    {
        // might be optional if easy auth (ping sean)
        public Jwt Jwt { get; set; }
    }

    public class Jwt
    {
        public string Audience { get; set; }
        public string Issuer { get; set; }
        public string IssuerKey { get; set; }
    }

    public class Actions
    {
        // Details what actions are allowed
        public string Action { get; set; }

        // Details what fields to include or exclude
        public Fields? Fields { get; set; }

        // Policy contains details about item-level security rules.
        public Policy? Policy { get; set; }
    }

    public class Fields
    {
        public string[]? Include { get; set; }
        public string[]? Exclude { get; set; }
    }

    /// <summary>
    /// Policy contains detail about item-level security rules.
    /// </summary>
    public class Policy
    {
        public string? Request { get; set; }
        public string? Database { get; set; }
    }

    /// <summary>
    /// Enum for possible database types, name
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
