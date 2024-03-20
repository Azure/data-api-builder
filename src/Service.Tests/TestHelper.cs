// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Humanizer;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests
{
    static class TestHelper
    {
        public static void SetupDatabaseEnvironment(string database)
        {
            Environment.SetEnvironmentVariable(FileSystemRuntimeConfigLoader.RUNTIME_ENVIRONMENT_VAR_NAME, database);
        }

        public static void UnsetAllDABEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable(FileSystemRuntimeConfigLoader.RUNTIME_ENVIRONMENT_VAR_NAME, null);
            Environment.SetEnvironmentVariable(FileSystemRuntimeConfigLoader.ASP_NET_CORE_ENVIRONMENT_VAR_NAME, null);
            Environment.SetEnvironmentVariable(FileSystemRuntimeConfigLoader.RUNTIME_ENV_CONNECTION_STRING, null);
        }

        /// <summary>
        /// Given the testing environment, retrieve the config path.
        /// </summary>
        /// <param name="environment"></param>
        /// <returns></returns>
        public static FileSystemRuntimeConfigLoader GetRuntimeConfigLoader()
        {
            FileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader runtimeConfigLoader = new(fileSystem);
            return runtimeConfigLoader;
        }

        public static ILoggerFactory ProvisionLoggerFactory() =>
          LoggerFactory.Create(builder =>
          {
              builder.AddConsole();
          });

        /// <summary>
        /// Given the configuration path, generate the runtime configuration provider
        /// using a mock logger.
        /// </summary>
        /// <param name="loader"></param>
        /// <returns></returns>
        public static RuntimeConfigProvider GetRuntimeConfigProvider(FileSystemRuntimeConfigLoader loader)
        {
            RuntimeConfigProvider runtimeConfigProvider = new(loader);

            // Only set IsLateConfigured for MsSQL for now to do certificate validation.
            // For Pg/MySQL databases, set this after SSL connections are enabled for testing.
            if (runtimeConfigProvider.TryGetConfig(out RuntimeConfig runtimeConfig)
                && runtimeConfig.DataSource.DatabaseType is DatabaseType.MSSQL)
            {
                runtimeConfigProvider.IsLateConfigured = true;
            }

            return runtimeConfigProvider;
        }

        /// <summary>
        /// Temporary Helper function to ensure that in testing we have an entity
        /// that can have a custom schema. Ultimately this will be replaced with a JSON string
        /// in the tests that can be fully customized for testing purposes.
        /// </summary>
        /// <param name="config">RuntimeConfig object</param>
        /// <param name="entityKey">The key with which the entity is to be added.</param>
        /// <param name="entityName">The source name of the entity.</param>
        public static RuntimeConfig AddMissingEntitiesToConfig(RuntimeConfig config, string entityKey, string entityName, string[] keyfields = null)
        {
            Entity entity = new(
                Source: new(entityName, EntitySourceType.Table, null, keyfields),
                GraphQL: new(entityKey, entityKey.Pluralize()),
                Rest: new(Enabled: true),
                Permissions: new[]
                {
                    new EntityPermission("anonymous", new EntityAction[] {
                        new(EntityActionOperation.Create, null, new()),
                        new(EntityActionOperation.Read, null, new()),
                        new(EntityActionOperation.Delete, null, new()),
                        new(EntityActionOperation.Update, null, new())
                    }),
                    new EntityPermission("authenticated", new EntityAction[] {
                        new(EntityActionOperation.Create, null, new()),
                        new(EntityActionOperation.Read, null, new()),
                        new(EntityActionOperation.Delete, null, new()),
                        new(EntityActionOperation.Update, null, new())
                    })
                },
                Mappings: null,
                Relationships: null);

            Dictionary<string, Entity> entities = new(config.Entities)
            {
                { entityKey, entity }
            };

            return config with { Entities = new(entities) };
        }

        /// <summary>
        /// Schema property of the config json. This is used for constructing the required config json strings
        /// for unit tests
        /// </summary>
        public const string SCHEMA_PROPERTY = @"
          ""$schema"": """ + FileSystemRuntimeConfigLoader.SCHEMA + @"""";

        /// <summary>
        /// A sample connection string for unit tests
        /// </summary>
        public const string SAMPLE_TEST_CONN_STRING = "Data Source=<>;Initial Catalog=<>;User ID=<>;Password=<>;";

        /// <summary>
        /// Data source property of the config json. This is used for constructing the required config json strings
        /// for unit tests
        /// </summary>
        public const string SAMPLE_SCHEMA_DATA_SOURCE = SCHEMA_PROPERTY + "," + @"
            ""data-source"": {
              ""database-type"": ""mssql"",
              ""connection-string"": """ + SAMPLE_TEST_CONN_STRING + @"""
            }
        ";

        /// <summary>
        /// A minimal valid config json without any entities. This config string is used in unit tests.
        /// </summary>
        public const string INITIAL_CONFIG =
          "{" +
            SAMPLE_SCHEMA_DATA_SOURCE + "," +
            @"
            ""runtime"": {
              ""rest"": {
                ""path"": ""/api"",
                ""enabled"": true
              },
              ""graphql"": {
                ""path"": ""/graphql"",
                ""enabled"": true,
                ""allow-introspection"": true
              },
              ""host"": {
                ""mode"": ""development"",
                ""cors"": {
                  ""origins"": [],
                  ""allow-credentials"": false
                },
                ""authentication"": {
                  ""provider"": ""StaticWebApps""
                }
              }
            },
            ""entities"": {}" +
          "}";

        /// <summary>
        /// A minimal valid config json without any entities. This config string is used in tests.
        /// Note: The test ConfigurationTests.ValidateStrictModeAsDefaultForRestRequestBody depends on BASE_CONFIG
        /// omitting the request-body-strict property.
        /// If there is a need to include this property here, the test needs to be adjusted accordingly.
        /// </summary>
        public const string BASE_CONFIG =
          "{" +
            SAMPLE_SCHEMA_DATA_SOURCE + "," +
            @"
            ""runtime"": {
              ""rest"": {
                ""path"": ""/api""
              },
              ""graphql"": {
                ""path"": ""/graphql"",
                ""allow-introspection"": true
              },
              ""host"": {
                ""mode"": ""development"",
                ""cors"": {
                  ""origins"": [""http://localhost:5000""],
                  ""allow-credentials"": false
                },
                ""authentication"": {
                  ""provider"": ""StaticWebApps""
                }
              }
            },
            ""entities"": {}" +
          "}";

        /// <summary>
        /// An empty entities section of the config file. This is used in constructing config json strings utilized for testing.
        /// </summary>
        public const string EMPTY_ENTITIES_CONFIG_JSON =
            @"
                ""entities"": {}
            ";

        /// <summary>
        /// A json string with Runtime Rest and GraphQL options. This is used in constructing config json strings utilized for testing. 
        /// </summary>
        public const string RUNTIME_REST_GRAPHQL_OPTIONS_CONFIG_JSON =
             "{" +
             SAMPLE_SCHEMA_DATA_SOURCE + "," +
             @"
            ""runtime"": {
              ""rest"": {
                ""path"": ""/api""
              },
              ""graphql"": {
                ""path"": ""/graphql"",
                ""allow-introspection"": true,";

        /// <summary>
        /// A json string with host and empty entity options. This is used in constructing config json strings utilized for testing.
        /// </summary>
        public const string HOST_AND_ENTITY_OPTIONS_CONFIG_JSON =
            @"
            ""host"": {
                ""mode"": ""development"",
                ""cors"": {
                  ""origins"": [""http://localhost:5000""],
                  ""allow-credentials"": false
                },
                ""authentication"": {
                  ""provider"": ""StaticWebApps""
                }
              }
            }" + "," +
            EMPTY_ENTITIES_CONFIG_JSON +
            "}";

        /// <summary>
        /// A minimal valid config json with multiple mutations section as null.
        /// </summary>
        public const string BASE_CONFIG_NULL_MULTIPLE_MUTATIONS_FIELD =
            RUNTIME_REST_GRAPHQL_OPTIONS_CONFIG_JSON +
              @"
                ""multiple-mutations"": null   
              }," +
            HOST_AND_ENTITY_OPTIONS_CONFIG_JSON;

        /// <summary>
        /// A minimal valid config json with an empty multiple mutations section.
        /// </summary>
        public const string BASE_CONFIG_EMPTY_MULTIPLE_MUTATIONS_FIELD =

            RUNTIME_REST_GRAPHQL_OPTIONS_CONFIG_JSON +
              @"
                ""multiple-mutations"": {}
              }," +
            HOST_AND_ENTITY_OPTIONS_CONFIG_JSON;

        /// <summary>
        /// A minimal valid config json with the create field within multiple mutation as null.
        /// </summary>
        public const string BASE_CONFIG_NULL_MULTIPLE_CREATE_FIELD =

            RUNTIME_REST_GRAPHQL_OPTIONS_CONFIG_JSON +
              @"
                ""multiple-mutations"": {
                      ""create"": null
                 }
              }," +
            HOST_AND_ENTITY_OPTIONS_CONFIG_JSON;

        /// <summary>
        /// A minimal valid config json with an empty create field within multiple mutation.
        /// </summary>
        public const string BASE_CONFIG_EMPTY_MULTIPLE_CREATE_FIELD =

            RUNTIME_REST_GRAPHQL_OPTIONS_CONFIG_JSON +
            @"
                ""multiple-mutations"": {
                      ""create"": {}
                }
            }," +
            HOST_AND_ENTITY_OPTIONS_CONFIG_JSON;

        public static RuntimeConfigProvider GenerateInMemoryRuntimeConfigProvider(RuntimeConfig runtimeConfig)
        {
            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, runtimeConfig.ToJson());
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider runtimeConfigProvider = new(loader);
            return runtimeConfigProvider;
        }

        /// <summary>
        /// Utility method that reads the config file for a given database type and constructs a
        /// new config file with custom changes as specified in the method parameters.
        /// </summary>
        /// <param name="configFileName">Name of the new config file to be constructed</param>
        /// <param name="hostModeType">HostMode for the engine</param>
        /// <param name="databaseType">Database type</param>
        /// <param name="runtimeBaseRoute">Base route for API requests.</param>
        public static void ConstructNewConfigWithSpecifiedHostMode(string configFileName, HostMode hostModeType, string databaseType, string runtimeBaseRoute = "/")
        {
            SetupDatabaseEnvironment(databaseType);
            RuntimeConfigProvider configProvider = GetRuntimeConfigProvider(GetRuntimeConfigLoader());
            RuntimeConfig config = configProvider.GetConfig();

            RuntimeConfig configWithCustomHostMode =
                config
                with
                {
                    Runtime = config.Runtime
                with
                    {
                        Host = config.Runtime?.Host
                with
                        { Mode = hostModeType },
                        BaseRoute = runtimeBaseRoute
                    }
                };
            File.WriteAllText(configFileName, configWithCustomHostMode.ToJson());

        }

        /// <summary>
        /// Adds the entity properties to the configuration and returns the updated configuration json as a string.
        /// </summary>
        /// <param name="configuration">Configuration Json.</param>
        /// <param name="entityProperties">Entity properties to be added to the configuration.</param>
        public static string AddPropertiesToJson(string configuration, string entityProperties)
        {
            JObject configurationJson = JObject.Parse(configuration);
            JObject entityPropertiesJson = JObject.Parse(entityProperties);

            configurationJson.Merge(entityPropertiesJson, new JsonMergeSettings
            {
                MergeArrayHandling = MergeArrayHandling.Union
            });
            return configurationJson.ToString();
        }
    }
}
