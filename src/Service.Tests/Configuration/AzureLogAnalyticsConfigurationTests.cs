// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.Tests.Configuration.ConfigurationTests;

namespace Azure.DataApiBuilder.Service.Tests.Configuration;

[TestClass, TestCategory(TestCategory.MSSQL)]
public class AzureLogAnalyticsConfigurationTests
{
    private const string CONFIG_WITH_TELEMETRY = "dab-azure-log-analytics-test-config.json";
    private const string CONFIG_WITHOUT_TELEMETRY = "dab-no-azure-log-analytics-test-config.json";
    private static RuntimeConfig _configuration;

    /// <summary>
    /// Creates runtime config file with specified Azure Log Analytics telemetry options.
    /// </summary>
    /// <param name="configFileName">Name of the config file to be created.</param>
    /// <param name="isTelemetryEnabled">Whether Azure Log Analytics telemetry is enabled or not.</param>
    /// <param name="workspaceId">Azure Log Analytics workspace ID.</param>
    /// <param name="logType">Custom log table name.</param>
    /// <param name="flushIntervalSeconds">Flush interval in seconds.</param>
    public static void SetUpAzureLogAnalyticsInConfig(string configFileName, bool isTelemetryEnabled, string workspaceId, string dcrId = "test-dcr-id", string dceEndpoint = "test-dce-endpoint", string logType = "DabLogs", int flushIntervalSeconds = 5)
    {
        DataSource dataSource = new(DatabaseType.MSSQL,
            GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

        _configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions: new(), restOptions: new());

        AzureLogAnalyticsAuthOptions authOptions = new(workspaceId, dcrId, dceEndpoint);
        AzureLogAnalyticsOptions azureLogAnalyticsOptions = new(isTelemetryEnabled, authOptions, logType, flushIntervalSeconds);
        TelemetryOptions _testTelemetryOptions = new(AzureLogAnalytics: azureLogAnalyticsOptions);
        _configuration = _configuration with { Runtime = _configuration.Runtime with { Telemetry = _testTelemetryOptions } };

        File.WriteAllText(configFileName, _configuration.ToJson());
    }

    /// <summary>
    /// Cleans up the test environment by deleting the runtime config with telemetry options.
    /// </summary>
    [TestCleanup]
    public void CleanUpTelemetryConfig()
    {
        if (File.Exists(CONFIG_WITH_TELEMETRY))
        {
            File.Delete(CONFIG_WITH_TELEMETRY);
        }

        if (File.Exists(CONFIG_WITHOUT_TELEMETRY))
        {
            File.Delete(CONFIG_WITHOUT_TELEMETRY);
        }

    }
    /// <summary>
    /// Tests if the services are correctly enabled for Open Telemetry.
    /// </summary>
    /// NOTE: This tests are still not finished, they will be completed in the next PR when the connection to Azure Log Analytics is completed
    [TestMethod]
    [Ignore]
    public void TestOpenTelemetryServicesEnabled()
    {
        // Arrange
        SetUpAzureLogAnalyticsInConfig(CONFIG_WITH_TELEMETRY, true, "http://localhost:4317");

        string[] args = new[]
        {
            $"--ConfigFileName={CONFIG_WITH_TELEMETRY}"
        };
        using TestServer server = new(Program.CreateWebHostBuilder(args));
    }

    /// <summary>
    /// Tests if the services are correctly disabled for Open Telemetry.
    /// </summary>
    /// NOTE: This tests are still not finished, they will be completed in the next PR when the connection to Azure Log Analytics is completed
    [TestMethod]
    [Ignore]
    public void TestOpenTelemetryServicesDisabled()
    {
        // Arrange
        SetUpAzureLogAnalyticsInConfig(CONFIG_WITHOUT_TELEMETRY, false, null, null, null, null);

        string[] args = new[]
        {
            $"--ConfigFileName={CONFIG_WITHOUT_TELEMETRY}"
        };
        using TestServer server = new(Program.CreateWebHostBuilder(args));
    }

    /// <summary>
    /// Test that Azure Log Analytics options serialize only user-provided properties.
    /// </summary>
    [TestMethod]
    public void TestAzureLogAnalyticsOptionsSerializationWithUserProvidedFlags()
    {
        // Arrange - Create config with only explicitly provided auth options
        DataSource dataSource = new(DatabaseType.MSSQL,
            GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

        RuntimeConfig config = InitMinimalRuntimeConfig(dataSource, graphqlOptions: new(), restOptions: new());

        // Only provide auth options, other properties should use defaults but not be serialized
        AzureLogAnalyticsAuthOptions authOptions = new("test-workspace-id", "test-dcr-id", "test-dce-endpoint");
        AzureLogAnalyticsOptions azureLogAnalyticsOptions = new(Auth: authOptions);
        TelemetryOptions telemetryOptions = new(AzureLogAnalytics: azureLogAnalyticsOptions);
        config = config with { Runtime = config.Runtime with { Telemetry = telemetryOptions } };

        // Act - Serialize to JSON
        string json = config.ToJson();

        // Assert - Should only contain auth properties, not enabled/log-type/flush-interval-seconds
        // Check within the azure-log-analytics section specifically
        Assert.IsTrue(json.Contains("\"auth\""), "Auth options should be included in serialized JSON");
        Assert.IsTrue(json.Contains("\"azure-log-analytics\""), "Azure log analytics section should exist");
        
        // Extract just the azure-log-analytics section for more precise checks
        int startIndex = json.IndexOf("\"azure-log-analytics\"");
        int openBrace = json.IndexOf('{', startIndex);
        int closeBrace = json.IndexOf('}', openBrace);
        string azureLogAnalyticsSection = json.Substring(openBrace, closeBrace - openBrace + 1);
        
        Assert.IsFalse(azureLogAnalyticsSection.Contains("\"enabled\""), "Enabled should not be included when using default value");
        Assert.IsFalse(azureLogAnalyticsSection.Contains("\"log-type\""), "Log-type should not be included when using default value");
        Assert.IsFalse(azureLogAnalyticsSection.Contains("\"flush-interval-seconds\""), "Flush-interval-seconds should not be included when using default value");
    }

    /// <summary>
    /// Test that Azure Log Analytics options serialize all properties when explicitly provided.
    /// </summary>
    [TestMethod]
    public void TestAzureLogAnalyticsOptionsSerializationWithAllPropertiesProvided()
    {
        // Arrange - Create config with all properties explicitly provided
        DataSource dataSource = new(DatabaseType.MSSQL,
            GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

        RuntimeConfig config = InitMinimalRuntimeConfig(dataSource, graphqlOptions: new(), restOptions: new());

        // Provide all properties explicitly
        AzureLogAnalyticsAuthOptions authOptions = new("test-workspace-id", "test-dcr-id", "test-dce-endpoint");
        AzureLogAnalyticsOptions azureLogAnalyticsOptions = new(true, authOptions, "CustomLogType", 10);
        TelemetryOptions telemetryOptions = new(AzureLogAnalytics: azureLogAnalyticsOptions);
        config = config with { Runtime = config.Runtime with { Telemetry = telemetryOptions } };

        // Act - Serialize to JSON
        string json = config.ToJson();

        // Assert - Should contain all properties
        Assert.IsTrue(json.Contains("\"enabled\""), "Enabled should be included when explicitly provided");
        Assert.IsTrue(json.Contains("\"auth\""), "Auth options should be included");
        Assert.IsTrue(json.Contains("\"log-type\""), "Log-type should be included when explicitly provided");
        Assert.IsTrue(json.Contains("\"flush-interval-seconds\""), "Flush-interval-seconds should be included when explicitly provided");
        Assert.IsTrue(json.Contains("\"CustomLogType\""), "Custom log type value should be present");
    }
}
