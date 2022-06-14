using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Parsers;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Azure.DataGateway.Service.Services.MetadataProviders;
using Azure.DataGateway.Service.Tests.SqlTests;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
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
        private const string COSMOS_ENVIRONMENT = TestCategory.COSMOS;
        private const string MSSQL_ENVIRONMENT = TestCategory.MSSQL;
        private const string MYSQL_ENVIRONMENT = TestCategory.MYSQL;
        private const string POSTGRESQL_ENVIRONMENT = TestCategory.POSTGRESQL;

        public TestContext TestContext { get; set; }

        [TestInitialize]
        public void Setup()
        {
            TestContext.Properties.Add(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, Environment.GetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME));
            TestContext.Properties.Add(RuntimeConfigPath.RUNTIME_ENVIRONMENT_VAR_NAME, Environment.GetEnvironmentVariable(RuntimeConfigPath.RUNTIME_ENVIRONMENT_VAR_NAME));
        }

        [DataTestMethod]
        [DataRow(new string[] { }, DisplayName = "No config returns 503 - config file flag absent")]
        [DataRow(new string[] { "--ConfigFileName=" }, DisplayName = "No config returns 503 - empty config file option")]
        [TestMethod("Validates that queries before runtime is configured returns a 503.")]
        public async Task TestNoConfigReturnsServiceUnavailable(string[] args)
        {
            TestServer server = new(Program.CreateWebHostBuilder(args));
            HttpClient httpClient = server.CreateClient();

            HttpResponseMessage result = await httpClient.GetAsync("/graphql");
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, result.StatusCode);
        }

        [TestMethod("Validates that once the configuration is set, the config controller isn't reachable.")]
        public async Task TestConflictAlreadySetConfiguration()
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            Dictionary<string, string> config = new()
            {
                {
                    $"{nameof(RuntimeConfigPath.ConfigFileName)}",
                    $"{RuntimeConfigPath.CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}"
                }
            };

            _ = await httpClient.PostAsync("/configuration", JsonContent.Create(config));
            ValidateCosmosDbSetup(server);

            HttpResponseMessage result = await httpClient.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.Conflict, result.StatusCode);
        }

        [TestMethod("Validates that the config controller returns a conflict when using local configuration.")]
        public async Task TestConflictLocalConfiguration()
        {
            Environment.SetEnvironmentVariable
                (ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            ValidateCosmosDbSetup(server);

            Dictionary<string, string> config = new()
            {
                {
                    $"{nameof(RuntimeConfigPath.ConfigFileName)}",
                    $"{RuntimeConfigPath.CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}"
                }
            };

            HttpResponseMessage result =
                await httpClient.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.Conflict, result.StatusCode);
        }

        [TestMethod("Validates that querying for a config that's not set returns a 404.")]
        public async Task TestGettingNonSetConfigurationReturns404()
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();
            Dictionary<string, string> config = new()
            {
                {
                    $"{nameof(RuntimeConfigPath.ConfigFileName)}",
                    $"{RuntimeConfigPath.CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}"
                }
            };

            _ = await httpClient.PostAsync("/configuration", JsonContent.Create(config));

            HttpResponseMessage result = await httpClient.GetAsync("/configuration?key=test");
            Assert.AreEqual(HttpStatusCode.NotFound, result.StatusCode);
        }

        [TestMethod("Validates that configurations are set and can be retrieved.")]
        public async Task TestSettingConfigurations()
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();
            Dictionary<string, string> config = new()
            {
                {
                    $"{nameof(RuntimeConfigPath.ConfigFileName)}",
                    $"{RuntimeConfigPath.CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}"
                },
            };

            HttpResponseMessage postResult =
                await httpClient.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

            foreach (KeyValuePair<string, string> setting in config)
            {
                HttpResponseMessage result =
                    await httpClient.GetAsync($"/configuration?key={setting.Key}");
                Assert.AreEqual(HttpStatusCode.OK, result.StatusCode);

                string text = await result.Content.ReadAsStringAsync();
                Assert.AreEqual(setting.Value, text);
            }
        }

        [TestMethod("Validates that local cosmos settings can be loaded and the correct classes are in the service provider.")]
        public void TestLoadingLocalCosmosSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            ValidateCosmosDbSetup(server);
        }

        [TestMethod("Validates that local MsSql settings can be loaded and the correct classes are in the service provider.")]
        public void TestLoadingLocalMsSqlSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MSSQL_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            object queryEngine = server.Services.GetService(typeof(IQueryEngine));
            Assert.IsInstanceOfType(queryEngine, typeof(SqlQueryEngine));

            object mutationEngine = server.Services.GetService(typeof(IMutationEngine));
            Assert.IsInstanceOfType(mutationEngine, typeof(SqlMutationEngine));

            object queryBuilder = server.Services.GetService(typeof(IQueryBuilder));
            Assert.IsInstanceOfType(queryBuilder, typeof(MsSqlQueryBuilder));

            object queryExecutor = server.Services.GetService(typeof(IQueryExecutor));
            Assert.IsInstanceOfType(queryExecutor, typeof(QueryExecutor<SqlConnection>));

            object sqlMetadataProvider = server.Services.GetService(typeof(ISqlMetadataProvider));
            Assert.IsInstanceOfType(sqlMetadataProvider, typeof(MsSqlMetadataProvider));
        }

        [TestMethod("Validates that local PostgreSql settings can be loaded and the correct classes are in the service provider.")]
        public void TestLoadingLocalPostgresSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, POSTGRESQL_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            object queryEngine = server.Services.GetService(typeof(IQueryEngine));
            Assert.IsInstanceOfType(queryEngine, typeof(SqlQueryEngine));

            object mutationEngine = server.Services.GetService(typeof(IMutationEngine));
            Assert.IsInstanceOfType(mutationEngine, typeof(SqlMutationEngine));

            object queryBuilder = server.Services.GetService(typeof(IQueryBuilder));
            Assert.IsInstanceOfType(queryBuilder, typeof(PostgresQueryBuilder));

            object queryExecutor = server.Services.GetService(typeof(IQueryExecutor));
            Assert.IsInstanceOfType(queryExecutor, typeof(QueryExecutor<NpgsqlConnection>));

            object sqlMetadataProvider = server.Services.GetService(typeof(ISqlMetadataProvider));
            Assert.IsInstanceOfType(sqlMetadataProvider, typeof(PostgreSqlMetadataProvider));
        }

        [TestMethod("Validates that local MySql settings can be loaded and the correct classes are in the service provider.")]
        public void TestLoadingLocalMySqlSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MYSQL_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            object queryEngine = server.Services.GetService(typeof(IQueryEngine));
            Assert.IsInstanceOfType(queryEngine, typeof(SqlQueryEngine));

            object mutationEngine = server.Services.GetService(typeof(IMutationEngine));
            Assert.IsInstanceOfType(mutationEngine, typeof(SqlMutationEngine));

            object queryBuilder = server.Services.GetService(typeof(IQueryBuilder));
            Assert.IsInstanceOfType(queryBuilder, typeof(MySqlQueryBuilder));

            object queryExecutor = server.Services.GetService(typeof(IQueryExecutor));
            Assert.IsInstanceOfType(queryExecutor, typeof(QueryExecutor<MySqlConnection>));

            object sqlMetadataProvider = server.Services.GetService(typeof(ISqlMetadataProvider));
            Assert.IsInstanceOfType(sqlMetadataProvider, typeof(MySqlMetadataProvider));
        }

        [TestMethod("Validates that trying to override configs that are already set fail.")]
        public async Task TestOverridingLocalSettingsFails()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();
            Dictionary<string, string> config = new()
            {
                {
                    $"{nameof(RuntimeConfigPath.ConfigFileName)}",
                    $"{RuntimeConfigPath.CONFIGFILE_NAME}.{MSSQL_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}"
                }
            };

            HttpResponseMessage postResult = await client.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.Conflict, postResult.StatusCode);
            // Since the body of the response when there's a conflict is the conflicting key:value pair, here we
            // expect DatabaseType:mssql.
            Assert.AreEqual($"ConfigFileName:hawaii-config.{MSSQL_ENVIRONMENT}.json",
                await postResult.Content.ReadAsStringAsync());
        }

        [TestMethod("Validates that setting the configuration at runtime will instantiate the proper classes.")]
        public async Task TestSettingConfigurationCreatesCorrectClasses()
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();
            Dictionary<string, string> config = new()
            {
                {
                    $"{nameof(RuntimeConfigPath.ConfigFileName)}",
                    $"{RuntimeConfigPath.CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}"
                }
            };

            HttpResponseMessage postResult = await client.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

            ValidateCosmosDbSetup(server);
        }

        [TestMethod("Validates that change notifications are raised by the InMemoryUpdateableConfigurationProvider.")]
        public void TestChangeNotificationsInMemoryUpdateableConfigurationProvider()
        {
            InMemoryUpdateableConfigurationProvider provider = new();
            Dictionary<string, string> config = new()
            {
                {
                    $"{nameof(RuntimeConfigPath.ConfigFileName)}",
                    $"{RuntimeConfigPath.CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}"
                }
            };

            provider.SetManyAndReload(config);

            IChangeToken token = provider.GetReloadToken();
            string finalConfigFileName;
            if (!provider.TryGet($"{nameof(RuntimeConfigPath.ConfigFileName)}", out finalConfigFileName))
            {
                Assert.Fail("RuntimeConfig File Name wasn't found in the provider.");
            }
            else
            {
                Assert.AreEqual(
                    $"{RuntimeConfigPath.CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}",
                    finalConfigFileName);
            }

            token.RegisterChangeCallback((state) =>
            {
                if (!provider.TryGet($"{nameof(RuntimeConfigPath.ConfigFileName)}",
                    out finalConfigFileName))
                {
                    Assert.Fail("RuntimeConfig File Name wasn't found in the provider.");
                }
            }, null);

            Dictionary<string, string> toUpdate = new()
            {
                {
                    $"{nameof(RuntimeConfigPath.ConfigFileName)}",
                    $"{RuntimeConfigPath.CONFIGFILE_NAME}.{POSTGRESQL_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}"
                }
            };
            provider.SetManyAndReload(toUpdate);
            Assert.AreEqual(
                $"{RuntimeConfigPath.CONFIGFILE_NAME}.{POSTGRESQL_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}",
                finalConfigFileName);
        }

        [TestMethod("Validates that an exception is thrown if there's a null model in filter parser.")]
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
        /// This function will attempt to read the hawaii-config.json
        /// file into the RuntimeConfig class. It verifies the deserialization succeeds.
        /// </summary>
        [TestMethod("Validates if deserialization of new runtime config format succeeds.")]
        public void TestReadingRuntimeConfig()
        {
            string jsonString = File.ReadAllText(RuntimeConfigPath.DefaultName);
            JsonSerializerOptions options = RuntimeConfig.GetDeserializationOptions();
            RuntimeConfig runtimeConfig =
                    JsonSerializer.Deserialize<RuntimeConfig>(jsonString, options);
            Assert.IsNotNull(runtimeConfig.Schema);
            Assert.IsInstanceOfType(runtimeConfig.DataSource, typeof(DataSource));
            Assert.IsTrue(runtimeConfig.CosmosDb == null
                || runtimeConfig.CosmosDb.GetType() == typeof(CosmosDbOptions));
            Assert.IsTrue(runtimeConfig.MsSql == null
                || runtimeConfig.MsSql.GetType() == typeof(MsSqlOptions));
            Assert.IsTrue(runtimeConfig.PostgreSql == null
                || runtimeConfig.PostgreSql.GetType() == typeof(PostgreSqlOptions));
            Assert.IsTrue(runtimeConfig.MySql == null
                || runtimeConfig.MySql.GetType() == typeof(MySqlOptions));

            Assert.IsInstanceOfType(runtimeConfig.Entities, typeof(Dictionary<string, Entity>));
            foreach (Entity entity in runtimeConfig.Entities.Values)
            {
                Assert.IsTrue(((JsonElement)entity.Source).ValueKind == JsonValueKind.String
                    || ((JsonElement)entity.Source).ValueKind == JsonValueKind.Object);

                Assert.IsTrue(entity.Rest == null
                    || ((JsonElement)entity.Rest).ValueKind == JsonValueKind.True
                    || ((JsonElement)entity.Rest).ValueKind == JsonValueKind.False
                    || ((JsonElement)entity.Rest).ValueKind == JsonValueKind.Object);
                if (entity.Rest != null
                    && ((JsonElement)entity.Rest).ValueKind == JsonValueKind.Object)
                {
                    RestEntitySettings rest =
                        ((JsonElement)entity.Rest).Deserialize<RestEntitySettings>(options);
                    Assert.IsTrue(
                        ((JsonElement)rest.Route).ValueKind == JsonValueKind.String
                        || ((JsonElement)rest.Route).ValueKind == JsonValueKind.Object);
                    if (((JsonElement)rest.Route).ValueKind == JsonValueKind.Object)
                    {
                        SingularPlural route = ((JsonElement)rest.Route).Deserialize<SingularPlural>(options);
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
                        ((JsonElement)entity.GraphQL).Deserialize<GraphQLEntitySettings>(options);
                    Assert.IsTrue(
                        ((JsonElement)graphQL.Type).ValueKind == JsonValueKind.String
                        || ((JsonElement)graphQL.Type).ValueKind == JsonValueKind.Object);
                    if (((JsonElement)graphQL.Type).ValueKind == JsonValueKind.Object)
                    {
                        SingularPlural route = ((JsonElement)graphQL.Type).Deserialize<SingularPlural>(options);
                    }
                }

                Assert.IsInstanceOfType(entity.Permissions, typeof(PermissionSetting[]));
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
                                ((JsonElement)action).Deserialize<Config.Action>(options);
                            Assert.IsTrue(allowedActions.Contains(configAction.Name));
                            Assert.IsTrue(configAction.Policy == null
                                || configAction.Policy.GetType() == typeof(Policy));
                            Assert.IsTrue(configAction.Fields == null
                                || configAction.Fields.GetType() == typeof(Field));
                        }
                        else
                        {
                            string name = ((JsonElement)action).Deserialize<string>(options);
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

        /// <summary>
        /// This function verifies command line configuration provider takes higher
        /// precendence than default configuration file hawaii-config.json
        /// </summary>
        [TestMethod("Validates command line configuration provider.")]
        public void TestCommandLineConfigurationProvider()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MSSQL_ENVIRONMENT);
            string[] args = new[]
            {
                $"--ConfigFileName={RuntimeConfigPath.CONFIGFILE_NAME}." +
                $"{COSMOS_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}"
            };

            TestServer server = new(Program.CreateWebHostBuilder(args));

            ValidateCosmosDbSetup(server);
        }

        /// <summary>
        /// This function verifies the environment variable HAWAII_RUNTIME
        /// takes precendence than ASPNETCORE_ENVIRONMENT for the configuration file.
        /// </summary>
        [TestMethod("Validates precedence is given to HAWAII_RUNTIME environment variable name.")]
        public void TestRuntimeEnvironmentVariable()
        {
            Environment.SetEnvironmentVariable(
                ASP_NET_CORE_ENVIRONMENT_VAR_NAME, MSSQL_ENVIRONMENT);
            Environment.SetEnvironmentVariable(
                RuntimeConfigPath.RUNTIME_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);

            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            ValidateCosmosDbSetup(server);
        }

        [TestMethod("Validates the runtime configuration file.")]
        public void TestConfigIsValid()
        {
            IOptionsMonitor<RuntimeConfigPath> configPath =
                SqlTestHelper.LoadConfig(MSSQL_ENVIRONMENT);
            IConfigValidator configValidator = new RuntimeConfigValidator(configPath, new MockFileSystem());
            configValidator.ValidateConfig();
        }

        /// <summary>
        /// Set the connection string to an invalid value and expect the service to be unavailable
        // since without this env var, it would be available - guaranteeing this env variable
        // has highest precedence irrespective of what the connection string is in the config file.
        /// </summary>
        [TestMethod("Validates that environment variable HAWAII_CONNSTRING has highest precedence.")]
        public void TestConnectionStringEnvVarHasHighestPrecedence()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            Environment.SetEnvironmentVariable(
                $"{RuntimeConfigPath.ENVIRONMENT_PREFIX}{nameof(RuntimeConfigPath.CONNSTRING)}",
                "Invalid Connection String");
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));
            try
            {
                _ = server.Services.GetService(typeof(CosmosClientProvider)) as CosmosClientProvider;
                Assert.Fail($"{RuntimeConfigPath.ENVIRONMENT_PREFIX}{nameof(RuntimeConfigPath.CONNSTRING)} is not given highest precedence");
            }
            catch (ArgumentException)
            {

            }
        }

        [TestMethod("Validates that an exception is thrown if config file for the runtime engine is not found.")]
        public void TestConfigFileNotFound()
        {
            RuntimeConfigPath runtimeConfigPath = new()
            {
                ConfigFileName = "NonExistentConfigFile.json"
            };

            Exception ex = Assert.ThrowsException<FileNotFoundException>(() => runtimeConfigPath.SetRuntimeConfigValue());
            Console.WriteLine(ex.Message);
            Assert.AreEqual(ex.Message, "Requested configuration file NonExistentConfigFile.json does not exist.");

        }

        [TestCleanup]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, (string)TestContext.Properties[ASP_NET_CORE_ENVIRONMENT_VAR_NAME]);
            Environment.SetEnvironmentVariable(RuntimeConfigPath.RUNTIME_ENVIRONMENT_VAR_NAME, (string)TestContext.Properties[RuntimeConfigPath.RUNTIME_ENVIRONMENT_VAR_NAME]);
        }

        private static void ValidateCosmosDbSetup(TestServer server)
        {
            object metadataProvider = server.Services.GetService(typeof(ISqlMetadataProvider));
            Assert.IsInstanceOfType(metadataProvider, typeof(CosmosSqlMetadataProvider));

            object queryEngine = server.Services.GetService(typeof(IQueryEngine));
            Assert.IsInstanceOfType(queryEngine, typeof(CosmosQueryEngine));

            object mutationEngine = server.Services.GetService(typeof(IMutationEngine));
            Assert.IsInstanceOfType(mutationEngine, typeof(CosmosMutationEngine));

            CosmosClientProvider cosmosClientProvider = server.Services.GetService(typeof(CosmosClientProvider)) as CosmosClientProvider;
            Assert.IsNotNull(cosmosClientProvider);
            Assert.IsNotNull(cosmosClientProvider.Client);
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
