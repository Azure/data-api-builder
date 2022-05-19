using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.DataGateway.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;

namespace Azure.DataGateway.Service.Tests.CosmosTests
{
    class TestHelper
    {
        public static readonly string DB_NAME = "graphqlTestDb";
        private static Lazy<IOptionsMonitor<RuntimeConfigPath>>
            _runtimeConfigPath = new(() => LoadConfig());

        private static IOptionsMonitor<RuntimeConfigPath> LoadConfig()
        {
            Dictionary<string, string> configFileNameMap = new()
            {
                {
                    nameof(RuntimeConfigPath.ConfigFileName),
                    RuntimeConfigPath.GetFileNameForEnvironment(TestCategory.COSMOS)
                }
            };
            IConfigurationRoot config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddInMemoryCollection(configFileNameMap)
                .Build();

            RuntimeConfigPath configPath = config.Get<RuntimeConfigPath>();
            RuntimeConfig? runtimeConfig = configPath.LoadRuntimeConfigValue();
            AddMissingEntitiesToConfig(runtimeConfig);
            return Mock.Of<IOptionsMonitor<RuntimeConfigPath>>(_ => _.CurrentValue == configPath);
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
        private static void AddMissingEntitiesToConfig(RuntimeConfig config)
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
                        ""actions"": [ ""create"", ""read"", ""delete"" ]
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

        public static IOptionsMonitor<RuntimeConfigPath> ConfigPath
        {
            get { return _runtimeConfigPath.Value; }
        }

        // TODO: This doesn't seem great, we'll load the file every time? 
        public static RuntimeConfig Config { get; } = _runtimeConfigPath.Value.CurrentValue.LoadRuntimeConfigValue();

        public static object GetItem(string id, string name = null, int numericVal = 4)
        {
            return new
            {
                id = id,
                name = string.IsNullOrEmpty(name) ? "test name" : name,
                dimension = "space",
                age = numericVal,
                myBooleanProp = true,
                anotherPojo = new
                {
                    anotherProp = "myname",
                    anotherIntProp = 55,
                    person = new
                    {
                        firstName = "A Person",
                        lastName = "the last name",
                        zipCode = 784298
                    }
                },
                character = new
                {
                    id = id,
                    name = "planet character",
                    type = "Mars",
                    homePlanet = 1,
                    primaryFunction = "test function"
                }
            };
        }
    }
}
