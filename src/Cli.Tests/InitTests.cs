// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests
{
    /// <summary>
    /// Test for config file initialization.
    /// </summary>
    [TestClass]
    public class InitTests
    {
        private string _basicRuntimeConfig = string.Empty;

        /// <summary>
        /// Setup the logger and test file for CLI
        /// </summary>
        [ClassInitialize]
        public static void Setup()
        {
            if (!File.Exists(TEST_SCHEMA_FILE))
            {
                File.Create(TEST_SCHEMA_FILE);
            }

            TestHelper.SetupTestLoggerForCLI();
        }

        /// <summary>
        /// Test the simple init config for mssql database. PG and MySQL should be similar.
        /// There is no need for a separate test.
        /// </summary>
        [TestMethod]
        public void MssqlDatabase()
        {
            InitOptions options = new(
                databaseType: DatabaseType.mssql,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: true,
                hostMode: HostModeType.Development,
                corsOrigin: new List<string>() { "http://localhost:3000", "http://nolocalhost:80" },
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                restPath: "rest-api",
                config: TEST_RUNTIME_CONFIG_FILE);

            _basicRuntimeConfig =
            @"{" +
                @"""$schema"": """ + DAB_DRAFT_SCHEMA_TEST_PATH + @"""" + "," +
                @"""data-source"": {
                    ""database-type"": ""mssql"",
                    ""connection-string"": ""testconnectionstring"",
                    ""options"":{
                        ""set-session-context"": true
                    }
                },
                ""entities"": {}
            }";

            // Adding runtime settings to the above basic config
            string expectedRuntimeConfig = AddPropertiesToJson(
                _basicRuntimeConfig,
                GetDefaultTestRuntimeSettingString(
                    HostModeType.Development,
                    new List<string>() { "http://localhost:3000", "http://nolocalhost:80" },
                    restPath: options.RestPath)
            );

            RunTest(options, expectedRuntimeConfig);
        }

        /// <summary>
        /// Test the simple init config for cosmosdb_postgresql database.
        /// </summary>
        [TestMethod]
        public void CosmosDbPostgreSqlDatabase()
        {
            InitOptions options = new(
                databaseType: DatabaseType.cosmosdb_postgresql,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: false,
                hostMode: HostModeType.Development,
                corsOrigin: new List<string>() { "http://localhost:3000", "http://nolocalhost:80" },
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                restPath: "/rest-endpoint",
                config: TEST_RUNTIME_CONFIG_FILE);

            _basicRuntimeConfig =
            @"{" +
                @"""$schema"": """ + DAB_DRAFT_SCHEMA_TEST_PATH + @"""" + "," +
                @"""data-source"": {
                    ""database-type"": ""cosmosdb_postgresql"",
                    ""connection-string"": ""testconnectionstring""
                },
                ""entities"": {}
            }";

            // Adding runtime settings to the above basic config
            string expectedRuntimeConfig = AddPropertiesToJson(
                _basicRuntimeConfig,
                GetDefaultTestRuntimeSettingString(
                    HostModeType.Development,
                    new List<string>() { "http://localhost:3000", "http://nolocalhost:80" },
                    restPath: options.RestPath)
            );
            RunTest(options, expectedRuntimeConfig);
        }

        /// <summary>
        /// Test to verify creation of initial config without providing
        /// connection-string
        /// </summary>
        [TestMethod]
        public void TestInitializingConfigWithoutConnectionString()
        {
            InitOptions options = new(
                databaseType: DatabaseType.mssql,
                connectionString: null,
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: false,
                hostMode: HostModeType.Development,
                corsOrigin: new List<string>() { "http://localhost:3000", "http://nolocalhost:80" },
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                config: TEST_RUNTIME_CONFIG_FILE);

            _basicRuntimeConfig =
            @"{" +
                @"""$schema"": """ + DAB_DRAFT_SCHEMA_TEST_PATH + @"""" + "," +
                @"""data-source"": {
                    ""database-type"": ""mssql"",
                    ""connection-string"": """",
                    ""options"":{
                        ""set-session-context"": false
                    }
                },
                ""entities"": {}
            }";

            // Adding runtime settings to the above basic config
            string expectedRuntimeConfig = AddPropertiesToJson(
                _basicRuntimeConfig,
                GetDefaultTestRuntimeSettingString(
                    HostModeType.Development,
                    new List<string>() { "http://localhost:3000", "http://nolocalhost:80" })
            );
            RunTest(options, expectedRuntimeConfig);
        }

        /// <summary>
        /// Test cosmosdb_nosql specifc settings like cosmosdb_nosql-database, cosmosdb_nosql-container, cosmos-schema file.
        /// </summary>
        [TestMethod]
        public void CosmosDbNoSqlDatabase()
        {
            InitOptions options = new(
                databaseType: DatabaseType.cosmosdb_nosql,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: "testdb",
                cosmosNoSqlContainer: "testcontainer",
                graphQLSchemaPath: TEST_SCHEMA_FILE,
                setSessionContext: false,
                hostMode: HostModeType.Production,
                corsOrigin: null,
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                config: TEST_RUNTIME_CONFIG_FILE);

            _basicRuntimeConfig =
            @"{" +
                @"""$schema"": """ + DAB_DRAFT_SCHEMA_TEST_PATH + @"""" + "," +
                @"""data-source"": {
                    ""database-type"": ""cosmosdb_nosql"",
                    ""connection-string"": ""testconnectionstring"",
                    ""options"": {
                        ""database"": ""testdb"",
                        ""container"": ""testcontainer"",
                        ""schema"": ""test-schema.gql""
                    }
                },
                ""entities"": {}
            }";

            // Adding runtime settings to the above basic config
            string expectedRuntimeConfig = AddPropertiesToJson(
                _basicRuntimeConfig,
                GetDefaultTestRuntimeSettingString(restPath: null));
            RunTest(options, expectedRuntimeConfig);
        }

        /// <summary>
        /// Verify that if graphQLSchema file is not present, config file won't be generated.
        /// It will show an error stating the graphQL schema file not found.
        /// </summary>
        [DataRow("no-schema.gql", false, DisplayName = "FAIL: GraphQL Schema file not available.")]
        [DataRow(TEST_SCHEMA_FILE, true, DisplayName = "PASS: GraphQL Schema file available.")]
        [DataTestMethod]
        public void VerifyGraphQLSchemaFileAvailabilityForCosmosDB(
            string schemaFileName,
            bool expectSuccess
        )
        {
            InitOptions options = new(
                databaseType: DatabaseType.cosmosdb_nosql,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: "somedb",
                cosmosNoSqlContainer: "somecontainer",
                graphQLSchemaPath: schemaFileName,
                setSessionContext: false,
                hostMode: HostModeType.Production,
                corsOrigin: null,
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                config: TEST_RUNTIME_CONFIG_FILE);

            Assert.AreEqual(expectSuccess, ConfigGenerator.TryCreateRuntimeConfig(options, out _));
        }

        /// <summary>
        /// Verify that if either database or graphQLSchema is null or empty, we will get error.
        /// </summary>
        [DataRow(null, "testcontainer", "", false, DisplayName = "Both database and schema are either null or empty.")]
        [DataRow("", "testcontainer", "testschema", false, DisplayName = "database is empty.")]
        [DataRow("testDatabase", "testcontainer", "", false, DisplayName = "database is provided, Schema is null.")]
        [DataRow("testDatabase", null, "", false, DisplayName = "database is provided, container and Schema is null/empty.")]
        [DataRow("testDatabase", null, TEST_SCHEMA_FILE, true, DisplayName = "database and schema provided, container is null/empty.")]
        [DataTestMethod]
        public void VerifyRequiredOptionsForCosmosDbNoSqlDatabase(
            string? cosmosDatabase,
            string? cosmosContainer,
            string? graphQLSchema,
            bool expectedResult)
        {
            InitOptions options = new(
                databaseType: DatabaseType.cosmosdb_nosql,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: cosmosDatabase,
                cosmosNoSqlContainer: cosmosContainer,
                graphQLSchemaPath: graphQLSchema,
                setSessionContext: false,
                hostMode: HostModeType.Production,
                corsOrigin: null,
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                config: TEST_RUNTIME_CONFIG_FILE);

            Assert.AreEqual(expectedResult, ConfigGenerator.TryCreateRuntimeConfig(options, out _));
        }

        /// <summary>
        /// Verify that if both REST and GraphQL is disabled, we will get error.
        /// </summary>
        [DataRow(true, true, false, DisplayName = "Both REST and GraphQL disabled.")]
        [DataRow(true, false, true, DisplayName = "REST disabled, and GraphQL enabled.")]
        [DataRow(false, true, true, DisplayName = "REST enabled, and GraphQL disabled.")]
        [DataRow(false, false, true, DisplayName = "Both REST and GraphQL are enabled.")]
        [DataTestMethod]
        public void EnsureFailureWhenBothRestAndGraphQLAreDisabled(
            bool RestDisabled,
            bool GraphQLDisabled,
            bool expectedResult)
        {
            InitOptions options = new(
                databaseType: DatabaseType.mssql,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: false,
                hostMode: HostModeType.Production,
                corsOrigin: null,
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                restDisabled: RestDisabled,
                graphqlDisabled: GraphQLDisabled,
                config: TEST_RUNTIME_CONFIG_FILE);

            Assert.AreEqual(expectedResult, ConfigGenerator.TryCreateRuntimeConfig(options, out _));
        }

                /// <summary>
        /// Test to verify creation of initial config with special characters
        /// such as [!,@,#,$,%,^,&,*, ,(,)] in connection-string.
        /// </summary>
        [TestMethod]
        public void TestSpecialCharactersInConnectionString()
        {
            InitOptions options = new(
                databaseType: DatabaseType.mssql,
                connectionString: "A!string@with#some$special%characters^to&check*proper(serialization)including space.",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: false,
                hostMode: HostModeType.Production,
                corsOrigin: null,
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                config: TEST_RUNTIME_CONFIG_FILE);

            _basicRuntimeConfig =
            @"{" +
                @"""$schema"": """ + DAB_DRAFT_SCHEMA_TEST_PATH + @"""" + "," +
                @"""data-source"": {
                    ""database-type"": ""mssql"",
                    ""connection-string"": ""A!string@with#some$special%characters^to&check*proper(serialization)including space."",
                    ""options"":{
                        ""set-session-context"": false
                    }
                },
                ""entities"": {}
            }";

            // Adding runtime settings to the above basic config
            string expectedRuntimeConfig = AddPropertiesToJson(
                _basicRuntimeConfig,
                GetDefaultTestRuntimeSettingString()
            );
            RunTest(options, expectedRuntimeConfig);
        }
        
        /// <summary>
        /// Test to verify that an error is thrown when user tries to
        /// initialize a config with a file name that already exists.
        /// </summary>
        [TestMethod]
        public void EnsureFailureOnReInitializingExistingConfig()
        {
            InitOptions options = new(
                databaseType: DatabaseType.mssql,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: false,
                hostMode: HostModeType.Development,
                corsOrigin: new List<string>() { },
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                config: TEST_RUNTIME_CONFIG_FILE);

            // Config generated successfully for the first time.
            Assert.AreEqual(true, ConfigGenerator.TryGenerateConfig(options));

            // Error is thrown because the config file with the same name
            // already exists.
            Assert.AreEqual(false, ConfigGenerator.TryGenerateConfig(options));
        }

        /// <summary>
        /// Test to verify the config is correctly generated with different Authentication providers.
        /// Audience and Issuer are needed only when the provider is JWT.
        /// Example:
        /// 1. With EasyAuth or Simulator
        /// "authentication": {
        ///     "provider": "StaticWebApps/AppService/Simulator"
        /// }
        ///
        /// 2. With JWT provider
        /// "authentication": {
        ///     "provider": "AzureAD"
        ///      "Jwt":
        ///      {
        ///          "Audience": "aud",
        ///          "Issuer": "iss"
        ///      }
        /// }
        /// </summary>
        [DataTestMethod]
        [DataRow("StaticWebApps", null, null, DisplayName = "StaticWebApps with no audience and no issuer specified.")]
        [DataRow("AppService", null, null, DisplayName = "AppService with no audience and no issuer specified.")]
        [DataRow("Simulator", null, null, DisplayName = "Simulator with no audience and no issuer specified.")]
        [DataRow("AzureAD", "aud-xxx", "issuer-xxx", DisplayName = "AzureAD with both audience and issuer specified.")]
        public void EnsureCorrectConfigGenerationWithDifferentAuthenticationProviders(
            string authenticationProvider,
            string? audience,
            string? issuer)
        {
            InitOptions options = new(
                databaseType: DatabaseType.mssql,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: false,
                hostMode: HostModeType.Production,
                corsOrigin: null,
                authenticationProvider: authenticationProvider,
                audience: audience,
                issuer: issuer,
                config: TEST_RUNTIME_CONFIG_FILE);

            _basicRuntimeConfig =
            @"{" +
                @"""$schema"": """ + DAB_DRAFT_SCHEMA_TEST_PATH + @"""" + "," +
                @"""data-source"": {
                    ""database-type"": ""mssql"",
                    ""connection-string"": ""testconnectionstring"",
                    ""options"":{
                        ""set-session-context"": false
                    }
                },
                ""entities"": {}
            }";

            // Adding runtime settings to the above basic config
            string expectedRuntimeConfig = AddPropertiesToJson(
                _basicRuntimeConfig,
                GetDefaultTestRuntimeSettingString(
                    authenticationProvider: authenticationProvider,
                    audience: audience,
                    issuer: issuer));
            RunTest(options, expectedRuntimeConfig);
        }

        /// <summary>
        /// Test to verify that error is thrown when user tries to
        /// initialize a config with a file name that already exists
        /// but with different case.
        /// </summary>
        [TestMethod]
        public void EnsureFailureReInitializingExistingConfigWithDifferentCase()
        {
            // Should PASS, new file is being created
            InitOptions initOptionsWithAllLowerCaseFileName = GetSampleInitOptionsWithFileName(TEST_RUNTIME_CONFIG_FILE);
            Assert.AreEqual(true, ConfigGenerator.TryGenerateConfig(initOptionsWithAllLowerCaseFileName));

            // same file with all uppercase letters
            InitOptions initOptionsWithAllUpperCaseFileName = GetSampleInitOptionsWithFileName(TEST_RUNTIME_CONFIG_FILE.ToUpper());
            // Platform Dependent
            // Windows,MacOs: Should FAIL - File Exists is Case insensitive
            // Unix: Should PASS - File Exists is Case sensitive
            Assert.AreEqual(
                expected: PlatformID.Unix.Equals(Environment.OSVersion.Platform) ? true : false,
                actual: ConfigGenerator.TryGenerateConfig(initOptionsWithAllUpperCaseFileName));
        }

        /// <summary>
        /// Call ConfigGenerator.TryCreateRuntimeConfig and verify json result.
        /// </summary>
        /// <param name="options">InitOptions.</param>
        /// <param name="expectedRuntimeConfig">Expected json string output.</param>
        private static void RunTest(InitOptions options, string expectedRuntimeConfig)
        {
            string runtimeConfigJson;
            Assert.IsTrue(ConfigGenerator.TryCreateRuntimeConfig(options, out runtimeConfigJson));

            JObject expectedJson = JObject.Parse(expectedRuntimeConfig);
            JObject actualJson = JObject.Parse(runtimeConfigJson);

            Assert.IsTrue(JToken.DeepEquals(expectedJson, actualJson));
        }

        /// <summary>
        /// Returns an InitOptions object with sample database and connection-string
        /// for a specified fileName.
        /// </summary>
        /// <param name="fileName">Name of the config file.</param>
        private static InitOptions GetSampleInitOptionsWithFileName(string fileName)
        {
            InitOptions options = new(
                databaseType: DatabaseType.mssql,
                connectionString: "testconnectionstring",
                cosmosNoSqlDatabase: null,
                cosmosNoSqlContainer: null,
                graphQLSchemaPath: null,
                setSessionContext: false,
                hostMode: HostModeType.Production,
                corsOrigin: new List<string>() { },
                authenticationProvider: EasyAuthType.StaticWebApps.ToString(),
                config: fileName);

            return options;
        }

        /// <summary>
        /// Removes the generated configuration file after each test
        /// to avoid file name conflicts on subsequent test runs because the
        /// file is statically named.
        /// </summary>
        [TestCleanup]
        public void CleanUp()
        {
            if (File.Exists(TEST_RUNTIME_CONFIG_FILE))
            {
                File.Delete(TEST_RUNTIME_CONFIG_FILE);
            }
        }
    }
}
