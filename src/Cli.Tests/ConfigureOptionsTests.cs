// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests
{
    /// <summary>
    /// Validates that `dab configure [options]` configures settings with the user-provided options.
    /// </summary>
    [TestClass]
    public class ConfigureOptionsTests : VerifyBase
    {
        private MockFileSystem? _fileSystem;
        private FileSystemRuntimeConfigLoader? _runtimeConfigLoader;
        private const string TEST_RUNTIME_CONFIG_FILE = "test-update-runtime-setting.json";

        [TestInitialize]
        public void TestInitialize()
        {
            _fileSystem = FileSystemUtils.ProvisionMockFileSystem();

            _runtimeConfigLoader = new FileSystemRuntimeConfigLoader(_fileSystem);

            ILoggerFactory loggerFactory = TestLoggerSupport.ProvisionLoggerFactory();

            SetLoggerForCliConfigGenerator(loggerFactory.CreateLogger<ConfigGenerator>());
            SetCliUtilsLogger(loggerFactory.CreateLogger<Utils>());
        }

        /// <summary>
        /// Validates that if depth-limit is not provided in `dab configure` options, then The config file must not change.
        /// Example: if depth-limit is not provided in the config, it should not be added.
        /// if { "depth-limit" : null } is provided in the config, it should not be removed.
        /// Also, if { "depth-limit" : -1 } is provided in the config, it should not be removed.
        /// </summary>
        [DataTestMethod]
        [DataRow(null, false, DisplayName = "Config: 'depth-limit' property not defined, should not be added.")]
        [DataRow(null, true, DisplayName = "Config: 'depth-limit' is null. It should not be removed.")]
        [DataRow(-1, true, DisplayName = "Config: 'depth-limit' is -1, should remain as is without change.")]
        public void TestNoUpdateOnGraphQLDepthLimitInRuntimeSettings(object? depthLimit, bool isDepthLimitProvidedInConfig)
        {
            string depthLimitSection = "";
            if (isDepthLimitProvidedInConfig)
            {
                if (depthLimit == null)
                {
                    depthLimitSection = $@"""depth-limit"": null";
                }
                else
                {
                    depthLimitSection = $@"""depth-limit"": {depthLimit}";
                }
            }

            string initialConfig = TestHelper.GenerateConfigWithGivenDepthLimit(depthLimitSection);

            // Arrange
            _fileSystem!.AddFile(TEST_RUNTIME_CONFIG_FILE, initialConfig);

            // Act: Run Configure with no options
            ConfigureOptions options = new(
                config: TEST_RUNTIME_CONFIG_FILE
            );

            Assert.IsTrue(_fileSystem!.File.Exists(TEST_RUNTIME_CONFIG_FILE));
            Assert.IsTrue(TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!));

            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);

            // Assert that INITIAL_CONFIG is same as the updated config
            if (isDepthLimitProvidedInConfig)
            {
                Assert.IsTrue(updatedConfig.Contains(depthLimitSection));
            }
            else
            {
                Assert.IsTrue(!updatedConfig.Contains("depth-limit"));
            }

            // Assert that INITIAL_CONFIG is same as the updated config
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(initialConfig), JObject.Parse(updatedConfig)));
        }

        /// <summary>
        /// Tests that running "dab configure --runtime.graphql.depth-limit 8" on a config with no depth-limit previously specified, results
        /// in runtime.graphql.depthlimit property being set with the value 8.
        [TestMethod]
        public void TestAddDepthLimitForGraphQL()
        {
            // Arrange
            _fileSystem!.AddFile(TEST_RUNTIME_CONFIG_FILE, new MockFileData(INITIAL_CONFIG));
            int maxDepthLimit = 8;

            Assert.IsTrue(_fileSystem!.File.Exists(TEST_RUNTIME_CONFIG_FILE));

            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(INITIAL_CONFIG, out RuntimeConfig? config));
            Assert.IsNull(config.Runtime!.GraphQL!.DepthLimit);

            // Act: Attmepts to Add Depth Limit
            ConfigureOptions options = new(
                depthLimit: maxDepthLimit,
                config: TEST_RUNTIME_CONFIG_FILE
            );
            Assert.IsTrue(TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!));

            // Assert: Validate the Depth Limit is added
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out config));
            Assert.IsNotNull(config.Runtime?.GraphQL?.DepthLimit);
            Assert.AreEqual(maxDepthLimit, config.Runtime.GraphQL.DepthLimit);
        }

        /// <summary>
        /// Test to update the current depth limit for GraphQL and removal the depth limit using -1.
        /// When runtime.graphql.depth-limit has an initial value of 8.
        /// validates that "dab configure --runtime.graphql.depth-limit {value}" sets the expected depth limit.
        /// </summary>
        [DataTestMethod]
        [DataRow(20, DisplayName = "Update current depth limit for GraphQL.")]
        [DataRow(-1, DisplayName = "Remove depth limit from GraphQL by setting depth limit to -1.")]
        public void TestUpdateDepthLimitForGraphQL(int? newDepthLimit)
        {
            int currentDepthLimit = 8;

            // Arrange
            RuntimeConfigLoader.TryParseConfig(INITIAL_CONFIG, out RuntimeConfig? config);
            Assert.IsNotNull(config);
            config = config with
            {
                Runtime = config.Runtime! with
                {
                    GraphQL = config.Runtime.GraphQL! with
                    {
                        DepthLimit = currentDepthLimit
                    }
                }
            };

            _fileSystem!.AddFile(TEST_RUNTIME_CONFIG_FILE, new MockFileData(config.ToJson()));
            ConfigureOptions options = new(
                depthLimit: newDepthLimit,
                config: TEST_RUNTIME_CONFIG_FILE
            );

            // Act: Update Depth Limit
            Assert.IsTrue(TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!));

            // Assert: Validate the Depth Limit is updated
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out config));

            Assert.AreEqual(newDepthLimit, config.Runtime?.GraphQL?.DepthLimit);
        }

        /// <summary>
        /// Tests the update of the database type in the runtime config.
        /// dab configure `--data-source.database-type {dbType}`
        /// This method verifies that the database type can be updated to various valid values, including different cases,
        /// and ensures that the config file is correctly modified and parsed after the update.
        /// </summary>
        [DataTestMethod]
        [DataRow("mssql", DisplayName = "Update the database type to MSSQL")]
        [DataRow("MSSql", DisplayName = "Update the database type to MSSQL with different case")]
        [DataRow("postgresql", DisplayName = "Update the database type to PostgreSQL")]
        [DataRow("cosmosdb_nosql", DisplayName = "Update the database type to CosmosDB_NoSQL")]
        [DataRow("cosmosdb_postgresql", DisplayName = "Update the database type to CosmosDB_PGSQL")]
        [DataRow("mysql", DisplayName = "Update the database type to MySQL")]
        public void TestDatabaseTypeUpdate(string dbType)
        {
            // Arrange
            SetupFileSystemWithInitialConfig(INITIAL_CONFIG);

            ConfigureOptions options = new(
                dataSourceDatabaseType: dbType,
                config: TEST_RUNTIME_CONFIG_FILE
            );

            // Act
            bool isSuccess = TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!);

            // Assert
            Assert.IsTrue(isSuccess);
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out RuntimeConfig? config));
            Assert.IsNotNull(config.Runtime);
            Assert.AreEqual(config.DataSource.DatabaseType, Enum.Parse<DatabaseType>(dbType, ignoreCase: true));
        }

        /// <summary>
        /// Tests the update of the database type from CosmosDB_NoSQL to MSSQL in the runtime config.
        /// This method verifies that the database type can be changed from CosmosDB_NoSQL to MSSQL and that the 
        /// specific MSSQL option 'set-session-context' is correctly added to the configuration and the specific
        /// cosmosDB options are removed.
        /// Command: dab configure --data-source.database-type mssql --data-source.options.set-session-context true 
        /// </summary>
        [TestMethod]
        public void TestDatabaseTypeUpdateCosmosDB_NoSQLToMSSQL()
        {
            // Arrange
            SetupFileSystemWithInitialConfig(INITIAL_COSMOSDB_NOSQL_CONFIG);

            ConfigureOptions options = new(
                dataSourceDatabaseType: "mssql",
                dataSourceOptionsSetSessionContext: true,
                config: TEST_RUNTIME_CONFIG_FILE
            );

            // Act
            bool isSuccess = TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!);

            // Assert
            Assert.IsTrue(isSuccess);
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out RuntimeConfig? config));
            Assert.IsNotNull(config.Runtime);
            Assert.AreEqual(config.DataSource.DatabaseType, DatabaseType.MSSQL);
            Assert.AreEqual(config.DataSource.Options!.GetValueOrDefault("set-session-context", false), true);
            Assert.IsFalse(config.DataSource.Options!.ContainsKey("database"));
            Assert.IsFalse(config.DataSource.Options!.ContainsKey("container"));
            Assert.IsFalse(config.DataSource.Options!.ContainsKey("schema"));
        }

        /// <summary>
        /// Tests the update of the database type from MSSQL to CosmosDB_NoSQL in the runtime config.
        /// This method verifies that the database type can be changed from MSSQL to CosmosDB_NoSQL and that the 
        /// specific CosmosDB_NoSQL options such as database, container, and schema are correctly added to the config.
        /// Command: dab configure --data-source.database-type cosmosdb_nosql
        /// --data-source.options.database testdb --data-source.options.container testcontainer --data-source.options.schema testschema.gql
        /// </summary>
        [TestMethod]
        public void TestDatabaseTypeUpdateMSSQLToCosmosDB_NoSQL()
        {
            // Arrange
            SetupFileSystemWithInitialConfig(INITIAL_CONFIG);

            ConfigureOptions options = new(
                dataSourceDatabaseType: "cosmosdb_nosql",
                dataSourceOptionsDatabase: "testdb",
                dataSourceOptionsContainer: "testcontainer",
                dataSourceOptionsSchema: "testschema.gql",
                config: TEST_RUNTIME_CONFIG_FILE
            );

            // Act
            bool isSuccess = TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!);

            // Assert
            Assert.IsTrue(isSuccess);
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out RuntimeConfig? config));
            Assert.IsNotNull(config.Runtime);
            Assert.AreEqual(config.DataSource.DatabaseType, DatabaseType.CosmosDB_NoSQL);
            Assert.AreEqual(config.DataSource.Options!.GetValueOrDefault("database"), "testdb");
            Assert.AreEqual(config.DataSource.Options!.GetValueOrDefault("container"), "testcontainer");
            Assert.AreEqual(config.DataSource.Options!.GetValueOrDefault("schema"), "testschema.gql");
        }

        /// <summary>
        /// Tests configuring database type with an invalid database type value.
        /// This method verifies that when an invalid database type is provided,
        /// the runtime config is not updated. It ensures that the method correctly identifies and handles
        /// invalid database types by returning false.
        /// </summary>
        [TestMethod]
        public void TestConfiguringInvalidDatabaseType()
        {
            // Arrange
            SetupFileSystemWithInitialConfig(INITIAL_CONFIG);

            ConfigureOptions options = new(
                dataSourceDatabaseType: "invalid",
                config: TEST_RUNTIME_CONFIG_FILE
            );

            // Act
            bool isSuccess = TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!);

            // Assert
            Assert.IsFalse(isSuccess);
        }

        /// <summary>
        /// Tests the failure scenario when attempting to add CosmosDB-specific options to an MSSQL database configuration.
        /// This method verifies that the configuration process correctly fails when options such as database, container,
        /// and schema, which are specific to CosmosDB_NoSQL, are provided for an MSSQL database type.
        /// </summary>
        [TestMethod]
        public void TestFailureWhenAddingCosmosDbOptionsToMSSQLDatabase()
        {
            // Arrange
            SetupFileSystemWithInitialConfig(INITIAL_CONFIG);

            ConfigureOptions options = new(
                dataSourceOptionsDatabase: "testdb",
                dataSourceOptionsContainer: "testcontainer",
                dataSourceOptionsSchema: "testschema.gql",
                config: TEST_RUNTIME_CONFIG_FILE
            );

            // Act
            bool isSuccess = TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!);

            // Assert
            Assert.IsFalse(isSuccess);
        }

        /// <summary>
        /// Tests the failure scenario when attempting to add the 'set-session-context' option to a MySQL database configuration.
        /// This method verifies that the configuration process correctly fails when the 'set-session-context' option,
        /// which is specific to MSSQL/DWSQL, is provided for a MySQL database type.
        /// </summary>
        [TestMethod]
        public void TestFailureWhenAddingSetSessionContextToMySQLDatabase()
        {
            // Arrange
            SetupFileSystemWithInitialConfig(INITIAL_CONFIG);

            ConfigureOptions options = new(
                dataSourceDatabaseType: "mysql",
                dataSourceOptionsSetSessionContext: true,
                config: TEST_RUNTIME_CONFIG_FILE
            );

            // Act
            bool isSuccess = TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!);

            // Assert
            Assert.IsFalse(isSuccess);
        }

        /// <summary>
        /// Sets up the mock file system with an initial configuration file.
        /// This method adds a config file to the mock file system and verifies its existence.
        /// It also attempts to parse the config file to ensure it is valid.
        /// </summary>
        /// <param name="jsonConfig">The config file data as a json string.</param>
        private void SetupFileSystemWithInitialConfig(string jsonConfig)
        {
            _fileSystem!.AddFile(TEST_RUNTIME_CONFIG_FILE, new MockFileData(jsonConfig));

            Assert.IsTrue(_fileSystem!.File.Exists(TEST_RUNTIME_CONFIG_FILE));

            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(jsonConfig, out RuntimeConfig? config));
            Assert.IsNotNull(config.Runtime);
        }
    }
}
