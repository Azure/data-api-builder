// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests
{
    /// <summary>
    /// Tests for verifying the functionality of adding OpenTelemetry to the config file.
    /// </summary>
    [TestClass]
    public class AddOpenTelemetryTests
    {
        public string RUNTIME_SECTION_WITH_OPEN_TELEMETRY_SECTION = GenerateRuntimeSection(TELEMETRY_SECTION_WITH_OPEN_TELEMETRY);
        public string RUNTIME_SECTION_WITH_EMPTY_TELEMETRY_SECTION = GenerateRuntimeSection(EMPTY_TELEMETRY_SECTION);
        [TestInitialize]
        public void TestInitialize()
        {
            ILoggerFactory loggerFactory = TestLoggerSupport.ProvisionLoggerFactory();

            ConfigGenerator.SetLoggerForCliConfigGenerator(loggerFactory.CreateLogger<ConfigGenerator>());
            Utils.SetCliUtilsLogger(loggerFactory.CreateLogger<Utils>());
        }

        /// <summary>
        /// Testing to check OpenTelemetry options are correctly added to the config.
        /// Verifying scenarios such as enabling/disabling OpenTelemetry and providing a valid/empty endpoint.
        /// </summary>
        [DataTestMethod]
        [DataRow(CliBool.True, "", false, DisplayName = "Fail to add OpenTelemetry with empty endpoint.")]
        [DataRow(CliBool.True, "http://localhost:4317", true, DisplayName = "Successfully adds OpenTelemetry with valid endpoint")]
        [DataRow(CliBool.False, "http://localhost:4317", true, DisplayName = "Successfully adds OpenTelemetry but disabled")]
        public void TestAddOpenTelemetry(CliBool isTelemetryEnabled, string endpoint, bool expectSuccess)
        {
            MockFileSystem fileSystem = FileSystemUtils.ProvisionMockFileSystem();
            string configPath = "test-opentelemetry-config.json";
            fileSystem.AddFile(configPath, new MockFileData(INITIAL_CONFIG));

            // Initial State
            Assert.IsTrue(fileSystem.FileExists(configPath));
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(fileSystem.File.ReadAllText(configPath), out RuntimeConfig? config));
            Assert.IsNotNull(config);
            Assert.IsNotNull(config.Runtime);
            Assert.IsNull(config.Runtime.Telemetry);

            // Add OpenTelemetry
            bool isSuccess = ConfigGenerator.TryAddTelemetry(
                new AddTelemetryOptions(openTelemetryEndpoint: endpoint, openTelemetryEnabled: isTelemetryEnabled, config: configPath),
                new FileSystemRuntimeConfigLoader(fileSystem),
                fileSystem);

            // Assert after adding OpenTelemetry
            Assert.AreEqual(expectSuccess, isSuccess);
            if (expectSuccess)
            {
                Assert.IsTrue(fileSystem.FileExists(configPath));
                Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(fileSystem.File.ReadAllText(configPath), out config));
                Assert.IsNotNull(config);
                Assert.IsNotNull(config.Runtime);
                Assert.IsNotNull(config.Runtime.Telemetry);
                TelemetryOptions telemetryOptions = config.Runtime.Telemetry;
                Assert.IsNotNull(telemetryOptions.OpenTelemetry);
                Assert.AreEqual(isTelemetryEnabled is CliBool.True ? true : false, telemetryOptions.OpenTelemetry.Enabled);
                Assert.AreEqual(endpoint, telemetryOptions.OpenTelemetry.Endpoint);
            }
        }

        /// <summary>
        /// Test to verify when Telemetry section is present in the config
        /// It should add OpenTelemetry if telemetry section is empty
        /// or overwrite the existing OpenTelemetry with the given OpenTelemetry options.
        /// </summary>
        [DataTestMethod]
        [DataRow(true, DisplayName = "Add OpenTelemetry when telemetry section is empty.")]
        [DataRow(false, DisplayName = "Overwrite OpenTelemetry when telemetry section already exists.")]
        public void TestAddOpenTelemetryWhenTelemetryAlreadyExists(bool isEmptyTelemetry)
        {
            MockFileSystem fileSystem = FileSystemUtils.ProvisionMockFileSystem();
            string configPath = "test-opentelemetry-config.json";
            string runtimeSection = isEmptyTelemetry ? RUNTIME_SECTION_WITH_EMPTY_TELEMETRY_SECTION : RUNTIME_SECTION_WITH_OPEN_TELEMETRY_SECTION;
            string configData = $"{{{SAMPLE_SCHEMA_DATA_SOURCE},{runtimeSection}}}";
            fileSystem.AddFile(configPath, new MockFileData(configData));

            // Initial State
            Assert.IsTrue(fileSystem.FileExists(configPath));
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(fileSystem.File.ReadAllText(configPath), out RuntimeConfig? config));
            Assert.IsNotNull(config);
            Assert.IsNotNull(config.Runtime);
            Assert.IsNotNull(config.Runtime.Telemetry);

            if (isEmptyTelemetry)
            {
                Assert.IsNull(config.Runtime.Telemetry.OpenTelemetry);
            }
            else
            {
                Assert.IsNotNull(config.Runtime.Telemetry.OpenTelemetry);
                Assert.AreEqual(true, config.Runtime.Telemetry.OpenTelemetry.Enabled);
                Assert.AreEqual("http://localhost:4317", config.Runtime.Telemetry.OpenTelemetry.Endpoint);
            }

            // Add OpenTelemetry
            bool isSuccess = ConfigGenerator.TryAddTelemetry(
                new AddTelemetryOptions(openTelemetryEndpoint: "http://localhost:4318", openTelemetryEnabled: CliBool.False, config: configPath),
                new FileSystemRuntimeConfigLoader(fileSystem),
                fileSystem);

            // Assert after adding OpenTelemetry
            Assert.IsTrue(isSuccess);
            Assert.IsTrue(fileSystem.FileExists(configPath));
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(fileSystem.File.ReadAllText(configPath), out config));
            Assert.IsNotNull(config);
            Assert.IsNotNull(config.Runtime);
            Assert.IsNotNull(config.Runtime.Telemetry);
            Assert.IsNotNull(config.Runtime.Telemetry.OpenTelemetry);
            Assert.IsFalse(config.Runtime.Telemetry.OpenTelemetry.Enabled);
            Assert.AreEqual("http://localhost:4318", config.Runtime.Telemetry.OpenTelemetry.Endpoint);
        }

        /// <summary>
        /// Generates a JSON string representing a runtime section of the config, with a customizable telemetry section.
        /// </summary>
        private static string GenerateRuntimeSection(string telemetrySection)
        {
            return $@"
                ""runtime"": {{
                    ""rest"": {{
                        ""path"": ""/api"",
                        ""enabled"": false
                    }},
                    ""graphql"": {{
                        ""path"": ""/graphql"",
                        ""enabled"": false,
                        ""allow-introspection"": true
                    }},
                    ""host"": {{
                        ""mode"": ""development"",
                        ""cors"": {{
                            ""origins"": [],
                            ""allow-credentials"": false
                        }},
                        ""authentication"": {{
                            ""provider"": ""StaticWebApps""
                        }}
                    }},
                    {telemetrySection}
                }},
                ""entities"": {{}}";
        }

        /// <summary>
        /// Represents a JSON string for the telemetry section of the config, with Application Insights enabled and a specified connection string.
        /// </summary>
        private const string TELEMETRY_SECTION_WITH_OPEN_TELEMETRY = @"
            ""telemetry"": {
                ""open-telemetry"": {
                    ""enabled"": true,
                    ""endpoint"": ""http://localhost:4317""
                }
            }";

        /// <summary>
        /// Represents a JSON string for the empty telemetry section of the config.
        /// </summary>
        private const string EMPTY_TELEMETRY_SECTION = @"
            ""telemetry"": {}";
    }
}
