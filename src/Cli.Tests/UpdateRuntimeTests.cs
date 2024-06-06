// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests
{
    /// <summary>
    /// Tests to validate cli commands to update runtime settings.
    /// </summary>
    [TestClass]
    public class UpdateRuntimeTests : VerifyBase
    {
        private MockFileSystem? _fileSystem;
        private FileSystemRuntimeConfigLoader? _runtimeConfigLoader;
        private static string _testConfigFile = "test-update-runtime-setting.json";

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
        /// Test to validate when update runtime setting command is used with no options
        /// no changes are made to the config file.
        /// </summary>
        [TestMethod]
        public void TestNoUpdateRuntimeSettings()
        {
            _fileSystem!.AddFile(_testConfigFile, new MockFileData(INITIAL_CONFIG));

            UpdateRuntimeOptions options = new(
                depthLimit: null,
                config: _testConfigFile
            );

            Assert.IsTrue(_fileSystem!.File.Exists(_testConfigFile));
            Assert.IsTrue(TryUpdateRuntimeSettings(options, _runtimeConfigLoader!, _fileSystem!));

            string updatedConfig = _fileSystem!.File.ReadAllText(_testConfigFile);
            // Assert that INITIAL_CONFIG is same as the updated config
            Assert.IsTrue(JToken.DeepEquals(JObject.Parse(INITIAL_CONFIG), JObject.Parse(updatedConfig)));
        }

        /// <summary>
        /// Test to validate when no limit was specified, it gets added to the config file
        /// using update runtime command. Here we are adding depth limit of 8 for GraphQL.
        /// Once added it should be present in the config file.
        [TestMethod]
        public void TestAddDepthLimitForGraphQL()
        {
            _fileSystem!.AddFile(_testConfigFile, new MockFileData(INITIAL_CONFIG));
            int maxDepthLimit = 8;

            Assert.IsTrue(_fileSystem!.File.Exists(_testConfigFile));

            // Initial State
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(INITIAL_CONFIG, out RuntimeConfig? config));
            Assert.IsNotNull(config);
            Assert.IsNull(config!.Runtime!.GraphQL!.DepthLimit);

            // Add Depth Limit
            UpdateRuntimeOptions options = new(
                depthLimit: maxDepthLimit,
                config: _testConfigFile
            );
            Assert.IsTrue(TryUpdateRuntimeSettings(options, _runtimeConfigLoader!, _fileSystem!));

            // Assert after adding Depth Limit
            string updatedConfig = _fileSystem!.File.ReadAllText(_testConfigFile);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out config));
            Assert.IsNotNull(config);
            Assert.IsNotNull(config!.Runtime!.GraphQL!.DepthLimit);
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

            // Generate Config with Depth Limit
            RuntimeConfigLoader.TryParseConfig(INITIAL_CONFIG, out RuntimeConfig? config);
            Assert.IsNotNull(config);
            config = config with
            {
                Runtime = config!.Runtime! with
                {
                    GraphQL = config!.Runtime!.GraphQL! with
                    {
                        DepthLimit = currentDepthLimit
                    }
                }
            };

            _fileSystem!.AddFile(_testConfigFile, new MockFileData(config.ToJson()));

            // Update Depth Limit
            UpdateRuntimeOptions options = new(
                depthLimit: newDepthLimit,
                config: _testConfigFile
            );
            Assert.IsTrue(TryUpdateRuntimeSettings(options, _runtimeConfigLoader!, _fileSystem!));

            // Assert after adding Depth Limit
            string updatedConfig = _fileSystem!.File.ReadAllText(_testConfigFile);
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(updatedConfig, out config));
            Assert.IsNotNull(config);

            int? expectedDepthLimit = newDepthLimit == -1 ? null : newDepthLimit;
            Assert.AreEqual(expectedDepthLimit, config!.Runtime!.GraphQL!.DepthLimit);

            if (expectedDepthLimit is null)
            {
                Console.WriteLine(JObject.Parse(INITIAL_CONFIG));
                Console.WriteLine(JObject.Parse(updatedConfig));
                Assert.IsTrue(JToken.DeepEquals(JObject.Parse(INITIAL_CONFIG), JObject.Parse(updatedConfig)));
            }
        }
    }
}
