// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.Tests.Configuration.ConfigurationTests;

namespace Azure.DataApiBuilder.Service.Tests.Configuration;

/// <summary>
/// Contains tests for telemetry functionality.
/// </summary>
[TestClass, TestCategory(TestCategory.MSSQL)]
public class TelemetryTests
{
    private const string TEST_APP_INSIGHTS_CONN_STRING = "InstrumentationKey=testKey;IngestionEndpoint=https://unitTest.com/;LiveEndpoint=https://unittest2.com/";

    private readonly static TelemetryOptions _testTelemetryOptions = new(new ApplicationInsightsOptions(true, TEST_APP_INSIGHTS_CONN_STRING));

    private const string CONFIG_WITH_TELEMETRY = "dab-telemetry-test-config.json";
    private static RuntimeConfig _configuration;

    /// <summary>
    /// Sets up the test environment by creating a runtime config with telemetry options.
    /// </summary>
    [ClassInitialize]
    public static void SetUpTelemetryInconfig(TestContext testContext)
    {
        DataSource dataSource = new(DatabaseType.MSSQL,
            GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

        _configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions: new(), restOptions: new());
        _configuration = _configuration with { Runtime = _configuration.Runtime with { Telemetry = _testTelemetryOptions } };

        File.WriteAllText(CONFIG_WITH_TELEMETRY, _configuration.ToJson());
    }

    /// <summary>
    /// Cleans up the test environment by deleting the runtime config with telemetry options.
    /// </summary>
    [ClassCleanup]
    public static void CleanUpTelemetryConfig()
    {
        File.Delete(CONFIG_WITH_TELEMETRY);
    }

    /// <summary>
    /// Test for non-hosted scenario.
    /// Tests that telemetry events are tracked whenever error is caught.
    /// In this test we try to query an entity without appropriate access and
    /// assert on the failure message in the telemetry event sent to Application Insights.
    /// </summary>
    [TestMethod]
    public async Task TestErrorCaughtEventIsSentForErrors_NonHostedScenario()
    {
        string[] args = new[]
        {
            $"--ConfigFileName={CONFIG_WITH_TELEMETRY}"
        };

        TestServer server = new(Program.CreateWebHostBuilder(args));
        TelemetryClient telemetryClient = server.Services.GetService<TelemetryClient>();
        TelemetryConfiguration telemetryConfiguration = telemetryClient.TelemetryConfiguration;
        List<ITelemetry> telemetryItems = new();
        telemetryConfiguration.TelemetryChannel = new CustomTelemetryChannel(telemetryItems);

        using (HttpClient client = server.CreateClient())
        {
            // Get request on non-accessible entity
            HttpRequestMessage restRequest = new(HttpMethod.Post, "/api/Publisher/id/1?name=Test");
            HttpResponseMessage restResponse = await client.SendAsync(restRequest);
            Assert.AreEqual(HttpStatusCode.Forbidden, restResponse.StatusCode);
        }

        // Asserting on TrackEvent telemetry items.
        Assert.AreEqual(1, telemetryItems.Count(item => item is EventTelemetry));

        Assert.IsTrue(telemetryItems.Any(item =>
            item is EventTelemetry
            && ((EventTelemetry)item).Name.Equals("ErrorCaught")
            && ((EventTelemetry)item).Properties["CategoryName"].Equals("Azure.DataApiBuilder.Service.Controllers.RestController")
            && ((EventTelemetry)item).Properties.ContainsKey("correlationId")
            && ((EventTelemetry)item).Properties["Message"].Contains("Error handling REST request.")));
    }

    /// <summary>
    /// Testing the Hosted Scenario for both configuration endpoint.
    /// Tests that telemetry events are tracked whenever error is caught.
    /// In this test we try to query an entity without appropriate access and
    /// assert on the failure message in the telemetry event sent to Application Insights.
    /// </summary>
    [DataTestMethod]
    [DataRow(CONFIGURATION_ENDPOINT)]
    [DataRow(CONFIGURATION_ENDPOINT_V2)]
    public async Task TestErrorCaughtEventIsSentForErrors_HostedScenario(string configurationEndpoint)
    {
        string[] args = new[]
        {
            $"--ConfigFileName={CONFIG_WITH_TELEMETRY}"
        };

        // Instantiate new server with no runtime config for post-startup configuration hydration tests.
        TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
        TelemetryClient telemetryClient = server.Services.GetService<TelemetryClient>();
        TelemetryConfiguration telemetryConfiguration = telemetryClient.TelemetryConfiguration;
        List<ITelemetry> telemetryItems = new();
        telemetryConfiguration.TelemetryChannel = new CustomTelemetryChannel(telemetryItems);

        using (HttpClient client = server.CreateClient())
        {
            JsonContent content = GetPostStartupConfigParams(TestCategory.MSSQL, _configuration, configurationEndpoint);

            HttpResponseMessage postResult =
            await client.PostAsync(configurationEndpoint, content);
            Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

            HttpStatusCode restResponseCode = await GetRestResponsePostConfigHydration(client, "Publisher/id/1?name=Test");

            Assert.AreEqual(expected: HttpStatusCode.Forbidden, actual: restResponseCode);
        }

        server.Dispose();

        // Asserting on TrackEvent telemetry items.
        Assert.AreEqual(1, telemetryItems.Count(item => item is EventTelemetry));

        Assert.IsTrue(telemetryItems.Any(item =>
            item is EventTelemetry
            && ((EventTelemetry)item).Name.Equals("ErrorCaught")
            && ((EventTelemetry)item).Properties["CategoryName"].Equals("Azure.DataApiBuilder.Service.Controllers.RestController")
            && ((EventTelemetry)item).Properties.ContainsKey("correlationId")
            && ((EventTelemetry)item).Properties["Message"].Contains("Error handling REST request.")));
    }

    /// <summary>
    /// The class is a custom telemetry channel to capture telemetry items and assert on them.
    /// </summary>
    private class CustomTelemetryChannel : ITelemetryChannel
    {
        private readonly List<ITelemetry> _telemetryItems;

        public CustomTelemetryChannel(List<ITelemetry> telemetryItems)
        {
            _telemetryItems = telemetryItems;
        }

        public bool? DeveloperMode { get; set; }

        public string EndpointAddress { get; set; }

        public void Dispose()
        { }

        public void Flush()
        { }

        public void Send(ITelemetry item)
        {
            _telemetryItems.Add(item);
        }
    }
}
