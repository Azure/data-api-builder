using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Parsers;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.DataApiBuilder.Service.Services;
using Azure.DataApiBuilder.Service.Services.MetadataProviders;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MySqlConnector;
using Npgsql;
using static Azure.DataApiBuilder.Config.RuntimeConfigPath;

namespace Azure.DataApiBuilder.Service.Tests.Configuration
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
            TestContext.Properties.Add(RUNTIME_ENVIRONMENT_VAR_NAME, Environment.GetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME));
        }

        [ClassCleanup]
        public static void Cleanup()
        {
            if (File.Exists($"{CONFIGFILE_NAME}.Test{CONFIG_EXTENSION}"))
            {
                File.Delete($"{CONFIGFILE_NAME}.Test{CONFIG_EXTENSION}");
            }

            if (File.Exists($"{CONFIGFILE_NAME}.HostTest{CONFIG_EXTENSION}"))
            {
                File.Delete($"{CONFIGFILE_NAME}.HostTest{CONFIG_EXTENSION}");
            }

            if (File.Exists($"{CONFIGFILE_NAME}.Test.overrides{CONFIG_EXTENSION}"))
            {
                File.Delete($"{CONFIGFILE_NAME}.Test.overrides{CONFIG_EXTENSION}");
            }

            if (File.Exists($"{CONFIGFILE_NAME}.HostTest.overrides{CONFIG_EXTENSION}"))
            {
                File.Delete($"{CONFIGFILE_NAME}.HostTest.overrides{CONFIG_EXTENSION}");
            }
        }

        [TestCleanup]
        public void CleanupAfterEachTest()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, (string)TestContext.Properties[ASP_NET_CORE_ENVIRONMENT_VAR_NAME]);
            Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, (string)TestContext.Properties[RUNTIME_ENVIRONMENT_VAR_NAME]);
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, "");
            Environment.SetEnvironmentVariable($"{ENVIRONMENT_PREFIX}{nameof(RuntimeConfigPath.CONNSTRING)}", "");
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

            ConfigurationPostParameters config = GetCosmosConfigurationParameters();

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

            ConfigurationPostParameters config = GetCosmosConfigurationParameters();

            HttpResponseMessage result =
                await httpClient.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.Conflict, result.StatusCode);
        }

        [TestMethod("Validates setting the configuration at runtime.")]
        public async Task TestSettingConfigurations()
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient httpClient = server.CreateClient();

            ConfigurationPostParameters config = GetCosmosConfigurationParameters();

            HttpResponseMessage postResult =
                await httpClient.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);
        }

        [TestMethod("Validates that local cosmos settings can be loaded and the correct classes are in the service provider.")]
        public void TestLoadingLocalCosmosSettings()
        {
            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, COSMOS_ENVIRONMENT);
            TestServer server = new(Program.CreateWebHostBuilder(Array.Empty<string>()));

            ValidateCosmosDbSetup(server);
        }

        [TestMethod("Validates that local MsSql settings can be loaded and the correct classes are in the service provider."), TestCategory(TestCategory.MSSQL)]
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

        [TestMethod("Validates that local PostgreSql settings can be loaded and the correct classes are in the service provider."), TestCategory(TestCategory.POSTGRESQL)]
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

        [TestMethod("Validates that local MySql settings can be loaded and the correct classes are in the service provider."), TestCategory(TestCategory.MYSQL)]
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

            ConfigurationPostParameters config = GetCosmosConfigurationParameters();

            HttpResponseMessage postResult = await client.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.Conflict, postResult.StatusCode);
        }

        [TestMethod("Validates that setting the configuration at runtime will instantiate the proper classes.")]
        public async Task TestSettingConfigurationCreatesCorrectClasses()
        {
            TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
            HttpClient client = server.CreateClient();

            ConfigurationPostParameters config = GetCosmosConfigurationParameters();

            HttpResponseMessage postResult = await client.PostAsync("/configuration", JsonContent.Create(config));
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

            ValidateCosmosDbSetup(server);
            RuntimeConfigProvider configProvider = server.Services.GetService<RuntimeConfigProvider>();

            Assert.IsNotNull(configProvider, "Configuration Provider shouldn't be null after setting the configuration at runtime.");
            Assert.IsNotNull(configProvider.GetRuntimeConfiguration(), "Runtime Configuration shouldn't be null after setting the configuration at runtime.");
            RuntimeConfig configuration;
            bool isConfigSet = configProvider.TryGetRuntimeConfiguration(out configuration);
            Assert.IsNotNull(configuration, "TryGetRuntimeConfiguration should set the config in the out parameter.");
            Assert.IsTrue(isConfigSet, "TryGetRuntimeConfiguration should return true when the config is set.");

            Assert.AreEqual(DatabaseType.cosmos, configuration.DatabaseType, "Expected cosmos database type after configuring the runtime with cosmos settings.");
            Assert.AreEqual(config.Schema, configuration.CosmosDb.GraphQLSchema, "Expected the schema in the configuration to match the one sent to the configuration endpoint.");
            Assert.AreEqual(config.ConnectionString, configuration.ConnectionString, "Expected the connection string in the configuration to match the one sent to the configuration endpoint.");
        }

        [TestMethod("Validates that an exception is thrown if there's a null model in filter parser.")]
        public void VerifyExceptionOnNullModelinFilterParser()
        {
            ODataParser parser = new();
            try
            {
                // FilterParser has no model so we expect exception
                parser.GetFilterClause(filterQueryString: string.Empty, resourcePath: string.Empty);
                Assert.Fail();
            }
            catch (DataApiBuilderException exception)
            {
                Assert.AreEqual("The runtime has not been initialized with an Edm model.", exception.Message);
                Assert.AreEqual(HttpStatusCode.InternalServerError, exception.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.UnexpectedError, exception.SubStatusCode);
            }
        }

        /// <summary>
        /// This function will attempt to read the dab-config.json
        /// file into the RuntimeConfig class. It verifies the deserialization succeeds.
        /// </summary>
        [TestMethod("Validates if deserialization of new runtime config format succeeds.")]
        public void TestReadingRuntimeConfig()
        {
            string jsonString = File.ReadAllText(RuntimeConfigPath.DefaultName);
            RuntimeConfig.TryGetDeserializedConfig(jsonString, out RuntimeConfig runtimeConfig);
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
                        ((JsonElement)entity.Rest).Deserialize<RestEntitySettings>(RuntimeConfig.SerializerOptions);
                    Assert.IsTrue(
                        ((JsonElement)rest.Route).ValueKind == JsonValueKind.String
                        || ((JsonElement)rest.Route).ValueKind == JsonValueKind.Object);
                    if (((JsonElement)rest.Route).ValueKind == JsonValueKind.Object)
                    {
                        SingularPlural route = ((JsonElement)rest.Route).Deserialize<SingularPlural>(RuntimeConfig.SerializerOptions);
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
                        ((JsonElement)entity.GraphQL).Deserialize<GraphQLEntitySettings>(RuntimeConfig.SerializerOptions);
                    Assert.IsTrue(
                        ((JsonElement)graphQL.Type).ValueKind == JsonValueKind.String
                        || ((JsonElement)graphQL.Type).ValueKind == JsonValueKind.Object);
                    if (((JsonElement)graphQL.Type).ValueKind == JsonValueKind.Object)
                    {
                        SingularPlural route = ((JsonElement)graphQL.Type).Deserialize<SingularPlural>(RuntimeConfig.SerializerOptions);
                    }
                }

                Assert.IsInstanceOfType(entity.Permissions, typeof(PermissionSetting[]));
                foreach (PermissionSetting permission in entity.Permissions)
                {
                    foreach (object action in permission.Actions)
                    {
                        HashSet<Operation> allowedActions =
                            new() { Operation.All, Operation.Create, Operation.Read,
                                Operation.Update, Operation.Delete };
                        Assert.IsTrue(((JsonElement)action).ValueKind == JsonValueKind.String ||
                            ((JsonElement)action).ValueKind == JsonValueKind.Object);
                        if (((JsonElement)action).ValueKind == JsonValueKind.Object)
                        {
                            Config.Action configAction =
                                ((JsonElement)action).Deserialize<Config.Action>(RuntimeConfig.SerializerOptions);
                            Assert.IsTrue(allowedActions.Contains(configAction.Name));
                            Assert.IsTrue(configAction.Policy == null
                                || configAction.Policy.GetType() == typeof(Policy));
                            Assert.IsTrue(configAction.Fields == null
                                || configAction.Fields.GetType() == typeof(Field));
                        }
                        else
                        {
                            Operation name = ((JsonElement)action).Deserialize<Operation>(RuntimeConfig.SerializerOptions);
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
        /// precendence than default configuration file dab-config.json
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
        /// This function verifies the environment variable DAB_ENVIRONMENT
        /// takes precendence than ASPNETCORE_ENVIRONMENT for the configuration file.
        /// </summary>
        [TestMethod("Validates precedence is given to DAB_ENVIRONMENT environment variable name.")]
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
            RuntimeConfigPath configPath =
                TestHelper.GetRuntimeConfigPath(MSSQL_ENVIRONMENT);
            RuntimeConfigProvider configProvider = TestHelper.GetRuntimeConfigProvider(configPath);

            Mock<ILogger<RuntimeConfigValidator>> configValidatorLogger = new();
            IConfigValidator configValidator =
                new RuntimeConfigValidator(
                    configProvider,
                    new MockFileSystem(),
                    configValidatorLogger.Object);

            configValidator.ValidateConfig();
        }

        /// <summary>
        /// Set the connection string to an invalid value and expect the service to be unavailable
        // since without this env var, it would be available - guaranteeing this env variable
        // has highest precedence irrespective of what the connection string is in the config file.
        /// </summary>
        [TestMethod("Validates that environment variable DAB_CONNSTRING has highest precedence.")]
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

        /// <summary>
        /// Test to verify the precedence logic for config file based on Environment variables.
        /// </summary>
        [DataTestMethod]
        [DataRow("HostTest", "Test", false, $"{CONFIGFILE_NAME}.Test{CONFIG_EXTENSION}", DisplayName = "hosting and dab environment set, without considering overrides.")]
        [DataRow("HostTest", "", false, $"{CONFIGFILE_NAME}.HostTest{CONFIG_EXTENSION}", DisplayName = "only hosting environment set, without considering overrides.")]
        [DataRow("", "Test1", false, $"{CONFIGFILE_NAME}.Test1{CONFIG_EXTENSION}", DisplayName = "only dab environment set, without considering overrides.")]
        [DataRow("", "Test2", true, $"{CONFIGFILE_NAME}.Test2.overrides{CONFIG_EXTENSION}", DisplayName = "only dab environment set, considering overrides.")]
        [DataRow("HostTest1", "", true, $"{CONFIGFILE_NAME}.HostTest1.overrides{CONFIG_EXTENSION}", DisplayName = "only hosting environment set, considering overrides.")]
        public void TestGetConfigFileNameForEnvironment(
            string hostingEnvironmentValue,
            string environmentValue,
            bool considerOverrides,
            string expectedRuntimeConfigFile)
        {
            if (!File.Exists(expectedRuntimeConfigFile))
            {
                File.Create(expectedRuntimeConfigFile);
            }

            Environment.SetEnvironmentVariable(ASP_NET_CORE_ENVIRONMENT_VAR_NAME, hostingEnvironmentValue);
            Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, environmentValue);
            string actualRuntimeConfigFile = GetFileNameForEnvironment(hostingEnvironmentValue, considerOverrides);
            Assert.AreEqual(expectedRuntimeConfigFile, actualRuntimeConfigFile);
        }

        private static ConfigurationPostParameters GetCosmosConfigurationParameters()
        {
            string cosmosFile = $"{RuntimeConfigPath.CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{RuntimeConfigPath.CONFIG_EXTENSION}";
            return new(
                File.ReadAllText(cosmosFile),
                File.ReadAllText("schema.gql"),
                "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==");
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
