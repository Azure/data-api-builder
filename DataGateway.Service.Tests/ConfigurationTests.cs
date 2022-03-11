using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Net.Http.Json;
using System.IO;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Configurations;
using Microsoft.Data.SqlClient;
using Npgsql;
using MySqlConnector;

namespace Azure.DataGateway.Service.Tests
{
    [TestClass]
    public class ConfigurationTests
    {
        private const string ASP_NET_CORE_ENVIRONMENT_VAR_NAME = "ASPNETCORE_ENVIRONMENT";
        private string _CosmosResolverConfig = File.ReadAllText("cosmos-config.json");
        private string _GraphQLSchema = File.ReadAllText("schema.gql");


        [TestMethod("Validates that querying for a config that's not set returns a 404.")]
        public async Task TestNoConfigReturnsServiceUnavailable()
        {
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            HttpResponseMessage result = await httpClient.GetAsync("/graphql");
            Assert.AreEqual(System.Net.HttpStatusCode.ServiceUnavailable, result.StatusCode);
        }

        [TestMethod("Validates that querying for a config that's not set returns a 404.")]
        public async Task TestGettingNonSetConfigurationReturns404()
        {
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            HttpResponseMessage result = await httpClient.GetAsync("/configuration?key=test");
            Assert.AreEqual(System.Net.HttpStatusCode.NotFound, result.StatusCode);
        }

        [TestMethod("Validates that configurations are set and can be retrieved.")]
        public async Task TestSettingConfigurations()
        {
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            Dictionary<string, string> config = new()
            {
                { "DataGatewayConfig:DatabaseType", "Cosmos" },
                { "DataGatewayConfig:ResolverConfigFile", "cosmos-config.json" },
                { "DataGatewayConfig:DatabaseConnection:ConnectionString", "Cosmos" }
            };
            HttpResponseMessage postResult = await httpClient.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(System.Net.HttpStatusCode.OK, postResult.StatusCode);

            foreach (KeyValuePair<string, string> setting in config)
            {
                HttpResponseMessage result = await httpClient.GetAsync($"/configuration?key={setting.Key}");
                Assert.AreEqual(System.Net.HttpStatusCode.OK, result.StatusCode);

                string text = await result.Content.ReadAsStringAsync();
                Assert.AreEqual(setting.Value, text);
            }
        }

        [TestMethod("Validates that local cosmos settings can be loaded and the correct classes are in the service provider.")]
        public void TestLoadingLocalCosmosSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, "Cosmos");
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            ValidateCosmosDbSetup(server);
        }

        [TestMethod("Validates that local MsSql settings can be loaded and the correct classes are in the service provider.")]
        public void TestLoadingLocalMsSqlSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, "MsSql");
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            object queryEngine = server.Services.GetService(typeof(IQueryEngine));
            Assert.IsInstanceOfType(queryEngine, typeof(SqlQueryEngine));

            object mutationEngine = server.Services.GetService(typeof(IMutationEngine));
            Assert.IsInstanceOfType(mutationEngine, typeof(SqlMutationEngine));

            object configValidator = server.Services.GetService(typeof(IConfigValidator));
            Assert.IsInstanceOfType(configValidator, typeof(SqlConfigValidator));

            object queryBuilder = server.Services.GetService(typeof(IQueryBuilder));
            Assert.IsInstanceOfType(queryBuilder, typeof(MsSqlQueryBuilder));

            object queryExecutor = server.Services.GetService(typeof(IQueryExecutor));
            Assert.IsInstanceOfType(queryExecutor, typeof(QueryExecutor<SqlConnection>));
        }

        [TestMethod("Validates that local PostgreSql settings can be loaded and the correct classes are in the service provider.")]
        public void TestLoadingLocalPostgresSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, "PostgreSql");
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            object queryEngine = server.Services.GetService(typeof(IQueryEngine));
            Assert.IsInstanceOfType(queryEngine, typeof(SqlQueryEngine));

            object mutationEngine = server.Services.GetService(typeof(IMutationEngine));
            Assert.IsInstanceOfType(mutationEngine, typeof(SqlMutationEngine));

            object configValidator = server.Services.GetService(typeof(IConfigValidator));
            Assert.IsInstanceOfType(configValidator, typeof(SqlConfigValidator));

            object queryBuilder = server.Services.GetService(typeof(IQueryBuilder));
            Assert.IsInstanceOfType(queryBuilder, typeof(PostgresQueryBuilder));

            object queryExecutor = server.Services.GetService(typeof(IQueryExecutor));
            Assert.IsInstanceOfType(queryExecutor, typeof(QueryExecutor<NpgsqlConnection>));
        }

        [TestMethod("Validates that local MySql settings can be loaded and the correct classes are in the service provider.")]
        public void TestLoadingLocalMySqlSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, "MySql");
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            object queryEngine = server.Services.GetService(typeof(IQueryEngine));
            Assert.IsInstanceOfType(queryEngine, typeof(SqlQueryEngine));

            object mutationEngine = server.Services.GetService(typeof(IMutationEngine));
            Assert.IsInstanceOfType(mutationEngine, typeof(SqlMutationEngine));

            object configValidator = server.Services.GetService(typeof(IConfigValidator));
            Assert.IsInstanceOfType(configValidator, typeof(SqlConfigValidator));

            object queryBuilder = server.Services.GetService(typeof(IQueryBuilder));
            Assert.IsInstanceOfType(queryBuilder, typeof(MySqlQueryBuilder));

            object queryExecutor = server.Services.GetService(typeof(IQueryExecutor));
            Assert.IsInstanceOfType(queryExecutor, typeof(QueryExecutor<MySqlConnection>));
        }

        [TestMethod("Validates that trying to override configs that are already set fail.")]
        public async Task TestOverridingLocalSettingsFails()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, "Cosmos");
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();
            Dictionary<string, string> config = new()
            {
                { "Test", "Value" },
                { "DataGatewayConfig:DatabaseType", "MsSql" }
            };

            HttpResponseMessage postResult = await client.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(System.Net.HttpStatusCode.Conflict, postResult.StatusCode);
            Assert.AreEqual("DataGatewayConfig:DatabaseType:MsSql", await postResult.Content.ReadAsStringAsync());
        }

        [TestMethod("Validates that setting the configuration at runtime will instantiate the proper classes.")]
        public async Task TestSettingConfigurationCreatesCorrectClasses()
        {
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();
            Dictionary<string, string> config = new()
            {
                { "DataGatewayConfig:DatabaseType", "Cosmos" },
                { "DataGatewayConfig:ResolverConfigFile", "cosmos-config.json" },
                { "DataGatewayConfig:DatabaseConnection:ConnectionString", "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==" }
            };

            HttpResponseMessage postResult = await client.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(System.Net.HttpStatusCode.OK, postResult.StatusCode);

            ValidateCosmosDbSetup(server);
        }

        [TestMethod("Validates setting the resolver config and graphql schema.")]
        public async Task TestSettingResolverConfigAndSchema()
        {
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();
            Dictionary<string, string> config = new()
            {
                { "DataGatewayConfig:DatabaseType", "Cosmos" },
                { "DataGatewayConfig:ResolverConfig", _CosmosResolverConfig },
                { "DataGatewayConfig:GraphQLSchema", _GraphQLSchema },
                { "DataGatewayConfig:DatabaseConnection:ConnectionString", "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==" }
            };

            HttpResponseMessage postResult = await client.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(System.Net.HttpStatusCode.OK, postResult.StatusCode);
        }

        [TestMethod("Validates that setting the resolver config without setting the schema fails.")]
        public async Task TestSettingResolverConfigAndNotSchemaFails()
        {
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();
            Dictionary<string, string> config = new()
            {
                { "DataGatewayConfig:DatabaseType", "Cosmos" },
                { "DataGatewayConfig:ResolverConfig", _CosmosResolverConfig },
                { "DataGatewayConfig:DatabaseConnection:ConnectionString", "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==" }
            };

            await VerifyThrowsException<NotSupportedException>(async () =>
            {
                HttpResponseMessage postResult = await client.PostAsync("/configuration", JsonContent.Create(config));
                Assert.AreEqual(System.Net.HttpStatusCode.OK, postResult.StatusCode);
            });
        }

        [TestMethod("Validates that setting both the resolver config and the config file fails.")]
        public async Task TestSettingResolverConfigAndPathFails()
        {
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();
            Dictionary<string, string> config = new()
            {
                { "DataGatewayConfig:DatabaseType", "Cosmos" },
                { "DataGatewayConfig:ResolverConfig", _CosmosResolverConfig },
                { "DataGatewayConfig:ResolverConfigFile", "cosmos-config.json" },
                { "DataGatewayConfig:DatabaseConnection:ConnectionString", "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==" }
            };

            await VerifyThrowsException<NotSupportedException>(async () =>
            {
                HttpResponseMessage postResult = await client.PostAsync("/configuration", JsonContent.Create(config));
                Assert.AreEqual(System.Net.HttpStatusCode.OK, postResult.StatusCode);
            });
        }

        [TestCleanup]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, "");
        }

        private static void ValidateCosmosDbSetup(TestServer server)
        {
            object queryEngine = server.Services.GetService(typeof(IQueryEngine));
            Assert.IsInstanceOfType(queryEngine, typeof(CosmosQueryEngine));

            object mutationEngine = server.Services.GetService(typeof(IMutationEngine));
            Assert.IsInstanceOfType(mutationEngine, typeof(CosmosMutationEngine));

            object configValidator = server.Services.GetService(typeof(IConfigValidator));
            Assert.IsInstanceOfType(configValidator, typeof(CosmosConfigValidator));

            CosmosClientProvider cosmosClientProvider = server.Services.GetService(typeof(CosmosClientProvider)) as CosmosClientProvider;
            Assert.IsNotNull(cosmosClientProvider);
            Assert.IsNotNull(cosmosClientProvider.Client);
        }

        /// <summary>
        /// Verifies that an exception of type T is thrown. Also checks AggregateException recursively.
        /// </summary>
        /// <typeparam name="T">The expected exception type.</typeparam>
        /// <param name="func">The function to execute that should throw.</param>
        private async Task VerifyThrowsException<T>(Func<Task> func) where T : Exception
        {
            bool exceptionThrown = false;
            try
            {
                await func();
            }
            catch (AggregateException aggregate)
            {
                aggregate.Handle(HandleException<T>);
                exceptionThrown = true;
            }
            catch (T e)
            {
                exceptionThrown = true;
            }

            Assert.IsTrue(exceptionThrown);
        }

        private bool HandleException<T>(Exception e) where T : Exception
        {
            if (e is AggregateException aggregateException)
            {
                aggregateException.Handle(HandleException<T>);
                return true;
            }
            else if (e is T)
            {
                return true;
            }

            return false;
        }
    }
}
