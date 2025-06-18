// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
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

    [TestMethod]
    public void TestAzureLogAnalyticsJsonDeserialization()
    {
        // Arrange
        string jsonConfig = @"{
            ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
            ""data-source"": {
                ""database-type"": ""mssql"",
                ""connection-string"": ""Server=localhost;Database=test;Integrated Security=true;""
            },
            ""runtime"": {
                ""telemetry"": {
                    ""azure-log-analytics"": {
                        ""enabled"": true,
                        ""auth"": {
                            ""workspace-id"": ""test-workspace-id"",
                            ""dcr-immutable-id"": ""test-dcr-id"",
                            ""dce-endpoint"": ""test-dce-endpoint""
                        },
                        ""log-type"": ""CustomLogs"",
                        ""flush-interval-seconds"": 10
                    }
                }
            },
            ""entities"": {}
        }";

        // Act
        bool success = RuntimeConfigLoader.TryParseConfig(jsonConfig, out RuntimeConfig config);

        // Assert
        Assert.IsTrue(success, "Configuration should be parsed successfully");
        Assert.IsNotNull(config, "Config should not be null");
        Assert.IsNotNull(config.Runtime, "Runtime should not be null");
        Assert.IsNotNull(config.Runtime.Telemetry, "Telemetry should not be null");
        Assert.IsNotNull(config.Runtime.Telemetry.AzureLogAnalytics, "AzureLogAnalytics should not be null");

        AzureLogAnalyticsOptions azureLogAnalytics = config.Runtime.Telemetry.AzureLogAnalytics;
        Assert.IsTrue(azureLogAnalytics.Enabled, "AzureLogAnalytics should be enabled");
        Assert.IsNotNull(azureLogAnalytics.Auth, "Auth should not be null");
        Assert.AreEqual("test-workspace-id", azureLogAnalytics.Auth.WorkspaceId, "WorkspaceId should match");
        Assert.AreEqual("test-dcr-id", azureLogAnalytics.Auth.DcrImmutableId, "DcrImmutableId should match");
        Assert.AreEqual("test-dce-endpoint", azureLogAnalytics.Auth.DceEndpoint, "DceEndpoint should match");
        Assert.AreEqual("CustomLogs", azureLogAnalytics.LogType, "LogType should match");
        Assert.AreEqual(10, azureLogAnalytics.FlushIntervalSeconds, "FlushIntervalSeconds should match");
    }

    [TestMethod]
    public void TestAzureLogAnalyticsJsonDeserializationWithDefaults()
    {
        // Arrange
        string jsonConfig = @"{
            ""$schema"": ""https://github.com/Azure/data-api-builder/releases/download/vmajor.minor.patch/dab.draft.schema.json"",
            ""data-source"": {
                ""database-type"": ""mssql"",
                ""connection-string"": ""Server=localhost;Database=test;Integrated Security=true;""
            },
            ""runtime"": {
                ""telemetry"": {
                    ""azure-log-analytics"": {
                        ""enabled"": false,
                        ""auth"": {
                            ""workspace-id"": ""test-workspace-id""
                        }
                    }
                }
            },
            ""entities"": {}
        }";

        // Act
        bool success = RuntimeConfigLoader.TryParseConfig(jsonConfig, out RuntimeConfig config);

        // Assert
        Assert.IsTrue(success, "Configuration should be parsed successfully");
        Assert.IsNotNull(config, "Config should not be null");
        Assert.IsNotNull(config.Runtime, "Runtime should not be null");
        Assert.IsNotNull(config.Runtime.Telemetry, "Telemetry should not be null");
        Assert.IsNotNull(config.Runtime.Telemetry.AzureLogAnalytics, "AzureLogAnalytics should not be null");

        AzureLogAnalyticsOptions azureLogAnalytics = config.Runtime.Telemetry.AzureLogAnalytics;
        Assert.IsFalse(azureLogAnalytics.Enabled, "AzureLogAnalytics should be disabled");
        Assert.IsNotNull(azureLogAnalytics.Auth, "Auth should not be null");
        Assert.AreEqual("test-workspace-id", azureLogAnalytics.Auth.WorkspaceId, "WorkspaceId should match");
        Assert.IsNull(azureLogAnalytics.Auth.DcrImmutableId, "DcrImmutableId should be null (default)");
        Assert.IsNull(azureLogAnalytics.Auth.DceEndpoint, "DceEndpoint should be null (default)");
        Assert.AreEqual("DabLogs", azureLogAnalytics.LogType, "LogType should have default value");
        Assert.AreEqual(5, azureLogAnalytics.FlushIntervalSeconds, "FlushIntervalSeconds should have default value");
    }

    [TestMethod]
    public void TestAzureLogAnalyticsJsonSerialization()
    {
        // Arrange
        AzureLogAnalyticsAuthOptions authOptions = new("test-workspace-id", "test-dcr-id", "test-dce-endpoint");
        AzureLogAnalyticsOptions azureLogAnalyticsOptions = new(true, authOptions, "CustomLogs", 10);
        TelemetryOptions telemetryOptions = new(AzureLogAnalytics: azureLogAnalyticsOptions);

        // Act
        string json = JsonSerializer.Serialize(telemetryOptions, RuntimeConfigLoader.GetSerializationOptions());

        // Debug output
        System.Console.WriteLine($"Generated JSON: {json}");

        // Assert
        Assert.IsTrue(json.Contains("\"azure-log-analytics\""), "JSON should contain azure-log-analytics property");
        Assert.IsTrue(json.Contains("\"enabled\":true") || json.Contains("\"enabled\": true"), "JSON should contain enabled property");
        Assert.IsTrue(json.Contains("\"workspace-id\":\"test-workspace-id\"") || json.Contains("\"workspace-id\": \"test-workspace-id\""), "JSON should contain workspace-id");
        Assert.IsTrue(json.Contains("\"dcr-immutable-id\":\"test-dcr-id\"") || json.Contains("\"dcr-immutable-id\": \"test-dcr-id\""), "JSON should contain dcr-immutable-id");
        Assert.IsTrue(json.Contains("\"dce-endpoint\":\"test-dce-endpoint\"") || json.Contains("\"dce-endpoint\": \"test-dce-endpoint\""), "JSON should contain dce-endpoint");
        Assert.IsTrue(json.Contains("\"log-type\":\"CustomLogs\"") || json.Contains("\"log-type\": \"CustomLogs\""), "JSON should contain log-type");
        Assert.IsTrue(json.Contains("\"flush-interval-seconds\":10") || json.Contains("\"flush-interval-seconds\": 10"), "JSON should contain flush-interval-seconds");
    }
}
