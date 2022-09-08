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
        /// Test the simple init config for mssql database. PG and MySQL should be similar.
        /// There is no need for a separate test.
        /// </summary>
        [TestMethod]
        public void MssqlDatabase()
        {
            InitOptions options = new(
                databaseType: DatabaseType.mssql,
                connectionString: "testconnectionstring",
                cosmosDatabase: null,
                cosmosContainer: null,
                graphQLSchemaPath: null,
                hostMode: HostModeType.Development,
                corsOrigin: new List<string>() { "http://localhost:3000", "http://nolocalhost:80" },
                config: _testRuntimeConfig,
                devModeDefaultAuth: "true");

            _basicRuntimeConfig =
            @"{
                ""$schema"": ""dab.draft-01.schema.json"",
                ""data-source"": {
                    ""database-type"": ""mssql"",
                    ""connection-string"": ""testconnectionstring""
                },
                ""mssql"": {
                    ""set-session-context"": true
                },
                ""entities"": {}
            }";

            // Adding runtime settings to the above basic config
            string expectedRuntimeConfig = AddPropertiesToJson(
                _basicRuntimeConfig,
                GetDefaultTestRuntimeSettingString(DatabaseType.mssql,
                    HostModeType.Development,
                    new List<string>() { "http://localhost:3000", "http://nolocalhost:80" },
                    authenticateDevModeRequest: true)
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
                cosmosDatabase: null,
                cosmosContainer: null,
                graphQLSchemaPath: null,
                hostMode: HostModeType.Development,
                corsOrigin: new List<string>() { "http://localhost:3000", "http://nolocalhost:80" },
                config: _testRuntimeConfig,
                devModeDefaultAuth: "false");

            _basicRuntimeConfig =
            @"{
                ""$schema"": ""dab.draft-01.schema.json"",
                ""data-source"": {
                    ""database-type"": ""mssql"",
                    ""connection-string"": """"
                },
                ""mssql"": {
                    ""set-session-context"": true
                },
                ""entities"": {}
            }";

            // Adding runtime settings to the above basic config
            string expectedRuntimeConfig = AddPropertiesToJson(
                _basicRuntimeConfig,
                GetDefaultTestRuntimeSettingString(DatabaseType.mssql,
                    HostModeType.Development,
                    new List<string>() { "http://localhost:3000", "http://nolocalhost:80" },
                    authenticateDevModeRequest: false)
            );
            RunTest(options, expectedRuntimeConfig);
        }

        /// <summary>
        /// Test cosmos db specifc settings like cosmos-database, cosmos-container, cosmos-schema file.
        /// </summary>
        [TestMethod]
        public void CosmosDatabase()
        {
            InitOptions options = new(
                databaseType: DatabaseType.cosmos,
                connectionString: "testconnectionstring",
                cosmosDatabase: "testdb",
                cosmosContainer: "testcontainer",
                graphQLSchemaPath: "schemafile",
                hostMode: HostModeType.Production,
                corsOrigin: null,
                config: _testRuntimeConfig,
                devModeDefaultAuth: null);

            _basicRuntimeConfig = @"{
                ""$schema"": ""dab.draft-01.schema.json"",
                ""data-source"": {
                    ""database-type"": ""cosmos"",
                    ""connection-string"": ""testconnectionstring""
                },
                ""cosmos"": {
                    ""database"": ""testdb"",
                    ""container"": ""testcontainer"",
                    ""schema"": ""schemafile""
                },
                ""entities"": {}
            }";

            // Adding runtime settings to the above basic config
            string expectedRuntimeConfig = AddPropertiesToJson(
                _basicRuntimeConfig,
                GetDefaultTestRuntimeSettingString(DatabaseType.cosmos));
            RunTest(options, expectedRuntimeConfig);
        }

        /// <summary>
        /// Verify that if either database or graphQLSchema is null or empty, we will get error.
        /// </summary>
        [DataRow(null, "testcontainer", "", false, DisplayName = "Both database and schema are either null or empty.")]
        [DataRow("", "testcontainer", "testschema", false, DisplayName = "database is empty.")]
        [DataRow("testDatabase", "testcontainer", "", false, DisplayName = "database is provided, Schema is null.")]
        [DataRow("testDatabase", null, "", false, DisplayName = "database is provided, container and Schema is null/empty.")]
        [DataRow("testDatabase", null, "testSchema", true, DisplayName = "database and schema provided, container is null/empty.")]
        [DataTestMethod]
        public void VerifyRequiredOptionsForCosmosDatabase(
            string? cosmosDatabase,
            string? cosmosContainer,
            string? graphQLSchema,
            bool expectedResult
        )
        {
            InitOptions options = new(
                databaseType: DatabaseType.cosmos,
                connectionString: "testconnectionstring",
                cosmosDatabase: cosmosDatabase,
                cosmosContainer: cosmosContainer,
                graphQLSchemaPath: graphQLSchema,
                hostMode: HostModeType.Production,
                corsOrigin: null,
                config: _testRuntimeConfig,
                devModeDefaultAuth: null
                );

            Assert.AreEqual(expectedResult, ConfigGenerator.TryCreateRuntimeConfig(options, out _));
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
                cosmosDatabase: null,
                cosmosContainer: null,
                graphQLSchemaPath: null,
                hostMode: HostModeType.Development,
                corsOrigin: new List<string>() { },
                config: _testRuntimeConfig,
                devModeDefaultAuth: null);

            // Config generated successfully for the first time.
            Assert.AreEqual(true, ConfigGenerator.TryGenerateConfig(options));

            // Error is thrown because the config file with the same name
            // already exists.
            Assert.AreEqual(false, ConfigGenerator.TryGenerateConfig(options));
        }

        /// <summary>
        /// Test to verify that error is thrown when user tries to
        /// initialize a config with a file name that already exists
        /// but with different case.
        /// </summary>
        [TestMethod]
        public void EnsureFailureReInitializingExistingConfigWithDifferentCase()
        {
            InitOptions initOptionsWithAllLowerCaseFileName = GetSampleInitOptionsWithFileName(_testRuntimeConfig);
            Assert.AreEqual(true, ConfigGenerator.TryGenerateConfig(initOptionsWithAllLowerCaseFileName));

            // Should FAIL - same file is used with different case
            InitOptions initOptionsWithAllUpperCaseFileName = GetSampleInitOptionsWithFileName(_testRuntimeConfig.ToUpper());
            Assert.AreEqual(false, ConfigGenerator.TryGenerateConfig(initOptionsWithAllUpperCaseFileName));
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
                cosmosDatabase: null,
                cosmosContainer: null,
                graphQLSchemaPath: null,
                hostMode: HostModeType.Production,
                corsOrigin: new List<string>() { },
                config: fileName,
                devModeDefaultAuth: null);

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
            if (File.Exists(_testRuntimeConfig))
            {
                File.Delete(_testRuntimeConfig);
            }
        }
    }
}
