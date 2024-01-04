// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Cli.Tests
{
    /// <summary>
    /// Tests for Adding new Entity.
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

        [DataTestMethod]
        [DataRow(true, "", DisplayName = "Add Telemetry with empty connection string")]
        [DataRow(true, "InstrumentationKey=00000000-0000-0000-0000-000000000000", DisplayName = "Add Telemetry with valid connection string")]
        [DataRow(false, "InstrumentationKey=00000000-0000-0000-0000-000000000000", DisplayName = "Add Telemetry but disabled")]
        public void AddTelemetryTest(bool isTelemetryEnabled, string appInsightsConnString)
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
            Assert.IsTrue(isSuccess);
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
