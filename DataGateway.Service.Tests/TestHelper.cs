using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests
{
    public class TestHelper
    {
        /// <summary>
        /// Given the testing environment, retrieve the config path.
        /// </summary>
        /// <param name="environment"></param>
        /// <returns></returns>
        public static RuntimeConfigPath GetRuntimeConfigPath(string environment)
        {
            string configFileName = RuntimeConfigPath.GetFileNameForEnvironment(environment);

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
            RuntimeConfig.TryGetDeserializedConfig(configJson, out RuntimeConfig runtimeConfig);
            mockRuntimeConfigProvider.Setup(x => x.GetRuntimeConfiguration()).Returns(runtimeConfig);
            return mockRuntimeConfigProvider.Object;
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
        /// that can have a custom schema. We create a new entity of 'Magazine' with
        /// a schema of 'foo' for table 'magazines', and then add this entity to our
        /// runtime configuration. Because MySql will not have a schema we need a way
        /// to customize this entity, which this helper function provides. Ultimately
        /// this will be replaced with a JSON string in the tests that can be fully
        /// customized for testing purposes.
        /// </summary>
        /// <param name="configPath"></param>
        public static void AddMissingEntitiesToConfig(RuntimeConfig config)
        {
            string magazineSource = config.DatabaseType is DatabaseType.mysql ? "\"magazines\"" : "\"foo.magazines\"";
            string magazineEntityJsonString =
              @"{ 
                    ""source"":  " + magazineSource + @",
                    ""graphql"": true,
                    ""permissions"": [
                      {
                        ""role"": ""anonymous"",
                        ""actions"": [ ""read"" ]
                      },
                      {
                        ""role"": ""authenticated"",
                        ""actions"": [ ""create"", ""update"", ""read"", ""delete"" ]
                      }
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

            Entity magazineEntity = JsonSerializer.Deserialize<Entity>(magazineEntityJsonString, options);
            config.Entities.Add("Magazine", magazineEntity);
        }
    }
}
