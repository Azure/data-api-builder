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
        /// Tests that running "dab configure --runtime.graphql.enabled" on a config with various values results
        /// in runtime. Takes in updated value for graphql.enabled and 
        /// validates whether the runtime config reflects those updated values
        [DataTestMethod]
        [DataRow(false, DisplayName = "Update enabled to be false for GraphQL.")]
        [DataRow(true, DisplayName = "Update enabled to be true for GraphQL.")]
        public void TestUpdateEnabledForGraphQLSettings(bool updatedEnabledValue)
        {
            // Arrange -> all the setup which includes creating options.
            SetupFileSystemWithInitialConfig(INITIAL_CONFIG);

            // Act: Attempts to update enabled flag
            ConfigureOptions options = new(
                runtimeGraphQLEnabled: updatedEnabledValue,
                config: TEST_RUNTIME_CONFIG_FILE
            );
            Assert.IsTrue(TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!));

            // Assert: Validate the Enabled Flag is updated
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out RuntimeConfig? runtimeConfig));
            Assert.IsNotNull(runtimeConfig.Runtime?.GraphQL?.Enabled);
            Assert.AreEqual(updatedEnabledValue, runtimeConfig.Runtime.GraphQL.Enabled);
        }

        /// <summary>
        /// Tests that running "dab configure --runtime.graphql.path" on a config with various values results
        /// in runtime config update. Takes in updated value for graphql.path and 
        /// validates whether the runtime config reflects those updated values
        [DataTestMethod]
        [DataRow("/updatedPath", DisplayName = "Update path to /updatedPath for GraphQL.")]
        [DataRow("/updated_Path", DisplayName = "Ensure underscore is allowed in GraphQL path name.")]
        [DataRow("/updated-Path", DisplayName = "Ensure hyphen is allowed in GraphQL path name.")]
        public void TestUpdatePathForGraphQLSettings(string updatedPathValue)
        {
            // Arrange -> all the setup which includes creating options.
            SetupFileSystemWithInitialConfig(INITIAL_CONFIG);

            // Act: Attempts to update path value
            ConfigureOptions options = new(
                runtimeGraphQLPath: updatedPathValue,
                config: TEST_RUNTIME_CONFIG_FILE
            );
            Assert.IsTrue(TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!));

            // Assert: Validate the Path update is updated
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out RuntimeConfig? runtimeConfig));
            Assert.IsNotNull(runtimeConfig.Runtime?.GraphQL?.Path);
            Assert.AreEqual(updatedPathValue, runtimeConfig.Runtime.GraphQL.Path);
        }

        /// <summary>
        /// Tests that running "dab configure --runtime.graphql.allow-introspection" on a 
        /// config with various values results in runtime config update.
        /// Takes in updated value for graphql.allow-introspection and 
        /// validates whether the runtime config reflects those updated values
        [DataTestMethod]
        [DataRow(false, DisplayName = "Update AllowIntrospection to be false for GraphQL.")]
        [DataRow(true, DisplayName = "Update AllowIntrospection to be true for GraphQL.")]
        public void TestUpdateAllowIntrospectionForGraphQLSettings(bool updatedAllowIntrospectionValue)
        {
            // Arrange -> all the setup which includes creating options.
            SetupFileSystemWithInitialConfig(INITIAL_CONFIG);

            // Act: Attempts to update allow-introspection flag
            ConfigureOptions options = new(
                runtimeGraphQLAllowIntrospection: updatedAllowIntrospectionValue,
                config: TEST_RUNTIME_CONFIG_FILE
            );
            Assert.IsTrue(TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!));

            // Assert: Validate the Allow-Introspection value is updated
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out RuntimeConfig? runtimeConfig));
            Assert.IsNotNull(runtimeConfig.Runtime?.GraphQL?.AllowIntrospection);
            Assert.AreEqual(updatedAllowIntrospectionValue, runtimeConfig.Runtime.GraphQL.AllowIntrospection);
        }

        /// <summary>
        /// Tests that running "dab configure --runtime.graphql.multiple-mutations.create.enabled"
        /// on a config with various values results in runtime config update. 
        /// Takes in updated value for multiple mutations.create.enabled and 
        /// validates whether the runtime config reflects those updated values
        [DataTestMethod]
        [DataRow(false, DisplayName = "Update MultipleMutation.Create.Enabled to be false for GraphQL.")]
        [DataRow(true, DisplayName = "Update MultipleMutation.Create.Enabled to be true for GraphQL.")]
        public void TestUpdateMultipleMutationCreateEnabledForGraphQLSettings(bool updatedMultipleMutationsCreateEnabledValue)
        {
            // Arrange -> all the setup which includes creating options.
            SetupFileSystemWithInitialConfig(INITIAL_CONFIG);

            // Act: Attempts to update multiple-mutations.create.enabled flag
            ConfigureOptions options = new(
                runtimeGraphQLMultipleMutationsCreateEnabled: updatedMultipleMutationsCreateEnabledValue,
                config: TEST_RUNTIME_CONFIG_FILE
            );
            Assert.IsTrue(TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!));

            // Assert: Validate the Multiple-Mutation.Create.Enabled is updated
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out RuntimeConfig? runtimeConfig));
            Assert.IsNotNull(runtimeConfig.Runtime?.GraphQL?.MultipleMutationOptions?.MultipleCreateOptions?.Enabled);
            Assert.AreEqual(updatedMultipleMutationsCreateEnabledValue, runtimeConfig.Runtime.GraphQL.MultipleMutationOptions.MultipleCreateOptions.Enabled);
        }

        /// <summary>
        /// Tests that running "dab configure --runtime.graphql.path" on a config with various values results
        /// in runtime config update. Takes in updatedPath and updated value for allow-introspection and 
        /// validates whether the runtime config reflects those updated values
        [TestMethod]
        public void TestUpdateMultipleParametersForGraphQLSettings()
        {
            // Arrange -> all the setup which includes creating options.
            SetupFileSystemWithInitialConfig(INITIAL_CONFIG);

            bool updatedAllowIntrospectionValue = false;
            string updatedPathValue = "/updatedPath";

            // Act: Attempts to update the path value and allow-introspection flag
            ConfigureOptions options = new(
                runtimeGraphQLPath: updatedPathValue,
                runtimeGraphQLAllowIntrospection: updatedAllowIntrospectionValue,
                config: TEST_RUNTIME_CONFIG_FILE
            );
            Assert.IsTrue(TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!));

            // Assert: Validate the path is updated and allow introspection is updated
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out RuntimeConfig? runtimeConfig));
            Assert.IsNotNull(runtimeConfig.Runtime?.GraphQL?.Path);
            Assert.IsNotNull(runtimeConfig.Runtime?.GraphQL?.AllowIntrospection);
            Assert.AreEqual(updatedPathValue, runtimeConfig.Runtime.GraphQL.Path);
            Assert.AreEqual(updatedAllowIntrospectionValue, runtimeConfig.Runtime.GraphQL.AllowIntrospection);
        }

        /// <summary>
        /// Tests that running "dab configure --runtime.rest.enabled" on a config with various values results
        /// in runtime. Takes in updated value for rest.enabled and 
        /// validates whether the runtime config reflects those updated values
        [DataTestMethod]
        [DataRow(false, DisplayName = "Update enabled to be false for Rest.")]
        [DataRow(true, DisplayName = "Update enabled to be true for Rest.")]
        public void TestUpdateEnabledForRestSettings(bool updatedEnabledValue)
        {
            // Arrange -> all the setup which includes creating options.
            SetupFileSystemWithInitialConfig(INITIAL_CONFIG);

            // Act: Attempts to update enabled flag
            ConfigureOptions options = new(
                runtimeRestEnabled: updatedEnabledValue,
                config: TEST_RUNTIME_CONFIG_FILE
            );
            Assert.IsTrue(TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!));

            // Assert: Validate the Enabled Flag is updated
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out RuntimeConfig? runtimeConfig));
            Assert.IsNotNull(runtimeConfig.Runtime?.Rest?.Enabled);
            Assert.AreEqual(updatedEnabledValue, runtimeConfig.Runtime.Rest.Enabled);
        }

        /// <summary>
        /// Tests that running "dab configure --runtime.rest.path" on a config with various values results
        /// in runtime config update. Takes in updated value for rest.path and 
        /// validates whether the runtime config reflects those updated values
        [DataTestMethod]
        [DataRow("/updatedPath", DisplayName = "Update path to /updatedPath for Rest.")]
        [DataRow("/updated_Path", DisplayName = "Ensure underscore is allowed in Rest path name.")]
        [DataRow("/updated-Path", DisplayName = "Ensure hyphen is allowed in Rest path name.")]
        public void TestUpdatePathForRestSettings(string updatedPathValue)
        {
            // Arrange -> all the setup which includes creating options.
            SetupFileSystemWithInitialConfig(INITIAL_CONFIG);

            // Act: Attempts to update path value
            ConfigureOptions options = new(
                runtimeRestPath: updatedPathValue,
                config: TEST_RUNTIME_CONFIG_FILE
            );
            Assert.IsTrue(TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!));

            // Assert: Validate the Path update is updated
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out RuntimeConfig? runtimeConfig));
            Assert.IsNotNull(runtimeConfig.Runtime?.Rest?.Path);
            Assert.AreEqual(updatedPathValue, runtimeConfig.Runtime.Rest.Path);
        }

        /// <summary>
        /// Tests that running "dab configure --runtime.rest.request-body-strict" on a config with various values results
        /// in runtime config update. Takes in updated value for rest.request-body-strict and 
        /// validates whether the runtime config reflects those updated values
        [DataTestMethod]
        [DataRow(false, DisplayName = "Update request-body-strict to be false for Rest.")]
        [DataRow(true, DisplayName = "Update request-body-strict to be true for Rest.")]
        public void TestUpdateRequestBodyStrictForRestSettings(bool updatedRequestBodyStrictValue)
        {
            // Arrange -> all the setup which includes creating options.
            SetupFileSystemWithInitialConfig(INITIAL_CONFIG);

            // Act: Attempts to update request-body-strict value
            ConfigureOptions options = new(
                runtimeRestRequestBodyStrict: updatedRequestBodyStrictValue,
                config: TEST_RUNTIME_CONFIG_FILE
            );
            Assert.IsTrue(TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!));

            // Assert: Validate the RequestBodyStrict Value is updated
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out RuntimeConfig? runtimeConfig));
            Assert.IsNotNull(runtimeConfig.Runtime?.Rest?.RequestBodyStrict);
            Assert.AreEqual(updatedRequestBodyStrictValue, runtimeConfig.Runtime.Rest.RequestBodyStrict);
        }

        /// <summary>
        /// Tests that running "dab configure --runtime.rest.enabled {value} --runtime.rest.path {value}"
        /// on a config with various values results in runtime config update. 
        /// Takes in updated value for enabled and path and further 
        /// validates whether the runtime config reflects those updated values
        [DataTestMethod]
        [DataRow(false, "/updatedPath", DisplayName = "Update enabled flag and path in Rest runtime settings.")]
        public void TestUpdateMultipleParametersRestSettings(bool updatedEnabledValue, string updatedPathValue)
        {
            // Arrange -> all the setup which includes creating options.
            SetupFileSystemWithInitialConfig(INITIAL_CONFIG);

            // Act: Attempts to update the path value and enabled flag
            ConfigureOptions options = new(
                runtimeRestPath: updatedPathValue,
                runtimeRestEnabled: updatedEnabledValue,
                config: TEST_RUNTIME_CONFIG_FILE
            );
            Assert.IsTrue(TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!));

            // Assert: Validate the path is updated and enabled is updated
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out RuntimeConfig? runtimeConfig));
            Assert.IsNotNull(runtimeConfig.Runtime?.Rest?.Path);
            Assert.IsNotNull(runtimeConfig.Runtime?.Rest?.Enabled);
            Assert.AreEqual(updatedPathValue, runtimeConfig.Runtime.Rest.Path);
            Assert.AreEqual(updatedEnabledValue, runtimeConfig.Runtime.Rest.Enabled);
        }

        /// <summary>
        /// Tests that running "dab configure --runtime.cache.enabled" on a config with various values results
        /// in runtime. Takes in updated value for cache.enabled and 
        /// validates whether the runtime config reflects those updated values
        [DataTestMethod]
        [DataRow(false, DisplayName = "Update enabled to be false for Cache.")]
        [DataRow(true, DisplayName = "Update enabled to be true for Cache.")]
        public void TestUpdateEnabledForCacheSettings(bool updatedEnabledValue)
        {
            // Arrange -> all the setup which includes creating options.
            SetupFileSystemWithInitialConfig(INITIAL_CONFIG);

            // Act: Attempts to update enabled flag
            ConfigureOptions options = new(
                runtimeCacheEnabled: updatedEnabledValue,
                config: TEST_RUNTIME_CONFIG_FILE
            );
            Assert.IsTrue(TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!));

            // Assert: Validate the Enabled Flag is updated
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out RuntimeConfig? runtimeConfig));
            Assert.IsNotNull(runtimeConfig.Runtime?.Cache?.Enabled);
            Assert.AreEqual(updatedEnabledValue, runtimeConfig.Runtime.Cache.Enabled);
        }

        /// <summary>
        /// Tests that running "dab configure --runtime.cache.ttl-seconds" on a config with various values results
        /// in runtime. Takes in updated value for cache.ttl-seconds and 
        /// validates whether the runtime config reflects those updated values
        [DataTestMethod]
        [DataRow(4, DisplayName = "Update in value of ttl for cache.")]
        public void TestUpdateTTLForCacheSettings(int updatedTtlValue)
        {
            // Arrange -> all the setup which includes creating options.
            SetupFileSystemWithInitialConfig(INITIAL_CONFIG);

            // Act: Attempts to update TTL Value
            ConfigureOptions options = new(
                runtimeCacheTtl: updatedTtlValue,
                config: TEST_RUNTIME_CONFIG_FILE
            );
            Assert.IsTrue(TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!));

            // Assert: Validate the TTL Value is updated
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out RuntimeConfig? runtimeConfig));
            Assert.IsNotNull(runtimeConfig.Runtime?.Cache?.TtlSeconds);
            Assert.AreEqual(updatedTtlValue, runtimeConfig.Runtime.Cache.TtlSeconds);
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
