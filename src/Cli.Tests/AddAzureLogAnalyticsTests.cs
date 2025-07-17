// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests
{
    /// <summary>
    /// Tests for verifying the functionality of adding AzureLogAnalytics to the config file.
    /// </summary>
    [TestClass]
    public class AddAzureLogAnalyticsTests
    {
        public static string RUNTIME_SECTION_WITH_AZURE_LOG_ANALYTICS_SECTION = GenerateRuntimeSection(TELEMETRY_SECTION_WITH_AZURE_LOG_ANALYTICS);
        public static string RUNTIME_SECTION_WITH_EMPTY_TELEMETRY_SECTION = GenerateRuntimeSection(EMPTY_TELEMETRY_SECTION);
        public static string RUNTIME_SECTION_WITH_EMPTY_AUTH_SECTION = GenerateRuntimeSection(EMPTY_AUTH_TELEMETRY_SECTION);
        [TestInitialize]
        public void TestInitialize()
        {
            ILoggerFactory loggerFactory = TestLoggerSupport.ProvisionLoggerFactory();

            ConfigGenerator.SetLoggerForCliConfigGenerator(loggerFactory.CreateLogger<ConfigGenerator>());
            Utils.SetCliUtilsLogger(loggerFactory.CreateLogger<Utils>());
        }

        /// <summary>
        /// Testing to check AzureLogAnalytics options are correctly added to the config.
        /// Verifying scenarios such as enabling/disabling AzureLogAnalytics and providing a valid/empty endpoint.
        /// </summary>
        [DataTestMethod]
        [DataRow(CliBool.True, "", "", "", false, DisplayName = "Fail to add AzureLogAnalytics with empty auth properties.")]
        [DataRow(CliBool.True, "workspaceId", "dcrImmutableId", "dceEndpoint", true, DisplayName = "Successfully adds AzureLogAnalytics with valid endpoint")]
        [DataRow(CliBool.False, "workspaceId", "dcrImmutableId", "dceEndpoint", true, DisplayName = "Successfully adds AzureLogAnalytics but disabled")]
        public void TestAddAzureLogAnalytics(CliBool isTelemetryEnabled, string workspaceId, string dcrImmutableId, string dceEndpoint, bool expectSuccess)
        {
            MockFileSystem fileSystem = FileSystemUtils.ProvisionMockFileSystem();
            string configPath = "test-azureloganalytics-config.json";
            fileSystem.AddFile(configPath, new MockFileData(INITIAL_CONFIG));

            // Initial State
            Assert.IsTrue(fileSystem.FileExists(configPath));
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(fileSystem.File.ReadAllText(configPath), out RuntimeConfig? config));
            Assert.IsNotNull(config);
            Assert.IsNotNull(config.Runtime);
            Assert.IsNull(config.Runtime.Telemetry);

            // Add AzureLogAnalytics
            bool isSuccess = ConfigGenerator.TryAddTelemetry(
                new AddTelemetryOptions(
                    azureLogAnalyticsEnabled: isTelemetryEnabled,
                    azureLogAnalyticsWorkspaceId: workspaceId,
                    azureLogAnalyticsDcrImmutableId: dcrImmutableId,
                    azureLogAnalyticsDceEndpoint: dceEndpoint,
                    config: configPath),
                new FileSystemRuntimeConfigLoader(fileSystem),
                fileSystem);

            // Assert after adding AzureLogAnalytics
            Assert.AreEqual(expectSuccess, isSuccess);
            if (expectSuccess)
            {
                Assert.IsTrue(fileSystem.FileExists(configPath));
                Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(fileSystem.File.ReadAllText(configPath), out config));
                Assert.IsNotNull(config);
                Assert.IsNotNull(config.Runtime);
                Assert.IsNotNull(config.Runtime.Telemetry);
                TelemetryOptions telemetryOptions = config.Runtime.Telemetry;
                Assert.IsNotNull(telemetryOptions.AzureLogAnalytics);
                Assert.AreEqual(isTelemetryEnabled is CliBool.True ? true : false, telemetryOptions.AzureLogAnalytics.Enabled);
                Assert.IsNotNull(telemetryOptions.AzureLogAnalytics.Auth);
                Assert.AreEqual(workspaceId, telemetryOptions.AzureLogAnalytics.Auth.WorkspaceId);
                Assert.AreEqual(dcrImmutableId, telemetryOptions.AzureLogAnalytics.Auth.DcrImmutableId);
                Assert.AreEqual(dceEndpoint, telemetryOptions.AzureLogAnalytics.Auth.DceEndpoint);
            }
        }

        /// <summary>
        /// Test to verify when Telemetry section is present in the config
        /// It should add AzureLogAnalytics if telemetry section is empty
        /// or overwrite the existing AzureLogAnalytics with the given AzureLogAnalytics options.
        /// </summary>
        [DataTestMethod]
        [DataRow(true, DisplayName = "Add AzureLogAnalytics when telemetry section is empty.")]
        [DataRow(false, DisplayName = "Overwrite AzureLogAnalytics when telemetry section already exists.")]
        public void TestAddAzureLogAnalyticsWhenTelemetryAlreadyExists(bool isEmptyTelemetry)
        {
            MockFileSystem fileSystem = FileSystemUtils.ProvisionMockFileSystem();
            string configPath = "test-azureloganalytics-config.json";
            string runtimeSection = isEmptyTelemetry ? RUNTIME_SECTION_WITH_EMPTY_TELEMETRY_SECTION : RUNTIME_SECTION_WITH_AZURE_LOG_ANALYTICS_SECTION;
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
                Assert.IsNull(config.Runtime.Telemetry.AzureLogAnalytics);
            }
            else
            {
                Assert.IsNotNull(config.Runtime.Telemetry.AzureLogAnalytics);
                Assert.AreEqual(true, config.Runtime.Telemetry.AzureLogAnalytics.Enabled);
                Assert.IsNotNull(config.Runtime.Telemetry.AzureLogAnalytics.Auth);
                Assert.AreEqual("workspaceId", config.Runtime.Telemetry.AzureLogAnalytics.Auth.WorkspaceId);
                Assert.AreEqual("dcrImmutableId", config.Runtime.Telemetry.AzureLogAnalytics.Auth.DcrImmutableId);
                Assert.AreEqual("dceEndpoint", config.Runtime.Telemetry.AzureLogAnalytics.Auth.DceEndpoint);
            }

            // Add AzureLogAnalytics
            bool isSuccess = ConfigGenerator.TryAddTelemetry(
                new AddTelemetryOptions(
                    azureLogAnalyticsEnabled: CliBool.False,
                    azureLogAnalyticsWorkspaceId: "newWorkspaceId",
                    azureLogAnalyticsDcrImmutableId: "newDcrImmutableId",
                    azureLogAnalyticsDceEndpoint: "newDceEndpoint",
                    config: configPath),
                new FileSystemRuntimeConfigLoader(fileSystem),
                fileSystem);

            // Assert after adding AzureLogAnalytics
            Assert.IsTrue(isSuccess);
            Assert.IsTrue(fileSystem.FileExists(configPath));
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(fileSystem.File.ReadAllText(configPath), out config));
            Assert.IsNotNull(config);
            Assert.IsNotNull(config.Runtime);
            Assert.IsNotNull(config.Runtime.Telemetry);
            Assert.IsNotNull(config.Runtime.Telemetry.AzureLogAnalytics);
            Assert.IsFalse(config.Runtime.Telemetry.AzureLogAnalytics.Enabled);
            Assert.IsNotNull(config.Runtime.Telemetry.AzureLogAnalytics.Auth);
            Assert.AreEqual("newWorkspaceId", config.Runtime.Telemetry.AzureLogAnalytics.Auth.WorkspaceId);
            Assert.AreEqual("newDcrImmutableId", config.Runtime.Telemetry.AzureLogAnalytics.Auth.DcrImmutableId);
            Assert.AreEqual("newDceEndpoint", config.Runtime.Telemetry.AzureLogAnalytics.Auth.DceEndpoint);
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
        private const string TELEMETRY_SECTION_WITH_AZURE_LOG_ANALYTICS = @"
            ""telemetry"": {
                ""azure-log-analytics"": {
                    ""enabled"": true,
                    ""auth"": {
                        ""workspace-id"": ""workspaceId"",
                        ""dcr-immutable-id"": ""dcrImmutableId"",
                        ""dce-endpoint"": ""dceEndpoint""
                    }
                }
            }";

        /// <summary>
        /// Represents a JSON string for the empty telemetry section of the config.
        /// </summary>
        private const string EMPTY_TELEMETRY_SECTION = @"
            ""telemetry"": {}";

        /// <summary>
        /// Represents a JSON string for the empty telemetry section of the config.
        /// </summary>
        private const string EMPTY_AUTH_TELEMETRY_SECTION = @"
            ""telemetry"": {
                ""azure-log-analytics"": {
                    ""enabled"": true,
                    ""auth"": {}
                }
            }";
    }
}
