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
        /// Validates that `dab configure` is a no-op because the user provided no options to update
        /// the config file. The config file must not change.
        /// </summary>
        [TestMethod]
        public void TestNoUpdateRuntimeSettings()
        {
            // Arrange
            _fileSystem!.AddFile(TEST_RUNTIME_CONFIG_FILE, new MockFileData(INITIAL_CONFIG));

            // Act: Run Configure with no options
            ConfigureOptions options = new(
                depthLimit: null,
                config: TEST_RUNTIME_CONFIG_FILE
            );

            Assert.IsTrue(_fileSystem!.File.Exists(TEST_RUNTIME_CONFIG_FILE));
            Assert.IsTrue(TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!));

            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);

            // Assert that INITIAL_CONFIG is same as the updated config
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(INITIAL_CONFIG), JObject.Parse(updatedConfig)));
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
            Assert.IsNotNull(config.Runtime!.GraphQL!.DepthLimit);
            Assert.AreEqual(maxDepthLimit, config!.Runtime!.GraphQL!.DepthLimit);
        }

        /// <summary>
        /// Test to update the current depth limit for GraphQL and removal the depth limit using -1.
        /// </summary>
        [DataTestMethod]
        [DataRow(20, DisplayName = "Update current depth limit for GraphQL.")]
        [DataRow(-1, DisplayName = "Remove depth limit from GraphQL.")]
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
                    GraphQL = config.Runtime!.GraphQL! with
                    {
                        DepthLimit = currentDepthLimit
                    }
                }
            };

            _fileSystem!.AddFile(TEST_RUNTIME_CONFIG_FILE, new MockFileData(config.ToJson()));

            // Act: Update Depth Limit
            ConfigureOptions options = new(
                depthLimit: newDepthLimit,
                config: TEST_RUNTIME_CONFIG_FILE
            );
            Assert.IsTrue(TryConfigureSettings(options, _runtimeConfigLoader!, _fileSystem!));

            // Assert: Validate the Depth Limit is updated
            string updatedConfig = _fileSystem!.File.ReadAllText(TEST_RUNTIME_CONFIG_FILE);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out config));

            Assert.AreEqual(newDepthLimit, config.Runtime!.GraphQL!.DepthLimit);
        }
    }
}
