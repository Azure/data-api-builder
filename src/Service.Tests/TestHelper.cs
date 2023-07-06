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

namespace Azure.DataApiBuilder.Service.Tests
{
    static class TestHelper
    {
        public static void SetupDatabaseEnvironment(string database)
        {
            Environment.SetEnvironmentVariable(RuntimeConfigLoader.RUNTIME_ENVIRONMENT_VAR_NAME, database);
        }

        public static void UnsetAllDABEnvironmentVariables()
        {
            Environment.SetEnvironmentVariable(RuntimeConfigLoader.RUNTIME_ENVIRONMENT_VAR_NAME, null);
            Environment.SetEnvironmentVariable(RuntimeConfigLoader.ASP_NET_CORE_ENVIRONMENT_VAR_NAME, null);
            Environment.SetEnvironmentVariable(RuntimeConfigLoader.RUNTIME_ENV_CONNECTION_STRING, null);
        }

        /// <summary>
        /// Given the testing environment, retrieve the config path.
        /// </summary>
        /// <param name="environment"></param>
        /// <returns></returns>
        public static RuntimeConfigLoader GetRuntimeConfigLoader()
        {
            FileSystem fileSystem = new();
            RuntimeConfigLoader runtimeConfigLoader = new(fileSystem);
            return runtimeConfigLoader;
        }

        /// <summary>
        /// Given the configuration path, generate the runtime configuration provider
        /// using a mock logger.
        /// </summary>
        /// <param name="loader"></param>
        /// <returns></returns>
        public static RuntimeConfigProvider GetRuntimeConfigProvider(RuntimeConfigLoader loader)
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
        public static RuntimeConfig AddMissingEntitiesToConfig(RuntimeConfig config, string entityKey, string entityName)
        {
            Entity entity = new(
                Source: new(entityName, EntitySourceType.Table, null, null),
                GraphQL: new(entityKey, entityKey.Pluralize()),
                Rest: new(Array.Empty<SupportedHttpVerb>()),
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
          ""$schema"": """ + RuntimeConfigLoader.SCHEMA + @"""";

        /// <summary>
        /// Data source property of the config json. This is used for constructing the required config json strings
        /// for unit tests
        /// </summary>
        public const string SAMPLE_SCHEMA_DATA_SOURCE = SCHEMA_PROPERTY + "," + @"
            ""data-source"": {
              ""database-type"": ""mssql"",
              ""connection-string"": ""testconnectionstring""
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
                ""path"": ""/api""
              },
              ""graphql"": {
                ""path"": ""/graphql"",
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

        public static RuntimeConfigProvider GenerateInMemoryRuntimeConfigProvider(RuntimeConfig runtimeConfig)
        {
            MockFileSystem fileSystem = new();
            fileSystem.AddFile(RuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, runtimeConfig.ToJson());
            RuntimeConfigLoader loader = new(fileSystem);
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
        public static void ConstructNewConfigWithSpecifiedHostMode(string configFileName, HostMode hostModeType, string databaseType, string runtimeBaseRoute = "")
        {
            TestHelper.SetupDatabaseEnvironment(databaseType);
            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(TestHelper.GetRuntimeConfigLoader());
            RuntimeConfig config = configProvider.GetConfig();

            RuntimeConfig configWithCustomHostMode =
                config
                with
                {
                    Runtime = config.Runtime
                with
                    {
                        Host = config.Runtime.Host
                with
                        { Mode = hostModeType },
                        BaseRoute = runtimeBaseRoute
                    }
                };
            File.WriteAllText(configFileName, configWithCustomHostMode.ToJson());

        }
    }
}
