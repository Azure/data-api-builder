// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Configuration;

[TestClass]
public class AzureLogAnalyticsConfigurationTests
{
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
