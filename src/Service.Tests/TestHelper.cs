using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests
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
                        $" \"{Operation.Create.ToString().ToLower()}\"," +
                        $" \"{Operation.Read.ToString().ToLower()}\"," +
                        $" \"{Operation.Delete.ToString().ToLower()}\"," +
                        $" \"{Operation.Update.ToString().ToLower()}\" ]" +
                      @"},
                      {
                        ""role"": ""authenticated"",
                        ""actions"": [" +
                        $" \"{Operation.Create.ToString().ToLower()}\"," +
                        $" \"{Operation.Read.ToString().ToLower()}\"," +
                        $" \"{Operation.Delete.ToString().ToLower()}\"," +
                        $" \"{Operation.Update.ToString().ToLower()}\" ]" +
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
    }
}
