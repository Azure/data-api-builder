using System.Text.Json.Serialization;

namespace Azure.DataGateway.Config
{
    /// <summary>
    /// Defines:
    ///  the backend database type and the related connection info
    ///  global/runtime configuration settings
    ///  what entities are exposed
    ///  the security rules(AuthZ) needed to access those entities
    ///  name mapping rules
    ///  relationships between entities
    ///  special/specific behavior related to the chosen backend database
    /// </summary>
    /// <param name="Schema">Schema used for validation will also contain version information.</param>
    /// <param name="DataSource">Contains information about which
    /// backend database type to connect to using its connection string.</param>
    /// <param name="CosmosDb/MsSql/MySql/PostgreSql">Different backend database specific options.
    /// Each type is its own dictionary for ease of deserialization.</param>
    /// <param name="RuntimeSettings">These settings are used to set runtime behavior on
    /// all the exposed entities. If not provided in the config, default settings will be set.</param>
    /// <param name="Entities">Represents the mapping between database
    /// objects and an exposed endpoint, along with relationships,
    /// field mapping and permission definition.
    /// By default, the entity names instruct the runtime
    /// to expose GraphQL types with that name and a REST endpoint reachable
    /// via an /entity-name url path.</param>
    public record RuntimeConfig(
        [property: JsonPropertyName("$schema")] string Schema,
        [property: JsonPropertyName("data-source")] DataSource DataSource,
        CosmosDbOptions? CosmosDb,
        MsSqlOptions? MsSql,
        PostgreSqlOptions? PostgreSql,
        MySqlOptions? MySql,
        [property: JsonPropertyName("runtime")]
        Dictionary<GlobalSettingsType, GlobalSettings> RuntimeSettings,
        [property: JsonPropertyName("entities")]
        Dictionary<string, Entity> Entities)
    {
        public void SetDefaults()
        {
            foreach (
                (GlobalSettingsType settingsType, GlobalSettings settings) in RuntimeSettings)
            {
                switch (settingsType)
                {
                    case GlobalSettingsType.Rest:
                        if (settings is not RestGlobalSettings)
                        {
                            RuntimeSettings[settingsType] = new RestGlobalSettings();
                        }

                        break;
                    case GlobalSettingsType.GraphQL:
                        if (settings is not GraphQLGlobalSettings)
                        {
                            RuntimeSettings[settingsType] = new GraphQLGlobalSettings();
                        }

                        break;
                    case GlobalSettingsType.Host:
                        if (settings is not HostGlobalSettings)
                        {
                            RuntimeSettings[settingsType] = new HostGlobalSettings();
                        }

                        break;
                    default:
                        throw new NotSupportedException("The runtime does not " +
                            " support this global settings type.");
                }
            }
        }
    }
}
