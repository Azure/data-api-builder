// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests
{
    /// <summary>
    /// Tests for verifying the functionality of adding telemetry to the config file.
    /// </summary>
    [TestClass]
    public class AddTelemetryTests
        : VerifyBase
    {
        public string RUNTIME_SECTION_WITH_APP_INSIGHTS_TELEMETRY_SECTION = GenerateRuntimeSection(TELEMETRY_SECTION_WITH_APP_INSIGHTS);
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
        [DataTestMethod]
        [DataRow(CliBool.True, "", false, DisplayName = "Fail to add telemetry with empty app-insights connection string.")]
        [DataRow(CliBool.True, "InstrumentationKey=00000000-0000-0000-0000-000000000000", true, DisplayName = "Successfully adds telemetry with valid connection string")]
        [DataRow(CliBool.False, "InstrumentationKey=00000000-0000-0000-0000-000000000000", true, DisplayName = "Successfully adds telemetry but disabled")]
        public void TestAddApplicationInsightsTelemetry(CliBool isTelemetryEnabled, string appInsightsConnString, bool expectSuccess)
        {
            MockFileSystem fileSystem = FileSystemUtils.ProvisionMockFileSystem();
            string configPath = "test-app-insights-config.json";
            fileSystem.AddFile(configPath, new MockFileData(INITIAL_CONFIG));

            // Initial State
            Assert.IsTrue(fileSystem.FileExists(configPath));
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(fileSystem.File.ReadAllText(configPath), out RuntimeConfig? config));
            Assert.IsNotNull(config);
            Assert.IsNotNull(config.Runtime);
            Assert.IsNull(config.Runtime.Telemetry);

            // Add Telemetry
            bool isSuccess = ConfigGenerator.TryAddTelemetry(
                new AddTelemetryOptions(appInsightsConnString: appInsightsConnString, appInsightsEnabled: isTelemetryEnabled, config: configPath),
                new FileSystemRuntimeConfigLoader(fileSystem),
                fileSystem);

            // Assert after adding telemetry
            Assert.AreEqual(expectSuccess, isSuccess);
            if (expectSuccess)
            {
                Assert.IsTrue(fileSystem.FileExists(configPath));
                Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(fileSystem.File.ReadAllText(configPath), out config));
                Assert.IsNotNull(config);
                Assert.IsNotNull(config.Runtime);
                Assert.IsNotNull(config.Runtime.Telemetry);
                TelemetryOptions telemetryOptions = config.Runtime.Telemetry;
                Assert.IsNotNull(telemetryOptions.ApplicationInsights);
                Assert.AreEqual(isTelemetryEnabled is CliBool.True ? true : false, telemetryOptions.ApplicationInsights.Enabled);
                Assert.AreEqual(appInsightsConnString, telemetryOptions.ApplicationInsights.ConnectionString);
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
        public void TestAddAppInsightsTelemetryWhenTelemetryAlreadyExists(bool isEmptyTelemetry)
        {
            MockFileSystem fileSystem = FileSystemUtils.ProvisionMockFileSystem();
            string configPath = "test-app-insights-config.json";
            string runtimeSection = isEmptyTelemetry ? RUNTIME_SECTION_WITH_EMPTY_TELEMETRY_SECTION : RUNTIME_SECTION_WITH_APP_INSIGHTS_TELEMETRY_SECTION;
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
                Assert.IsNull(config.Runtime.Telemetry.ApplicationInsights);
            }
            else
            {
                Assert.IsNotNull(config.Runtime.Telemetry.ApplicationInsights);
                Assert.AreEqual(true, config.Runtime.Telemetry.ApplicationInsights.Enabled);
                Assert.AreEqual("InstrumentationKey=00000000-0000-0000-0000-000000000000", config.Runtime.Telemetry.ApplicationInsights.ConnectionString);
            }

            // Add Telemetry
            bool isSuccess = ConfigGenerator.TryAddTelemetry(
                new AddTelemetryOptions(appInsightsConnString: "InstrumentationKey=11111-1111-111-11-1", appInsightsEnabled: CliBool.False, config: configPath),
                new FileSystemRuntimeConfigLoader(fileSystem),
                fileSystem);

            // Assert after adding telemetry
            Assert.IsTrue(isSuccess);
            Assert.IsTrue(fileSystem.FileExists(configPath));
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(fileSystem.File.ReadAllText(configPath), out config));
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
        private const string TELEMETRY_SECTION_WITH_APP_INSIGHTS = @"
            ""telemetry"": {
                ""application-insights"": {
                    ""enabled"": true,
                    ""connection-string"": ""InstrumentationKey=00000000-0000-0000-0000-000000000000""
                }
            }";

        /// <summary>
        /// Represents a JSON string for the empty telemetry section of the config.
        /// </summary>
        private const string EMPTY_TELEMETRY_SECTION = @"
            ""telemetry"": {}";
    }

}
