// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using static Azure.DataApiBuilder.Service.Tests.Configuration.ConfigurationTests;

namespace Azure.DataApiBuilder.Service.Tests.Configuration.Telemetry;

/// <summary>
/// Contains tests for OpenTelemetry functionality.
/// </summary>
[TestClass, TestCategory(TestCategory.MSSQL)]
public class OpenTelemetryTests
{
    public TestContext TestContext { get; set; }

    private const string CONFIG_WITH_TELEMETRY = "dab-open-telemetry-test-config.json";
    private const string CONFIG_WITHOUT_TELEMETRY = "dab-no-open-telemetry-test-config.json";
    private static RuntimeConfig _configuration;

    /// <summary>
    /// This is a helper function that creates runtime config file with specified telemetry options.
    /// </summary>
    /// <param name="configFileName">Name of the config file to be created.</param>
    /// <param name="isTelemetryEnabled">Whether telemetry is enabled or not.</param>
    /// <param name="telemetryConnectionString">Telemetry connection string.</param>
    public static void SetUpTelemetryInConfig(string configFileName, bool isOtelEnabled, string otelEndpoint, string otelHeaders, OtlpExportProtocol? otlpExportProtocol)
    {
        DataSource dataSource = new(DatabaseType.MSSQL,
            GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

        _configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions: new(), restOptions: new(), mcpOptions: new());

        TelemetryOptions _testTelemetryOptions = new(OpenTelemetry: new OpenTelemetryOptions(isOtelEnabled, otelEndpoint, otelHeaders, otlpExportProtocol, "TestServiceName"));
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
    [TestMethod]
    public void TestOpenTelemetryServicesEnabled()
    {
        // Arrange
        SetUpTelemetryInConfig(CONFIG_WITH_TELEMETRY, true, "http://localhost:4317", "key=key", OtlpExportProtocol.Grpc);

        string[] args = new[]
        {
            $"--ConfigFileName={CONFIG_WITH_TELEMETRY}"
        };
        using TestServer server = new(Program.CreateWebHostBuilder(args));

        // Additional assertions to check if OpenTelemetry is enabled correctly in services
        IServiceProvider serviceProvider = server.Services;
        TracerProvider tracerProvider = serviceProvider.GetService<TracerProvider>();
        MeterProvider meterProvider = serviceProvider.GetService<MeterProvider>();

        // If tracerProvider and meterProvider are not null, OTEL is enabled
        Assert.IsNotNull(tracerProvider, "TracerProvider should be registered.");
        Assert.IsNotNull(meterProvider, "MeterProvider should be registered.");
    }

    /// <summary>
    /// Tests if the services are correctly disabled for Open Telemetry.
    /// </summary>
    [TestMethod]
    public void TestOpenTelemetryServicesDisabled()
    {
        // Arrange
        SetUpTelemetryInConfig(CONFIG_WITHOUT_TELEMETRY, false, null, null, null);

        string[] args = new[]
        {
            $"--ConfigFileName={CONFIG_WITHOUT_TELEMETRY}"
        };
        using TestServer server = new(Program.CreateWebHostBuilder(args));

        // Additional assertions to check if OpenTelemetry is disabled correctly in services
        IServiceProvider serviceProvider = server.Services;
        TracerProvider tracerProvider = serviceProvider.GetService<TracerProvider>();
        MeterProvider meterProvider = serviceProvider.GetService<MeterProvider>();

        // If tracerProvider and meterProvider are null, OTEL is disabled
        Assert.IsNull(tracerProvider, "TracerProvider should not be registered.");
        Assert.IsNull(meterProvider, "MeterProvider should not be registered.");
    }
}
