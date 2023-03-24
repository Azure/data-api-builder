// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests
{
    public class TestHelper
    {

        public const string REQUESTBODY = @"
                    {
                        ""title"": ""New book"",
                        ""publisher_id"": ""one""
                    }
                ";

        /// <summary>
        /// Given the testing environment, retrieve the config path.
        /// </summary>
        /// <param name="environment"></param>
        /// <returns></returns>
        public static RuntimeConfigPath GetRuntimeConfigPath(string environment)
        {
            string configFileName = RuntimeConfigPath.GetFileNameForEnvironment(
                                                        hostingEnvironmentName: environment,
                                                        considerOverrides: true);

            Dictionary<string, string> configFileNameMap = new()
            {
                {
                    nameof(RuntimeConfigPath.ConfigFileName),
                    configFileName
                }
            };

            IConfigurationRoot config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddInMemoryCollection(configFileNameMap)
                .Build();

            return config.Get<RuntimeConfigPath>();
        }

        /// <summary>
        /// Given the configuration path, generate the runtime configuration provider
        /// using a mock logger.
        /// </summary>
        /// <param name="configPath"></param>
        /// <returns></returns>
        public static RuntimeConfigProvider GetRuntimeConfigProvider(
            RuntimeConfigPath configPath)
        {
            Mock<ILogger<RuntimeConfigProvider>> configProviderLogger = new();
            RuntimeConfigProvider runtimeConfigProvider
                = new(configPath,
                      configProviderLogger.Object);

            // Only set IsLateConfigured for MsSQL for now to do certificate validation.
            // For Pg/MySQL databases, set this after SSL connections are enabled for testing.
            if (runtimeConfigProvider.TryGetRuntimeConfiguration(out RuntimeConfig runtimeConfig)
                && runtimeConfig.DatabaseType is DatabaseType.mssql)
            {
                runtimeConfigProvider.IsLateConfigured = true;
            }

            return runtimeConfigProvider;
        }

        /// <summary>
        /// Gets the runtime config provider such that the given config is set as the
        /// desired RuntimeConfiguration.
        /// </summary>
        /// <param name="config"></param>
        /// <returns></returns>
        public static RuntimeConfigProvider GetRuntimeConfigProvider(
            RuntimeConfig config)
        {
            Mock<ILogger<RuntimeConfigProvider>> configProviderLogger = new();
            RuntimeConfigProvider runtimeConfigProvider
                = new(config,
                      configProviderLogger.Object);

            // Only set IsLateConfigured for MsSQL for now to do certificate validation.
            // For Pg/MySQL databases, set this after SSL connections are enabled for testing.
            if (config is not null && config.DatabaseType is DatabaseType.mssql)
            {
                runtimeConfigProvider.IsLateConfigured = true;
            }

            return runtimeConfigProvider;
        }

        /// <summary>
        /// Given the configuration path, generate a mock runtime configuration provider
        /// using a mock logger.
        /// The mock provider returns a mock RestPath set to the input param path.
        /// </summary>
        /// <param name="configPath"></param>
        /// <param name="path"></param>
        /// <returns></returns>
        public static RuntimeConfigProvider GetMockRuntimeConfigProvider(
            RuntimeConfigPath configPath,
            string path)
        {
            Mock<ILogger<RuntimeConfigProvider>> configProviderLogger = new();
            Mock<RuntimeConfigProvider> mockRuntimeConfigProvider
                = new(configPath,
                      configProviderLogger.Object);
            mockRuntimeConfigProvider.Setup(x => x.RestPath).Returns(path);
            mockRuntimeConfigProvider.Setup(x => x.TryLoadRuntimeConfigValue()).Returns(true);
            string configJson = RuntimeConfigProvider.GetRuntimeConfigJsonString(configPath.ConfigFileName);
            RuntimeConfig.TryGetDeserializedRuntimeConfig(configJson, out RuntimeConfig runtimeConfig, configProviderLogger.Object);
            mockRuntimeConfigProvider.Setup(x => x.GetRuntimeConfiguration()).Returns(runtimeConfig);
            mockRuntimeConfigProvider.Setup(x => x.IsLateConfigured).Returns(true);
            return mockRuntimeConfigProvider.Object;
        }

        /// <summary>
        /// Given the environment, return the runtime config provider.
        /// </summary>
        /// <param name="environment">The environment for which the test is being run. (e.g. TestCategory.COSMOS)</param>
        /// <returns></returns>
        public static RuntimeConfigProvider GetRuntimeConfigProvider(string environment)
        {
            RuntimeConfigPath configPath = GetRuntimeConfigPath(environment);
            return GetRuntimeConfigProvider(configPath);
        }

        /// <summary>
        /// Given the configurationProvider, try to load and get the runtime config object.
        /// </summary>
        /// <param name="configProvider"></param>
        /// <returns></returns>
        public static RuntimeConfig GetRuntimeConfig(RuntimeConfigProvider configProvider)
        {
            if (!configProvider.TryLoadRuntimeConfigValue())
            {
                Assert.Fail($"Failed to load runtime configuration file in test setup");
            }

            return configProvider.GetRuntimeConfiguration();
        }

        /// <summary>
        /// Temporary Helper function to ensure that in testing we have an entity
        /// that can have a custom schema. Ultimately this will be replaced with a JSON string
        /// in the tests that can be fully customized for testing purposes.
        /// </summary>
        /// <param name="config">Runtimeconfig object</param>
        /// <param name="entityKey">The key with which the entity is to be added.</param>
        /// <param name="entityName">The source name of the entity.</param>
        public static void AddMissingEntitiesToConfig(RuntimeConfig config, string entityKey, string entityName)
        {
            string source = "\"" + entityName + "\"";
            string entityJsonString =
              @"{
                    ""source"":  " + source + @",
                    ""graphql"": true,
                    ""permissions"": [
                      {
                        ""role"": ""anonymous"",
                        ""actions"": [" +
                        $" \"{Config.Operation.Create.ToString().ToLower()}\"," +
                        $" \"{Config.Operation.Read.ToString().ToLower()}\"," +
                        $" \"{Config.Operation.Delete.ToString().ToLower()}\"," +
                        $" \"{Config.Operation.Update.ToString().ToLower()}\" ]" +
                      @"},
                      {
                        ""role"": ""authenticated"",
                        ""actions"": [" +
                        $" \"{Config.Operation.Create.ToString().ToLower()}\"," +
                        $" \"{Config.Operation.Read.ToString().ToLower()}\"," +
                        $" \"{Config.Operation.Delete.ToString().ToLower()}\"," +
                        $" \"{Config.Operation.Update.ToString().ToLower()}\" ]" +
                      @"}
                    ]
                }";

            JsonSerializerOptions options = new()
            {
                PropertyNameCaseInsensitive = true,
                Converters =
                {
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
                }
            };

            Entity entity = JsonSerializer.Deserialize<Entity>(entityJsonString, options);
            config.Entities.Add(entityKey, entity);
        }

        /// <summary>
        /// Schema property of the config json. This is used for constructing the required config json strings
        /// for unit tests
        /// </summary>
        public const string SCHEMA_PROPERTY = @"
          ""$schema"": """ + Azure.DataApiBuilder.Config.RuntimeConfig.SCHEMA + @"""";

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

        public static void ChangeHostTypeInConfigFile(HostModeType hostModeType, string databaseType)
        {
            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(databaseType);
            RuntimeConfig config = configProvider.GetRuntimeConfiguration();
            HostGlobalSettings customHostGlobalSettings = config.HostGlobalSettings with { Mode = hostModeType };
            JsonElement serializedCustomHostGlobalSettings =
                JsonSerializer.SerializeToElement(customHostGlobalSettings, RuntimeConfig.SerializerOptions);
            Dictionary<GlobalSettingsType, object> customRuntimeSettings = new(config.RuntimeSettings);
            customRuntimeSettings.Remove(GlobalSettingsType.Host);
            customRuntimeSettings.Add(GlobalSettingsType.Host, serializedCustomHostGlobalSettings);
            RuntimeConfig configWithCustomHostMode =
                config with { RuntimeSettings = customRuntimeSettings };
            File.WriteAllText(
                "custom-config.json",
                JsonSerializer.Serialize(configWithCustomHostMode, RuntimeConfig.SerializerOptions));

        }

        public static HttpMethod GetHttpMethod(string httpMethod)
        {
            switch (httpMethod)
            {
                case "GET": return HttpMethod.Get;
                case "POST": return HttpMethod.Post;
                case "PUT": return HttpMethod.Put;
                case "PATCH": return HttpMethod.Patch;
                case "DELETE": return HttpMethod.Delete;
                default:
                    throw new DataApiBuilderException(
                        message: "HTTP Request Type not supported.",
                        statusCode: HttpStatusCode.BadRequest,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.NotSupported);
            }
        }
    }
}
