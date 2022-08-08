using System.Text.Json;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config
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
    /*
        * This is an example of the configuration format
        *
        {
            "$schema": "",
            "data-source": {
                "database-type": "mssql",
                "connection-string": ""
            },
            "mssql": {},
            "runtime": {
                "host": {
                    "authentication": {
                        "provider": "",
                        "jwt": {
                            "audience": "",
                            "issuer": "",
                            "issuer-key": ""
                        }
                    }
                }
            },
            "entities" : {},
        }
    */
    public record RuntimeConfig(
        [property: JsonPropertyName(RuntimeConfig.SCHEMA_PROPERTY_NAME)] string Schema,
        [property: JsonPropertyName(DataSource.JSON_PROPERTY_NAME)] DataSource DataSource,
        [property: JsonPropertyName(CosmosDbOptions.JSON_PROPERTY_NAME)]
        CosmosDbOptions? CosmosDb,
        [property: JsonPropertyName(MsSqlOptions.JSON_PROPERTY_NAME)]
        MsSqlOptions? MsSql,
        [property: JsonPropertyName(PostgreSqlOptions.JSON_PROPERTY_NAME)]
        PostgreSqlOptions? PostgreSql,
        [property: JsonPropertyName(MySqlOptions.JSON_PROPERTY_NAME)]
        MySqlOptions? MySql,
        [property: JsonPropertyName(GlobalSettings.JSON_PROPERTY_NAME)]
        Dictionary<GlobalSettingsType, object>? RuntimeSettings,
        [property: JsonPropertyName(Entity.JSON_PROPERTY_NAME)]
        Dictionary<string, Entity> Entities)
    {
        public const string SCHEMA_PROPERTY_NAME = "$schema";
        public const string SCHEMA = "hawaii.draft-01.schema.json";

        // use camel case
        // convert Enum to strings
        // case insensitive
        public readonly static JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            Converters =
                {
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
                }
        };

        /// <summary>
        /// Pick up the global runtime settings from the dictionary if present
        /// otherwise initialize with default.
        /// </summary>
        public void DetermineGlobalSettings()
        {
            if (RuntimeSettings is not null)
            {
                foreach (
                (GlobalSettingsType settingsType, object settingsJson) in RuntimeSettings)
                {
                    switch (settingsType)
                    {
                        case GlobalSettingsType.Rest:
                            RestGlobalSettings
                                = ((JsonElement)settingsJson).Deserialize<RestGlobalSettings>(SerializerOptions)!;
                            break;
                        case GlobalSettingsType.GraphQL:
                            GraphQLGlobalSettings =
                                ((JsonElement)settingsJson).Deserialize<GraphQLGlobalSettings>(SerializerOptions)!;
                            break;
                        case GlobalSettingsType.Host:
                            HostGlobalSettings =
                                ((JsonElement)settingsJson).Deserialize<HostGlobalSettings>(SerializerOptions)!;
                            break;
                        default:
                            throw new NotSupportedException("The runtime does not " +
                                " support this global settings type.");
                    }
                }
            }
        }

        /// <summary>
        /// Try to deserialize the given json string into its object form.
        /// </summary>
        /// <typeparam name="T">The object type.</typeparam>
        /// <param name="configJson">Json string to be deserialized.</param>
        /// <param name="deserializedConfig">Deserialized json object upon success.</param>
        /// <returns>True on success, false otherwise.</returns>
        public static bool TryGetDeserializedConfig<T>(
            string configJson,
            out T? deserializedConfig)
        {
            try
            {
                deserializedConfig = JsonSerializer.Deserialize<T>(configJson, SerializerOptions);
                return true;
            }
            catch (JsonException ex)
            {
                // until this function is refactored to exist in RuntimeConfigProvider
                // we must use Console for logging.
                Console.Error.WriteLine($"Deserialization of the configuration file failed.\n" +
                    $"Message:\n {ex.Message}\n" +
                    $"Stack Trace:\n {ex.StackTrace}");

                deserializedConfig = default(T);
                return false;
            }
        }

        [JsonIgnore]
        public RestGlobalSettings RestGlobalSettings { get; private set; } = new();

        [JsonIgnore]
        public GraphQLGlobalSettings GraphQLGlobalSettings { get; private set; } = new();

        [JsonIgnore]
        public HostGlobalSettings HostGlobalSettings { get; private set; } = new();

        public bool IsEasyAuthAuthenticationProvider()
        {
            // by default, if there is no AuthenticationSection,
            // EasyAuth StaticWebApps is the authentication scheme.
            return AuthNConfig is null ||
                   AuthNConfig!.IsEasyAuthAuthenticationProvider();
        }

        [JsonIgnore]
        public DatabaseType DatabaseType
        {
            get
            {
                return DataSource.DatabaseType;
            }
        }

        [JsonIgnore]
        public string ConnectionString
        {
            get
            {
                return DataSource.ConnectionString;
            }

            set
            {
                DataSource.ConnectionString = value;
            }
        }

        [JsonIgnore]
        public AuthenticationConfig? AuthNConfig
        {
            get
            {
                return HostGlobalSettings.Authentication;
            }
        }

        [JsonIgnore]
        public string DatabaseTypeNotSupportedMessage => $"The provided database-type value: {DatabaseType} is currently not supported. Please check the configuration file.";
    }
}
