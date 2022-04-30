using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

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
                        "audience": "",
                        "issuer": ""
                    }
                }
            },
            "entities" : {},
            "resolver-config-file": ""
        }
    */
    public record RuntimeConfig(
        [property: JsonPropertyName(RuntimeConfig.SCHEMA_PROPERTY_NAME)] string Schema,
        [property: JsonPropertyName(DataSource.CONFIG_PROPERTY_NAME)] DataSource DataSource,
        CosmosDbOptions? CosmosDb,
        MsSqlOptions? MsSql,
        PostgreSqlOptions? PostgreSql,
        MySqlOptions? MySql,
        [property: JsonPropertyName(GlobalSettings.CONFIG_PROPERTY_NAME)]
        Dictionary<GlobalSettingsType, object> RuntimeSettings,
        Dictionary<string, Entity> Entities)
    {
        public const string CONFIG_PROPERTY_NAME = "runtime-config";
        public const string CONFIGFILE_PROPERTY_NAME = "runtime-config-file";
        public const string CONFIGFILE_NAME = "hawaii-config";
        public const string CONFIG_EXTENSION = ".json";
        public const string SCHEMA_PROPERTY_NAME = "$schema";
        public const string RESOLVER_CONFIG_PROPERTY_NAME = "resolver-config-file";

        public const string RUNTIME_ENVIRONMENT_VAR_NAME = "HAWAII_ENVIRONMENT";
        public static string ENVIRONMENT_VAR_PREFIX = "HAWAII";

        public static string DefaultRuntimeConfigName
        {
            get
            {
                return $"{CONFIGFILE_NAME}{CONFIG_EXTENSION}";
            }
        }

        public void SetDefaults()
        {
            foreach (
                (GlobalSettingsType settingsType, object settings) in RuntimeSettings)
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

        public static T GetDeserializedConfig<T>(string configJson)
        {
            JsonSerializerOptions options = new()
            {
                PropertyNameCaseInsensitive = true,
            };
            options.Converters.Add(new JsonStringEnumConverter());

            // This feels verbose but it avoids having to make _config nullable - which would result in more
            // down the line issues and null check requirements
            T? deserializedConfig;
            if ((deserializedConfig = JsonSerializer.Deserialize<T>(configJson, options)) == null)
            {
                throw new JsonException("Failed to get a deserialized config from the provided config");
            }

            return deserializedConfig!;
        }

        public AuthenticationConfig? AuthNConfig
        {
            get
            {
                HostGlobalSettings? hostSettings =
                    RuntimeSettings[GlobalSettingsType.Host] as HostGlobalSettings;
                return hostSettings != null ? (hostSettings.Authentication) : null;
            }
        }

        public bool IsEasyAuthAuthenticationProvider()
        {
            return AuthNConfig != null
                   ? AuthNConfig.IsEasyAuthAuthenticationProvider()
                    : false;
        }

        public bool DoesDatabaseTypeHaveValue()
        {
            return DatabaseType.HasValue;
        }

        public DatabaseType? DatabaseType
        {
            get
            {
                return DataSource.DatabaseType;
            }
        }

        public string? ConnectionString
        {
            get
            {
                return DataSource.ConnectionString;
            }
        }
    }

    /// <summary>
    /// Post configuration processing for RuntimeConfig.
    /// We check for database connection options.
    ///
    /// This inteface is called before IValidateOptions. Hence, we need to do some validation here.
    /// </summary>
    public class RuntimeConfigPostConfiguration : IPostConfigureOptions<RuntimeConfig>
    {
        public void PostConfigure(string name, RuntimeConfig options)
        {
            if (!options.DoesDatabaseTypeHaveValue())
            {
                return;
            }

            bool isResolverConfigSet = !string.IsNullOrEmpty(options.DataSource.ResolverConfig);
            bool isResolverConfigFileSet = !string.IsNullOrEmpty(options.DataSource.ResolverConfigFile);
            bool isGraphQLSchemaSet = !string.IsNullOrEmpty(options.DataSource.GraphQLSchema);
            if (!(isResolverConfigSet ^ isResolverConfigFileSet))
            {
                throw new NotSupportedException
                    ("Either the Resolver Config or the Resolver Config File needs to be provided. Not both.");
            }

            if (isResolverConfigSet && !isGraphQLSchemaSet)
            {
                throw new NotSupportedException("The GraphQLSchema should be provided with the config.");
            }

            if (string.IsNullOrWhiteSpace(options.DataSource.ConnectionString))
            {
                throw new NotSupportedException($"The Connection String should be provided.");
            }

            ValidateAuthenticationConfig(options);
        }

        private static void ValidateAuthenticationConfig(RuntimeConfig options)
        {
            bool isAudienceSet =
                options.AuthNConfig != null &&
                options.AuthNConfig.Jwt != null &&
                !string.IsNullOrEmpty(options.AuthNConfig.Jwt.Audience);
            bool isIssuerSet =
                options.AuthNConfig != null &&
                options.AuthNConfig.Jwt != null &&
                !string.IsNullOrEmpty(options.AuthNConfig.Jwt.Issuer);
            if (!options.IsEasyAuthAuthenticationProvider() && (!isAudienceSet || !isIssuerSet))
            {
                throw new NotSupportedException("Audience and Issuer must be set" +
                    " when not using EasyAuth.");
            }

            if (!options.IsEasyAuthAuthenticationProvider() && (isAudienceSet || isIssuerSet))
            {
                throw new NotSupportedException("Audience and Issuer should not be set" +
                    " and are not used with EasyAuth.");
            }
        }
    }

    /// <summary>
    /// Validate config.
    /// This happens after post configuration.
    /// </summary>
    public class RuntimeConfigValidation : IValidateOptions<RuntimeConfig>
    {
        public ValidateOptionsResult Validate(string name, RuntimeConfig options)
        {
            if (!options.DoesDatabaseTypeHaveValue())
            {
                return ValidateOptionsResult.Success;
            }

            return string.IsNullOrWhiteSpace(options.ConnectionString)
                ? ValidateOptionsResult.Fail("Invalid connection string.")
                : ValidateOptionsResult.Success;
        }
    }
}
