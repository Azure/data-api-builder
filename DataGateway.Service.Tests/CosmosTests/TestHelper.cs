using System;
using System.IO;
using Azure.DataGateway.Service.Configurations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Moq;

namespace Azure.DataGateway.Service.Tests.CosmosTests
{
    class TestHelper
    {
        public static readonly string DB_NAME = "graphqlTestDb";
        private static Lazy<IOptions<RuntimeConfig>> _runtimeConfig = new(() => TestHelper.LoadConfig());

        private static IOptions<RuntimeConfig> LoadConfig()
        {
            RuntimeConfig runtimeConfig = new();
            IConfigurationRoot config = new ConfigurationBuilder()
                .SetBasePath(Directory.GetCurrentDirectory())
                .AddJsonFile($"appsettings.Cosmos.json")
                .Build();

            config.Bind(nameof(RuntimeConfig), runtimeConfig);

            return Options.Create(runtimeConfig);
        }

        public static IOptions<RuntimeConfig> RuntimeConfig
        {
            get { return _runtimeConfig.Value; }
        }

        public static IOptionsMonitor<RuntimeConfig> RuntimeConfigMonitor
        {
            get
            {
                return Mock.Of<IOptionsMonitor<RuntimeConfig>>(_ => _.CurrentValue == RuntimeConfig.Value);
            }
        }

        public static object GetItem(string id)
        {
            return new
            {
                id = id,
                myProp = "a value",
                myIntProp = 4,
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
                }
            };
        }
    }
}
