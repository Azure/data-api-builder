using System;
using System.Collections.Generic;
using System.IO;
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
            configPath.SetRuntimeConfigValue();
            return Mock.Of<IOptionsMonitor<RuntimeConfigPath>>(_ => _.CurrentValue == configPath);
        }

        public static IOptionsMonitor<RuntimeConfigPath> ConfigPath
        {
            get { return _runtimeConfigPath.Value; }
        }

        public static object GetItem(string id)
        {
            return new
            {
                id = id,
                name = "test name",
                myProp = "a value",
                age = 4,
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
