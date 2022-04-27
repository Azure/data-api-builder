using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using MySqlConnector;
using Npgsql;

namespace Azure.DataGateway.Service.Tests.Configuration
{
    [TestClass]
    public class ConfigurationTests
    {
        private const string ASP_NET_CORE_ENVIRONMENT_VAR_NAME = "ASPNETCORE_ENVIRONMENT";
        private string _cosmosResolverConfig = File.ReadAllText("cosmos-config.json");
        private string _graphqlSchema = File.ReadAllText("schema.gql");
        private const string COMSMOS_DEFAULT_CONNECTION_STRING = "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==";

        [TestMethod("Validates that queries before runtime is configured returns a 503.")]
        public async Task TestNoConfigReturnsServiceUnavailable()
        {
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            HttpResponseMessage result = await httpClient.GetAsync("/graphql");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        }

        [TestMethod("Validates that once the configuration is set, the config controller isn't reachable.")]
        public async Task TestConflictAlreadySetConfiguration()
        {
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            Dictionary<string, string> config = new()
            {
                { "DataGatewayConfig:DatabaseType", "cosmos" },
                { "DataGatewayConfig:ResolverConfigFile", "cosmos-config.json" },
                { "DataGatewayConfig:DatabaseConnection:ConnectionString", COMSMOS_DEFAULT_CONNECTION_STRING }
            };

            _ = await httpClient.PostAsync("/configuration", JsonContent.Create(config));
            ValidateCosmosDbSetup(server);

            HttpResponseMessage result = await httpClient.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.Conflict, result.StatusCode);
        }

        [TestMethod("Validates that the config controller returns a conflict when using local configuration.")]
        public async Task TestConflictLocalConfiguration()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, "Cosmos");
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            ValidateCosmosDbSetup(server);

            Dictionary<string, string> config = new()
            {
                { "DataGatewayConfig:DatabaseType", "cosmos" },
                { "DataGatewayConfig:ResolverConfigFile", "cosmos-config.json" },
                { "DataGatewayConfig:DatabaseConnection:ConnectionString", "Cosmos" }
            };

            HttpResponseMessage result = await httpClient.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.Conflict, result.StatusCode);
        }

        [TestMethod("Validates that querying for a config that's not set returns a 404.")]
        public async Task TestGettingNonSetConfigurationReturns404()
        {
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();
            Dictionary<string, string> config = new()
            {
                { "DataGatewayConfig:DatabaseType", "cosmos" },
                { "DataGatewayConfig:ResolverConfigFile", "cosmos-config.json" },
                { "DataGatewayConfig:DatabaseConnection:ConnectionString", "Cosmos" }
            };
            _ = await httpClient.PostAsync("/configuration", JsonContent.Create(config));

            HttpResponseMessage result = await httpClient.GetAsync("/configuration?key=test");
            Assert.AreEqual(HttpStatusCode.NotFound, result.StatusCode);
        }

        [TestMethod("Validates that configurations are set and can be retrieved.")]
        public async Task TestSettingConfigurations()
        {
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            Dictionary<string, string> config = new()
            {
                { "DataGatewayConfig:DatabaseType", "cosmos" },
                { "DataGatewayConfig:ResolverConfigFile", "cosmos-config.json" },
                { "DataGatewayConfig:DatabaseConnection:ConnectionString", "Cosmos" }
            };
            HttpResponseMessage postResult = await httpClient.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

            foreach (KeyValuePair<string, string> setting in config)
            {
                HttpResponseMessage result = await httpClient.GetAsync($"/configuration?key={setting.Key}");
                Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);

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

            object graphQLMetadataProvider = server.Services.GetService(typeof(IGraphQLMetadataProvider));
            Assert.IsInstanceOfType(graphQLMetadataProvider, typeof(SqlGraphQLFileMetadataProvider));

            object sqlMetadataProvider = server.Services.GetService(typeof(ISqlMetadataProvider));
            Assert.IsInstanceOfType(sqlMetadataProvider, typeof(MsSqlMetadataProvider));
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

            object graphQLMetadataProvider = server.Services.GetService(typeof(IGraphQLMetadataProvider));
            Assert.IsInstanceOfType(graphQLMetadataProvider, typeof(SqlGraphQLFileMetadataProvider));

            object sqlMetadataProvider = server.Services.GetService(typeof(ISqlMetadataProvider));
            Assert.IsInstanceOfType(sqlMetadataProvider, typeof(PostgreSqlMetadataProvider));
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

            object graphQLMetadataProvider = server.Services.GetService(typeof(IGraphQLMetadataProvider));
            Assert.IsInstanceOfType(graphQLMetadataProvider, typeof(SqlGraphQLFileMetadataProvider));

            object sqlMetadataProvider = server.Services.GetService(typeof(ISqlMetadataProvider));
            Assert.IsInstanceOfType(sqlMetadataProvider, typeof(MySqlMetadataProvider));
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
                { "DataGatewayConfig:DatabaseType", "mssql" }
            };

            HttpResponseMessage postResult = await client.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.Conflict, postResult.StatusCode);
            // Since the body of the response when there's a conflict is the conflicting key:value pair, here we
            // expect DatabaseType:mssql.
            Assert.AreEqual("DataGatewayConfig:DatabaseType:mssql", await postResult.Content.ReadAsStringAsync());
        }

        [TestMethod("Validates that setting the configuration at runtime will instantiate the proper classes.")]
        public async Task TestSettingConfigurationCreatesCorrectClasses()
        {
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();
            Dictionary<string, string> config = new()
            {
                { "DataGatewayConfig:DatabaseType", "cosmos" },
                { "DataGatewayConfig:ResolverConfigFile", "cosmos-config.json" },
                { "DataGatewayConfig:DatabaseConnection:ConnectionString", COMSMOS_DEFAULT_CONNECTION_STRING }
            };

            HttpResponseMessage postResult = await client.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

            ValidateCosmosDbSetup(server);
        }

        [TestMethod("Validates setting the resolver config and graphql schema.")]
        public async Task TestSettingResolverConfigAndSchema()
        {
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();
            Dictionary<string, string> config = new()
            {
                { "DataGatewayConfig:DatabaseType", "cosmos" },
                { "DataGatewayConfig:ResolverConfig", _cosmosResolverConfig },
                { "DataGatewayConfig:GraphQLSchema", _graphqlSchema },
                { "DataGatewayConfig:DatabaseConnection:ConnectionString", COMSMOS_DEFAULT_CONNECTION_STRING }
            };

            HttpResponseMessage postResult = await client.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);
        }

        [TestMethod("Validates that setting the resolver config without setting the schema fails.")]
        public async Task TestSettingResolverConfigAndNotSchemaFails()
        {
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();
            Dictionary<string, string> config = new()
            {
                { "DataGatewayConfig:DatabaseType", "cosmos" },
                { "DataGatewayConfig:ResolverConfig", _cosmosResolverConfig },
                { "DataGatewayConfig:DatabaseConnection:ConnectionString", COMSMOS_DEFAULT_CONNECTION_STRING }
            };

            await VerifyThrowsException<NotSupportedException>(async () =>
            {
                HttpResponseMessage postResult = await client.PostAsync("/configuration", JsonContent.Create(config));
            });
        }

        [TestMethod("Validates that setting both the resolver config and the config file fails.")]
        public async Task TestSettingResolverConfigAndPathFails()
        {
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();
            Dictionary<string, string> config = new()
            {
                { "DataGatewayConfig:DatabaseType", "cosmos" },
                { "DataGatewayConfig:ResolverConfig", _cosmosResolverConfig },
                { "DataGatewayConfig:ResolverConfigFile", "cosmos-config.json" },
                { "DataGatewayConfig:DatabaseConnection:ConnectionString", COMSMOS_DEFAULT_CONNECTION_STRING }
            };

            await VerifyThrowsException<NotSupportedException>(async () =>
            {
                HttpResponseMessage postResult = await client.PostAsync("/configuration", JsonContent.Create(config));
            });
        }

        [TestMethod("Validates that change notifications are raised by the InMemoryUpdateableConfigurationProvider.")]
        public void TestChangeNotificationsInMemoryUpdateableConfigurationProvider()
        {
            InMemoryUpdateableConfigurationProvider provider = new();
            Dictionary<string, string> config = new()
            {
                { "DataGatewayConfig:DatabaseType", "cosmos" },
            };
            provider.SetManyAndReload(config);

            IChangeToken token = provider.GetReloadToken();
            string finalDatabaseType;
            string finalResolverConfigFile = "";
            if (!provider.TryGet("DataGatewayConfig:DatabaseType", out finalDatabaseType))
            {
                Assert.Fail("DataGatewayConfig:DatabaseType wasn't found in the provider.");
            }
            else
            {
                Assert.AreEqual("cosmos", finalDatabaseType);
            }

            token.RegisterChangeCallback((state) =>
            {
                if (!provider.TryGet("DataGatewayConfig:DatabaseType", out finalDatabaseType))
                {
                    Assert.Fail("DataGatewayConfig:DatabaseType wasn't found in the provider.");
                }

                if (!provider.TryGet("DataGatewayConfig:ResolverConfigFile", out finalResolverConfigFile))
                {
                    Assert.Fail("DataGatewayConfig:ResolverConfigFile wasn't found in the provider.");
                }
            }, null);

            Dictionary<string, string> toUpdate = new()
            {
                { "DataGatewayConfig:DatabaseType", "postgresql" },
                { "DataGatewayConfig:ResolverConfigFile", "some-file.json" }
            };
            provider.SetManyAndReload(toUpdate);
            Assert.AreEqual("postgresql", finalDatabaseType);
            Assert.AreEqual("some-file.json", finalResolverConfigFile);
        }

        [TestMethod("Validates that the develeoper config is correctly read and its fields are populated appropriately.")]
        public void VerifyExceptionOnNullModelinFilterParser()
        {
            FilterParser parser = new();
            try
            {
                // FilterParser has no model so we expect exception
                parser.GetFilterClause(filterQueryString: string.Empty, resourcePath: string.Empty);
                Assert.Fail();
            }
            catch (DataGatewayException exception)
            {
                Assert.AreEqual("The runtime has not been initialized with an Edm model.", exception.Message);
                Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
                Assert.AreEqual(DataGatewayException.SubStatusCodes.UnexpectedError, exception.SubStatusCode);
            }
        }

        /// <summary>
        /// This function will attempt to read the runtime-config-test.json
        /// file into the RuntimeConfig class. It verifies the deserialization succeeds.
        /// </summary>
        [TestMethod("Validates if deserialization of new runtime config format succeeds.")]
        public void TestReadingRuntimeConfig()
        {
            string jsonString = File.ReadAllText("runtime-config-test.json");
            // use camel case
            // convert Enum to strings
            // case insensitive
            JsonSerializerOptions options = new()
            {
                PropertyNameCaseInsensitive = true,
                Converters =
                {
                    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)
                }
            };

            RuntimeConfig runtimeConfig =
                    JsonSerializer.Deserialize<RuntimeConfig>(jsonString, options);
            Assert.IsNotNull(runtimeConfig.Schema);
            Assert.IsTrue(runtimeConfig.DataSource.GetType() == typeof(DataSource));
            Assert.IsTrue(runtimeConfig.CosmosDb == null
                || runtimeConfig.CosmosDb.GetType() == typeof(CosmosDbOptions));
            Assert.IsTrue(runtimeConfig.MsSql == null
                || runtimeConfig.MsSql.GetType() == typeof(MsSqlOptions));
            Assert.IsTrue(runtimeConfig.PostgreSql == null
                || runtimeConfig.PostgreSql.GetType() == typeof(PostgreSqlOptions));
            Assert.IsTrue(runtimeConfig.MySql == null
                || runtimeConfig.MySql.GetType() == typeof(MySqlOptions));

            Assert.IsTrue(runtimeConfig.Entities.GetType() == typeof(Dictionary<string, Entity>));
            foreach (Entity entity in runtimeConfig.Entities.Values)
            {
                Assert.IsTrue(((JsonElement)entity.Source).ValueKind == JsonValueKind.String ||
                    ((JsonElement)entity.Source).ValueKind == JsonValueKind.Object);

                Assert.IsTrue(entity.Rest == null
                    || ((JsonElement)entity.Rest).ValueKind == JsonValueKind.True
                    || ((JsonElement)entity.Rest).ValueKind == JsonValueKind.False
                    || ((JsonElement)entity.Rest).ValueKind == JsonValueKind.Object);
                if (entity.Rest != null
                    && ((JsonElement)entity.Rest).ValueKind == JsonValueKind.Object)
                {
                    RestEntitySettings rest =
                        ((JsonElement)entity.Rest).Deserialize<RestEntitySettings>();
                    Assert.IsTrue(
                        ((JsonElement)rest.Route).ValueKind == JsonValueKind.String
                        || ((JsonElement)rest.Route).ValueKind == JsonValueKind.Object);
                    if (((JsonElement)rest.Route).ValueKind == JsonValueKind.Object)
                    {
                        SingularPlural route = ((JsonElement)rest.Route).Deserialize<SingularPlural>();
                    }
                }

                Assert.IsTrue(entity.GraphQL == null
                    || ((JsonElement)entity.GraphQL).ValueKind == JsonValueKind.True
                    || ((JsonElement)entity.GraphQL).ValueKind == JsonValueKind.False
                    || ((JsonElement)entity.GraphQL).ValueKind == JsonValueKind.Object);
                if (entity.GraphQL != null
                    && ((JsonElement)entity.GraphQL).ValueKind == JsonValueKind.Object)
                {
                    GraphQLEntitySettings graphQL =
                        ((JsonElement)entity.GraphQL).Deserialize<GraphQLEntitySettings>();
                    Assert.IsTrue(
                        ((JsonElement)graphQL.Type).ValueKind == JsonValueKind.String
                        || ((JsonElement)graphQL.Type).ValueKind == JsonValueKind.Object);
                    if (((JsonElement)graphQL.Type).ValueKind == JsonValueKind.Object)
                    {
                        SingularPlural route = ((JsonElement)graphQL.Type).Deserialize<SingularPlural>();
                    }
                }

                Assert.IsTrue(entity.Permissions.GetType() == typeof(PermissionSetting[]));
                foreach (PermissionSetting permission in entity.Permissions)
                {
                    foreach (object action in permission.Actions)
                    {
                        HashSet<string> allowedActions =
                            new() { "*", "create", "read", "update", "delete" };
                        Assert.IsTrue(((JsonElement)action).ValueKind == JsonValueKind.String ||
                            ((JsonElement)action).ValueKind == JsonValueKind.Object);
                        if (((JsonElement)action).ValueKind == JsonValueKind.Object)
                        {
                            Config.Action configAction =
                                ((JsonElement)action).Deserialize<Config.Action>();
                            Assert.IsTrue(allowedActions.Contains(configAction.Name));
                            Assert.IsTrue(configAction.Policy == null
                                || configAction.Policy.GetType() == typeof(Policy));
                            Assert.IsTrue(configAction.Fields == null
                                || configAction.Fields.GetType() == typeof(Field));
                        }
                        else
                        {
                            string name = ((JsonElement)action).Deserialize<string>();
                            Assert.IsTrue(allowedActions.Contains(name));
                        }
                    }
                }

                Assert.IsTrue(entity.Relationships == null ||
                    entity.Relationships.GetType()
                        == typeof(Dictionary<string, Relationship>));
                Assert.IsTrue(entity.Mappings == null ||
                    entity.Mappings.GetType()
                        == typeof(Dictionary<string, string>));
            }
        }

        [TestCleanup]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, "");
        }

        private static void ValidateCosmosDbSetup(TestServer server)
        {
            object metadataProvider = server.Services.GetService(typeof(IGraphQLMetadataProvider));
            Assert.IsInstanceOfType(metadataProvider, typeof(GraphQLFileMetadataProvider));

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
            catch (T)
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
