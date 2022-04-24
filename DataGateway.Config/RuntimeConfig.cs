using System.Text.Json.Serialization;

namespace Azure.DataGateway.Config
{
    /// <summary>
    /// Defines:
    ///  the backend database type and the related connection info
    ///  global/runtime configuration settings
    ///  what entities are exposed
    ///  the security rules(AuthZ) needed to access those identities
    ///  name mapping rules
    ///  relationships between entities(if not inferrable from the underlying database)
    ///  special/specific behavior related to the chosen backend database
    /// </summary>
    /// <param name="Schema">Schema used for validation will also contain version information.</param>
    /// <param name="DataSource">DataSource contains information about which
    /// back end database type to connect to using its connection string.</param>
    /// <param name="Entities">Represents the mapping between database
    /// objects and an exposed endpoint, along with relationships,
    /// field mapping and permission definition.
    /// By default, the entity names instruct the runtime
    /// to expose a GraphQL types with that name and a REST endpoint reachable
    /// via an /entity-name url path.</param>
    /// <param name="Cosmos/MsSql/MySql/PostgreSql">Different backend database specific options.
    /// We have each type as its own dictionary for ease of deserialization.</param>
    /// <param name="RuntimeSettings">These settings are used to set runtime behavior on
    /// all the exposed entities. If not provided in the config, default settings will be set.</param>
    public record RuntimeConfig(
        [property: JsonPropertyName("$schema")] string Schema,
        [property: JsonPropertyName("data-source")] DataSource DataSource,
        Dictionary<string, Entity> Entities,
        CosmosOptions? Cosmos,
        MsSqlOptions? MsSql,
        PostgreSqlOptions? PostgreSql,
        MySqlOptions? MySql,
        [property: JsonPropertyName("runtime")]
        Dictionary<GlobalSettingsType, GlobalSettings> RuntimeSettings);
}
