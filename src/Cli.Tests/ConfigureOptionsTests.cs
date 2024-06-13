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
    }
}
