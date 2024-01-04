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
        [DataRow(true, "", false, DisplayName = "Add Telemetry with empty connection string should fail.")]
        [DataRow(true, "InstrumentationKey=00000000-0000-0000-0000-000000000000", true, DisplayName = "Add Telemetry with valid connection string")]
        [DataRow(false, "InstrumentationKey=00000000-0000-0000-0000-000000000000", true, DisplayName = "Add Telemetry but disabled")]
        public void TestAddApplicationInsightsTelemetry(bool isTelemetryEnabled, string appInsightsConnString, bool expectSuccess)
        {
            MockFileSystem fileSystem = FileSystemUtils.ProvisionMockFileSystem();
            string configPath = "test-app-insights-config.json";
            fileSystem.AddFile(configPath, new MockFileData(INITIAL_CONFIG));

            // Initial State
            Assert.IsTrue(fileSystem.FileExists(configPath));
            Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(fileSystem.File.ReadAllText(configPath), out RuntimeConfig? config));
            Assert.IsNotNull(config);
            Assert.IsNull(config.Runtime!.Telemetry);

            // Add Telemetry
            bool isSuccess = ConfigGenerator.TryAddTelemetry(
                new AddTelemetryOptions(isTelemetryEnabled, appInsightsConnString, configPath),
                new FileSystemRuntimeConfigLoader(fileSystem),
                fileSystem);

            // Assert after adding telemetry
            Assert.AreEqual(expectSuccess, isSuccess);
            if (expectSuccess)
            {
                Assert.IsTrue(fileSystem.FileExists(configPath));
                Assert.IsTrue(RuntimeConfigLoader.TryParseConfig(fileSystem.File.ReadAllText(configPath), out config));
                Assert.IsNotNull(config);
                Assert.IsNotNull(config.Runtime!.Telemetry);
                Assert.IsNotNull(config.Runtime!.Telemetry!.ApplicationInsights);
                Assert.AreEqual(isTelemetryEnabled, config.Runtime!.Telemetry!.ApplicationInsights!.Enabled);
                Assert.AreEqual(appInsightsConnString, config.Runtime!.Telemetry!.ApplicationInsights!.ConnectionString);
            }
        }
    }
}
