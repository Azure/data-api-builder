// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests.TelemetryTests
{
    /// <summary>
    /// Tests for verifying the functionality of adding telemetry to the config file.
    /// </summary>
    [TestClass]
    public class AddTelemetryTests
        : VerifyBase
    {
        public const string CONFIG_PATH = "test-telemetry-config.json";
        public string RUNTIME_SECTION_WITH_EMPTY_TELEMETRY_SECTION = GenerateRuntimeSection(EMPTY_TELEMETRY_SECTION);

        [TestInitialize]
        public void TestInitialize()
        {
            ILoggerFactory loggerFactory = TestLoggerSupport.ProvisionLoggerFactory();

            SetLoggerForCliConfigGenerator(loggerFactory.CreateLogger<ConfigGenerator>());
            SetCliUtilsLogger(loggerFactory.CreateLogger<Utils>());
        }

        /// <summary>
        /// Testing to check telemetry options are correctly added to the config.
        /// Verifying scenarios such as enabling/disabling telemetry and providing a valid/empty connection string.
        /// </summary>
        [TestMethod]
        public void TestAddTelemetryBase(AddTelemetryOptions addTelemetryOptions, bool expectSuccess, out RuntimeConfig runtimeConfig)
        {
            MockFileSystem fileSystem = FileSystemUtils.ProvisionMockFileSystem();
            fileSystem.AddFile(CONFIG_PATH, new MockFileData(INITIAL_CONFIG));

            // Initial State
            Assert.IsTrue(fileSystem.FileExists(CONFIG_PATH));
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(fileSystem.File.ReadAllText(CONFIG_PATH), out RuntimeConfig? config));
            Assert.IsNotNull(config);
            Assert.IsNotNull(config.Runtime);
            Assert.IsNull(config.Runtime.Telemetry);

            // Add Telemetry
            bool isSuccess = ConfigGenerator.TryAddTelemetry(
                addTelemetryOptions,
                new FileSystemRuntimeConfigLoader(fileSystem),
                fileSystem);

            runtimeConfig = config;
            // Assert after adding telemetry
            Assert.AreEqual(expectSuccess, isSuccess);
            if (expectSuccess)
            {
                Assert.IsTrue(fileSystem.FileExists(CONFIG_PATH));
                Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(fileSystem.File.ReadAllText(CONFIG_PATH), out config));
                Assert.IsNotNull(config);
                Assert.IsNotNull(config.Runtime);
                Assert.IsNotNull(config.Runtime.Telemetry);
            }
        }

        /// <summary>
        /// Test to verify when Telemetry section is present in the config
        /// It should add application insights telemetry if telemetry section is empty
        /// or overwrite the existing app insights telemetry with the given app insights telemetry options.
        /// </summary>
        [DataTestMethod]
        [DataRow(true, DisplayName = "Add AppInsights Telemetry when telemetry section is empty.")]
        [DataRow(false, DisplayName = "Overwrite AppInsights Telemetry when telemetry section already exists.")]
        public void TestSetupAddTelemetryWhenTelemetryAlreadyExists(bool isEmptyTelemetry, string runtimeSectionWithTelemetry, TelemetryOption telemetryOption)
        {
            ArgumentNullException.ThrowIfNull(runtimeSectionWithTelemetry);
            MockFileSystem fileSystem = FileSystemUtils.ProvisionMockFileSystem();
            string runtimeSection = isEmptyTelemetry ? RUNTIME_SECTION_WITH_EMPTY_TELEMETRY_SECTION : runtimeSectionWithTelemetry;
            string configData = $"{{{SAMPLE_SCHEMA_DATA_SOURCE},{runtimeSection}}}";
            fileSystem.AddFile(CONFIG_PATH, new MockFileData(configData));

            // Initial State
            Assert.IsTrue(fileSystem.FileExists(CONFIG_PATH));
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(fileSystem.File.ReadAllText(CONFIG_PATH), out RuntimeConfig? config));
            Assert.IsNotNull(config);
            Assert.IsNotNull(config.Runtime);
            Assert.IsNotNull(config.Runtime.Telemetry);

            if (isEmptyTelemetry)
            {
                switch (telemetryOption)
                {
                    case TelemetryOption.ApplicationInsights:
                        Assert.IsNull(config.Runtime.Telemetry.ApplicationInsights);
                        break;

                    case TelemetryOption.OpenTelemetry:
                        Assert.IsNull(config.Runtime.Telemetry.OpenTelemetry);
                        break;

                    case TelemetryOption.AzureLogAnalytics:
                        Assert.IsNull(config.Runtime.Telemetry.AzureLogAnalytics);
                        break;

                    default:
                        Assert.Fail("Invalid Option was chosen");
                        break;
                }
            }
            else
            {
                Assert.IsNotNull(config.Runtime.Telemetry.ApplicationInsights);
                Assert.AreEqual(true, config.Runtime.Telemetry.ApplicationInsights.Enabled);
                Assert.AreEqual("InstrumentationKey=00000000-0000-0000-0000-000000000000", config.Runtime.Telemetry.ApplicationInsights.ConnectionString);
            }

            // Add Telemetry
            bool isSuccess = ConfigGenerator.TryAddTelemetry(
                new AddTelemetryOptions(appInsightsConnString: "InstrumentationKey=11111-1111-111-11-1", appInsightsEnabled: CliBool.False, config: CONFIG_PATH),
                new FileSystemRuntimeConfigLoader(fileSystem),
                fileSystem);

            // Assert after adding telemetry
            Assert.IsTrue(isSuccess);
            Assert.IsTrue(fileSystem.FileExists(CONFIG_PATH));
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(fileSystem.File.ReadAllText(CONFIG_PATH), out config));
            Assert.IsNotNull(config);
            Assert.IsNotNull(config.Runtime);
            Assert.IsNotNull(config.Runtime.Telemetry);
            Assert.IsNotNull(config.Runtime.Telemetry.ApplicationInsights);
            Assert.IsFalse(config.Runtime.Telemetry.ApplicationInsights.Enabled);
            Assert.AreEqual("InstrumentationKey=11111-1111-111-11-1", config.Runtime.Telemetry.ApplicationInsights.ConnectionString);
        }

        /// <summary>
        /// Generates a JSON string representing a runtime section of the config, with a customizable telemetry section.
        /// </summary>
        protected static string GenerateRuntimeSection(string telemetrySection)
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
        /// Represents a JSON string for the empty telemetry section of the config.
        /// </summary>
        private const string EMPTY_TELEMETRY_SECTION = @"
            ""telemetry"": {}";

        enum TelemetryOption
        {
            ApplicationInsights,
            OpenTelemetry,
            AzureLogAnalytics
        }
    }
}
