// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Telemetry;
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
    /// Tests that the OTLP exporter honors the httpprotobuf exporter-protocol from the config
    /// by checking that exported traces reach a local HTTP endpoint as protobuf.
    /// </summary>
    [TestMethod]
    public async Task TestOpenTelemetryExporterUsesHttpProtobufProtocol()
    {
        // Arrange
        TcpListener portFinder = new(IPAddress.Loopback, 0);
        portFinder.Start();
        int port = ((IPEndPoint)portFinder.LocalEndpoint).Port;
        portFinder.Stop();

        string otelEndpoint = $"http://localhost:{port}/";
        using HttpListener collector = new();
        collector.Prefixes.Add(otelEndpoint);
        collector.Start();

        SetUpTelemetryInConfig(CONFIG_WITH_TELEMETRY, true, otelEndpoint, "key=key", OtlpExportProtocol.HttpProtobuf);

        string[] args = new[]
        {
            $"--ConfigFileName={CONFIG_WITH_TELEMETRY}"
        };
        using TestServer server = new(Program.CreateWebHostBuilder(args));
        TracerProvider tracerProvider = server.Services.GetRequiredService<TracerProvider>();

        // Act
        using (TelemetryTracesHelper.DABActivitySource.StartActivity("TestActivity"))
        {
        }

        Task<bool> flushTask = Task.Run(() => tracerProvider.ForceFlush());

        // Assert
        // Logs and metrics share the collector, so keep answering requests until the trace export arrives.
        Task timeout = Task.Delay(TimeSpan.FromSeconds(30));
        bool traceReceived = false;
        while (!traceReceived)
        {
            Task<HttpListenerContext> requestTask = collector.GetContextAsync();
            Assert.AreSame(requestTask, await Task.WhenAny(requestTask, timeout), "OTLP exporter did not send the trace over HTTP.");
            HttpListenerContext context = await requestTask;
            using MemoryStream body = new();
            await context.Request.InputStream.CopyToAsync(body);
            traceReceived = context.Request.Url!.AbsolutePath == "/v1/traces"
                && Encoding.UTF8.GetString(body.ToArray()).Contains("TestActivity");
            if (traceReceived)
            {
                Assert.AreEqual("application/x-protobuf", context.Request.ContentType);
            }

            context.Response.StatusCode = 200;
            context.Response.Close();
        }

        Assert.IsTrue(await flushTask, "TracerProvider flush did not succeed.");
    }

    /// <summary>
    /// Tests that the signal path is appended to the OTLP endpoint only for HttpProtobuf, keeping any path prefix.
    /// </summary>
    [DataTestMethod]
    [DataRow("http://localhost:4318", OtlpExportProtocol.HttpProtobuf, "http://localhost:4318/v1/traces")]
    [DataRow("http://localhost:4318/otel/", OtlpExportProtocol.HttpProtobuf, "http://localhost:4318/otel/v1/traces")]
    [DataRow("http://localhost:4317", OtlpExportProtocol.Grpc, "http://localhost:4317/")]
    [DataRow("http://localhost:4317", null, "http://localhost:4317/")]
    public void TestGetOtlpSignalEndpoint(string endpoint, OtlpExportProtocol? protocol, string expected)
    {
        Assert.AreEqual(new Uri(expected), Startup.GetOtlpSignalEndpoint(new Uri(endpoint), protocol, "v1/traces"));
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
