// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Telemetry;
using Azure.Identity;
using Azure.Monitor.Ingestion;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static Azure.DataApiBuilder.Service.Tests.Configuration.ConfigurationTests;

namespace Azure.DataApiBuilder.Service.Tests.Configuration.Telemetry;

/// <summary>
/// Contains tests for Azure Log Analytics functionality.
/// </summary>
[TestClass, TestCategory(TestCategory.MSSQL)]
public class AzureLogAnalyticsTests
{
    public TestContext TestContext { get; set; }

    private const string CONFIG_WITH_TELEMETRY = "dab-azure-log-analytics-test-config.json";
    private const string CONFIG_WITHOUT_TELEMETRY = "dab-no-azure-log-analytics-test-config.json";
    private static RuntimeConfig _configuration;

    /// <summary>
    /// This is a helper function that creates runtime config file with specified telemetry options.
    /// </summary>
    /// <param name="configFileName">Name of the config file to be created.</param>
    /// <param name="isTelemetryEnabled">Whether telemetry is enabled or not.</param>
    /// <param name="telemetryConnectionString">Telemetry connection string.</param>
    public static void SetUpTelemetryInConfig(string configFileName, bool isLogAnalyticsEnabled, string logAnalyticsCustomTable, string logAnalyticsDcrImmutableId, string logAnalyticsDceEndpoint)
    {
        DataSource dataSource = new(DatabaseType.MSSQL,
            GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

        _configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions: new(), restOptions: new());

        TelemetryOptions _testTelemetryOptions = new(AzureLogAnalytics: new AzureLogAnalyticsOptions(isLogAnalyticsEnabled, new AzureLogAnalyticsAuthOptions(logAnalyticsCustomTable, logAnalyticsDcrImmutableId, logAnalyticsDceEndpoint)));
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
    /// Tests if the services are correctly enabled for Azure Log Analytics.
    /// </summary>
    [TestMethod]
    public void TestAzureLogAnalyticsServicesEnabled()
    {
        // Arrange
        SetUpTelemetryInConfig(CONFIG_WITH_TELEMETRY, true, "Custom-Table-Name-Test", "DCR-Immutable-ID-Test", "https://fake.dce.endpoint");

        string[] args = new[]
        {
            $"--ConfigFileName={CONFIG_WITH_TELEMETRY}"
        };
        using TestServer server = new(Program.CreateWebHostBuilder(args));

        // Additional assertions to check if AzureLogAnalytics is enabled correctly in services
        IServiceProvider serviceProvider = server.Services;
        AzureLogAnalyticsCustomLogCollector customLogCollector = (AzureLogAnalyticsCustomLogCollector)serviceProvider.GetService<ICustomLogCollector>();
        AzureLogAnalyticsFlusherService flusherService = serviceProvider.GetService<AzureLogAnalyticsFlusherService>();
        IEnumerable<ILoggerProvider> loggerProvidersServices = serviceProvider.GetServices<ILoggerProvider>();
        AzureLogAnalyticsLoggerProvider loggerProvider = loggerProvidersServices.OfType<AzureLogAnalyticsLoggerProvider>().FirstOrDefault();

        // If customLogCollector, flusherService, and loggerProvider are not null when AzureLogAnalytics is enabled
        Assert.IsNotNull(customLogCollector, "AzureLogAnalyticsCustomLogCollector should be registered.");
        Assert.IsNotNull(flusherService, "AzureLogAnalyticsFlusherService should be registered.");
        Assert.IsNotNull(loggerProvider, "AzureLogAnalyticsLoggerProvider should be registered.");
    }

    /// <summary>
    /// Tests if the logs are flushed correctly when Azure Log Analytics is enabled.
    /// </summary>
    [DataTestMethod]
    [DataRow("Information Test Message", LogLevel.Information)]
    [DataRow("Trace Test Message", LogLevel.Trace)]
    [DataRow("Warning Test Message", LogLevel.Warning)]
    public async Task TestAzureLogAnalyticsFlushServiceSucceed(string message, LogLevel logLevel)
    {
        // Arrange
        CancellationTokenSource tokenSource = new();
        AzureLogAnalyticsOptions azureLogAnalyticsOptions = new(true, new AzureLogAnalyticsAuthOptions("custom-table-name-test", "dcr-immutable-id-test", "https://fake.dce.endpoint"), "DABLogs", 1);
        CustomLogsIngestionClient customClient = new(azureLogAnalyticsOptions.Auth.DceEndpoint);
        AzureLogAnalyticsCustomLogCollector customLogCollector = new();

        ILoggerFactory loggerFactory = new LoggerFactory();
        ILogger<Startup> logger = loggerFactory.CreateLogger<Startup>();
        AzureLogAnalyticsFlusherService flusherService = new(azureLogAnalyticsOptions, customLogCollector, customClient, logger);

        // Act
        await customLogCollector.LogAsync(message, logLevel);

        _ = Task.Run(() => flusherService.StartAsync(tokenSource.Token));

        await Task.Delay(1000);

        // Assert
        AzureLogAnalyticsLogs actualLog = customClient.LogAnalyticsLogs[0];
        Assert.AreEqual(logLevel.ToString(), actualLog.LogLevel);
        Assert.AreEqual(message, actualLog.Message);
    }

    /// <summary>
    /// Tests if the services are correctly disabled for Azure Log Analytics.
    /// </summary>
    [TestMethod]
    public void TestAzureLogAnalyticsServicesDisabled()
    {
        // Arrange
        SetUpTelemetryInConfig(CONFIG_WITHOUT_TELEMETRY, false, null, null, null);

        string[] args = new[]
        {
            $"--ConfigFileName={CONFIG_WITHOUT_TELEMETRY}"
        };
        using TestServer server = new(Program.CreateWebHostBuilder(args));

        // Additional assertions to check if Azure Log Analytics is disabled correctly in services
        IServiceProvider serviceProvider = server.Services;
        AzureLogAnalyticsFlusherService flusherService = serviceProvider.GetService<AzureLogAnalyticsFlusherService>();
        AzureLogAnalyticsLoggerProvider loggerProvider = serviceProvider.GetService<AzureLogAnalyticsLoggerProvider>();

        // If flusherService and loggerProvider are null, Azure Log Analytics is disabled
        Assert.IsNull(flusherService, "AzureLogAnalyticsFlusherService should not be registered.");
        Assert.IsNull(loggerProvider, "AzureLogAnalyticsLoggerProvider should not be registered.");
    }

    /// <summary>
    /// Custom logs ingestion to test that all the logs are being sent correctly to Azure Log Analytics
    /// </summary>
    private class CustomLogsIngestionClient : LogsIngestionClient
    {
        public List<AzureLogAnalyticsLogs> LogAnalyticsLogs { get; } = new();

        public CustomLogsIngestionClient(string dceEndpoint) : base(new Uri(dceEndpoint), new DefaultAzureCredential()) { } // CodeQL [SM05137] DefaultAzureCredential will use Managed Identity if available or fallback to default.

        public async override Task<Response> UploadAsync<T>(string ruleId, string streamName, IEnumerable<T> logs, LogsUploadOptions options = null, CancellationToken cancellationToken = default)
        {
            LogAnalyticsLogs.AddRange(logs.Cast<AzureLogAnalyticsLogs>());

            Response mockResponse = Response.FromValue(Mock.Of<Response>(), Mock.Of<Response>());
            return await Task.FromResult(mockResponse);
        }
    }
}
