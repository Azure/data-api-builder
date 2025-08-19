// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Serilog;
using Serilog.Core;
using static Azure.DataApiBuilder.Service.Tests.Configuration.ConfigurationTests;

namespace Azure.DataApiBuilder.Service.Tests.Configuration.Telemetry;

/// <summary>
/// Contains tests for File Sink functionality.
/// </summary>
[TestClass, TestCategory(TestCategory.MSSQL)]
public class FileSinkTests
{
    public TestContext TestContext { get; set; }

    private const string CONFIG_WITH_TELEMETRY = "dab-file-sink-test-config.json";
    private const string CONFIG_WITHOUT_TELEMETRY = "dab-no-file-sink-test-config.json";
    private static RuntimeConfig _configuration;

    /// <summary>
    /// This is a helper function that creates runtime config file with specified telemetry options.
    /// </summary>
    /// <param name="configFileName">Name of the config file to be created.</param>
    /// <param name="isTelemetryEnabled">Whether telemetry is enabled or not.</param>
    /// <param name="telemetryConnectionString">Telemetry connection string.</param>
    public static void SetUpTelemetryInConfig(string configFileName, bool isFileSinkEnabled, string fileSinkPath)
    {
        DataSource dataSource = new(DatabaseType.MSSQL,
            GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

        _configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions: new(), restOptions: new());

        TelemetryOptions _testTelemetryOptions = new(File: new FileSinkOptions(isFileSinkEnabled, fileSinkPath));
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
    /// Tests if the services are correctly enabled for File Sink.
    /// </summary>
    [TestMethod]
    public void TestFileSinkServicesEnabled()
    {
        // Arrange
        SetUpTelemetryInConfig(CONFIG_WITH_TELEMETRY, true, "/dab-log-test/file-sink-file.txt");

        string[] args = new[]
        {
            $"--ConfigFileName={CONFIG_WITH_TELEMETRY}"
        };
        using TestServer server = new(Program.CreateWebHostBuilder(args));

        // Additional assertions to check if File Sink is enabled correctly in services
        IServiceProvider serviceProvider = server.Services;
        LoggerConfiguration serilogLoggerConfiguration = serviceProvider.GetService<LoggerConfiguration>();
        Logger serilogLogger = serviceProvider.GetService<Logger>();

        // If customLogCollector, flusherService, and loggerProvider are not null when File Sink is enabled
        Assert.IsNotNull(serilogLoggerConfiguration, "LoggerConfiguration for Serilog should be registered.");
        Assert.IsNotNull(serilogLogger, "Logger for Serilog should be registered.");
    }

    /// <summary>
    /// Tests if the logs are flushed to the proper path when File Sink is enabled.
    /// </summary>
    [DataTestMethod]
    [DataRow("/file-sink-test-file.txt")]
    [DataRow("/file-sink-test-file.log")]
    [DataRow("/file-sink-test-file.csv")]
    public void TestFileSinkSucceed(string fileName)
    {
        // Arrange
        SetUpTelemetryInConfig(CONFIG_WITH_TELEMETRY, true, fileName);

        string[] args = new[]
        {
            $"--ConfigFileName={CONFIG_WITH_TELEMETRY}"
        };
        using TestServer server = new(Program.CreateWebHostBuilder(args));

        // Assert
        Assert.IsTrue(File.Exists(fileName));
    }

    /// <summary>
    /// Tests if the services are correctly disabled for File Sink.
    /// </summary>
    [TestMethod]
    public void TestFileSinkServicesDisabled()
    {
        // Arrange
        SetUpTelemetryInConfig(CONFIG_WITHOUT_TELEMETRY, false, null);

        string[] args = new[]
        {
            $"--ConfigFileName={CONFIG_WITHOUT_TELEMETRY}"
        };
        using TestServer server = new(Program.CreateWebHostBuilder(args));

        // Additional assertions to check if File Sink is enabled correctly in services
        IServiceProvider serviceProvider = server.Services;
        LoggerConfiguration serilogLoggerConfiguration = serviceProvider.GetService<LoggerConfiguration>();
        Logger serilogLogger = serviceProvider.GetService<Logger>();

        // If customLogCollector, flusherService, and loggerProvider are not null when File Sink is enabled
        Assert.IsNotNull(serilogLoggerConfiguration, "LoggerConfiguration for Serilog should be registered.");
        Assert.IsNotNull(serilogLogger, "Logger for Serilog should be registered.");
    }
}
