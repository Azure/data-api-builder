// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging;

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
            "runtime": {
                "host": {
                    "authentication": {
                        "provider": "",
                        "jwt": {
                            "audience": "",
                            "issuer": ""
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
        [property: JsonPropertyName(GlobalSettings.JSON_PROPERTY_NAME)]
        Dictionary<GlobalSettingsType, object>? RuntimeSettings,
        [property: JsonPropertyName(Entity.JSON_PROPERTY_NAME)]
        Dictionary<string, Entity> Entities)
    {
        public const string SCHEMA_PROPERTY_NAME = "$schema";
        public const string SCHEMA = "dab.draft.schema.json";

        // use camel case
        // convert Enum to strings
        // case insensitive
        public readonly static JsonSerializerOptions SerializerOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            // As of .NET Core 7, JsonDocument and JsonSerializer only support skipping or disallowing 
            // of comments; they do not support loading them. If we set JsonCommentHandling.Allow for either,
            // it will throw an exception.
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters =
                {
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
                }
        };

        /// <summary>
        /// Pick up the global runtime settings from the dictionary if present
        /// otherwise initialize with default.
        /// </summary>
        /// <param name="logger">ILogger instance to log warning.</param>
        /// <exception cref="NotSupportedException"></exception>
        public void DetermineGlobalSettings(ILogger? logger = null)
        {
            if (RuntimeSettings is not null)
            {
                foreach (
                (GlobalSettingsType settingsType, object settingsJson) in RuntimeSettings)
                {
                    switch (settingsType)
                    {
                        case GlobalSettingsType.Rest:
                            if (DatabaseType is DatabaseType.cosmosdb_nosql)
                            {
                                string warningMsg = "REST runtime settings will not be honored for " +
                                        $"{DatabaseType} as it does not support REST yet.";
                                if (logger is null)
                                {
                                    Console.WriteLine(warningMsg);
                                }
                                else
                                {
                                    logger.LogWarning(warningMsg);
                                }
                            }

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
        /// This method reads the dab.draft.schema.json which contains the link for online published
        /// schema for dab, based on the version of dab being used to generate the runtime config.
        /// </summary>
        public static string GetPublishedDraftSchemaLink()
        {
            string? assemblyDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);

            if (assemblyDirectory is null)
            {
                throw new DataApiBuilderException(
                    message: "Could not get the link for DAb draft schema.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            string? schemaPath = Path.Combine(assemblyDirectory, "dab.draft.schema.json");
            string schemaFileContent = File.ReadAllText(schemaPath);
            Dictionary<string, object>? jsonDictionary = JsonSerializer.Deserialize<Dictionary<string, object>>(schemaFileContent, SerializerOptions);

            if (jsonDictionary is null)
            {
                throw new DataApiBuilderException(
                    message: "The schema file is misconfigured. Please check the file formatting.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            object? additionalProperties;
            if (!jsonDictionary.TryGetValue("additionalProperties", out additionalProperties))
            {
                throw new DataApiBuilderException(
                    message: "The schema file doesn't have the required field : additionalProperties",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            // properties cannot be null since the property additionalProperties exist in the schema file.
            Dictionary<string, string> properties = JsonSerializer.Deserialize<Dictionary<string, string>>(additionalProperties.ToString()!)!;

            string? versionNum;
            if (!properties.TryGetValue("version", out versionNum))
            {
                throw new DataApiBuilderException(message: "Missing required property 'version' in additionalProperties section.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            return versionNum;
        }

        /// <summary>
        /// Deserialize GraphQL configuration on each entity.
        /// </summary>
        public void DetermineGraphQLEntityNames()
        {
            foreach (Entity entity in Entities.Values)
            {
                if (!entity.TryProcessGraphQLNamingConfig())
                {
                    throw new NotSupportedException("The runtime does not support this GraphQL settings type for an entity.");
                }
            }
        }

        /// <summary>
        /// Mapping GraphQL singular type To each entity name.
        /// This is used for looking up top-level entity name with GraphQL type, GraphQL type is not matching any of the top level entity name.
        /// Use singular field to find the top level entity name, then do the look up from the entities dictionary
        /// </summary>
        public void MapGraphQLSingularTypeToEntityName(ILogger? logger)
        {
            foreach (KeyValuePair<string, Entity> item in Entities)
            {
                Entity entity = item.Value;
                string entityName = item.Key;

                if (entity.GraphQL != null
                    && entity.GraphQL is GraphQLEntitySettings)
                {
                    GraphQLEntitySettings? graphQL = entity.GraphQL as GraphQLEntitySettings;

                    if (graphQL is null || graphQL.Type is null
                        || (graphQL.Type is not SingularPlural && graphQL.Type is not string))
                    {
                        // Use entity name since GraphQL type unavailable
                        logger?.LogInformation($"GraphQL type for {entityName} is {entityName}");
                        continue;
                    }

                    string? graphQLType = (graphQL.Type is SingularPlural) ? ((SingularPlural)graphQL.Type).Singular : graphQL.Type.ToString();

                    if (graphQLType is not null)
                    {
                        GraphQLSingularTypeToEntityNameMap.TryAdd(graphQLType, entityName);
                        // We have the GraphQL type so we log that
                        logger?.LogInformation($"GraphQL type for {entityName} is {graphQLType}");
                    }
                }

                // Log every entity that is not disabled for GQL
                if (entity.GraphQL is null || entity.GraphQL is true)
                {
                    // Use entity name since GraphQL type unavailable
                    logger?.LogInformation($"GraphQL type for {entityName} is {entityName}");
                }
            }
        }

        /// <summary>
        /// Try to deserialize the given json string into its object form.
        /// </summary>
        /// <typeparam name="T">The object type.</typeparam>
        /// <param name="jsonString">Json string to be deserialized.</param>
        /// <param name="deserializedConfig">Deserialized json object upon success.</param>
        /// <returns>True on success, false otherwise.</returns>
        public static bool TryGetDeserializedJsonString<T>(
            string jsonString,
            out T? deserializedJsonString,
            ILogger logger)
        {
            try
            {
                deserializedJsonString = JsonSerializer.Deserialize<T>(jsonString, SerializerOptions);
                return true;
            }
            catch (JsonException ex)
            {
                // until this function is refactored to exist in RuntimeConfigProvider
                // we must use Console for logging.
                logger.LogError($"Deserialization of the json string failed.\n" +
                    $"Message:\n {ex.Message}\n" +
                    $"Stack Trace:\n {ex.StackTrace}");

                deserializedJsonString = default(T);
                return false;
            }
        }

        /// <summary>
        /// Try to deserialize the given json string into its object form.
        /// </summary>
        /// <param name="configJson">Json string to be deserialized.</param>
        /// <param name="deserializedRuntimeConfig">Deserialized json object upon success.</param>
        /// <returns>True on success, false otherwise.</returns>
        public static bool TryGetDeserializedRuntimeConfig(
            string configJson,
            [NotNullWhen(true)] out RuntimeConfig? deserializedRuntimeConfig,
            ILogger? logger)
        {
            try
            {
                deserializedRuntimeConfig = JsonSerializer.Deserialize<RuntimeConfig>(configJson, SerializerOptions);
                deserializedRuntimeConfig!.DetermineGlobalSettings(logger);
                deserializedRuntimeConfig!.DetermineGraphQLEntityNames();
                deserializedRuntimeConfig.DataSource.PopulateDbSpecificOptions();
                return true;
            }
            catch (Exception ex)
            {
                string errorMessage = $"Deserialization of the configuration file failed.\n" +
                        $"Message:\n {ex.Message}\n" +
                        $"Stack Trace:\n {ex.StackTrace}";

                if (logger is null)
                {
                    // logger can be null when called from CLI
                    Console.Error.WriteLine(errorMessage);
                }
                else
                {
                    logger.LogError(errorMessage);
                }

                deserializedRuntimeConfig = null;
                return false;
            }
        }

        [JsonIgnore]
        public RestGlobalSettings RestGlobalSettings { get; private set; } = new();

        [JsonIgnore]
        public GraphQLGlobalSettings GraphQLGlobalSettings { get; private set; } = new();

        [JsonIgnore]
        public HostGlobalSettings HostGlobalSettings { get; private set; } = new();

        [JsonIgnore]
        public Dictionary<string, string> GraphQLSingularTypeToEntityNameMap { get; private set; } = new();

        public bool IsEasyAuthAuthenticationProvider()
        {
            // by default, if there is no AuthenticationSection,
            // EasyAuth StaticWebApps is the authentication scheme.
            return AuthNConfig != null &&
                   AuthNConfig.IsEasyAuthAuthenticationProvider();
        }

        public bool IsAuthenticationSimulatorEnabled()
        {
            return AuthNConfig != null &&
                AuthNConfig!.IsAuthenticationSimulatorEnabled();
        }

        public bool IsJwtConfiguredIdentityProvider()
        {
            return AuthNConfig != null &&
                !AuthNConfig.IsEasyAuthAuthenticationProvider() &&
                !AuthNConfig.IsAuthenticationSimulatorEnabled();
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
