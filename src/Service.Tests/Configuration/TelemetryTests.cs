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

        _configuration = InitMinimalRuntimeConfig(dataSource, new(), new());
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
    /// Tests that telemetry events are tracked for non-hosted scenarios.
    /// Makes a REST and GraphQL requests and asserts on the telemetry items.
    /// </summary>
    [TestMethod]
    public async Task TestTrackTelemetryEventsForNonHostedScenario()
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
            string query = @"{
                book_by_pk(id: 1) {
                    id,
                    title,
                    publisher_id
                }
            }";

            object payload = new { query };

            HttpRequestMessage graphQLRequest = new(HttpMethod.Post, "/graphql")
            {
                Content = JsonContent.Create(payload)
            };

            HttpResponseMessage graphQLResponse = await client.SendAsync(graphQLRequest);
            Assert.AreEqual(HttpStatusCode.OK, graphQLResponse.StatusCode);

            HttpRequestMessage restRequest = new(HttpMethod.Get, "/api/Book");
            HttpResponseMessage restResponse = await client.SendAsync(restRequest);
            Assert.AreEqual(HttpStatusCode.OK, restResponse.StatusCode);
        }

        // Asserting on TrackEvent telemetry items.
        Assert.AreEqual(2, telemetryItems.Count());

        Assert.IsTrue(telemetryItems.Any(item =>
            item is EventTelemetry
            && ((EventTelemetry)item).Name == "GraphQLRequestReceived"
            && ((EventTelemetry)item).Properties["GraphQLOperation"] == "Query"
            && ((EventTelemetry)item).Properties["GraphQLEntityOperationName"] == "book_by_pk"
            && ((EventTelemetry)item).Properties["GraphQLRequestMethod"] == "POST"));

        Assert.IsTrue(telemetryItems.Any(item =>
            item is EventTelemetry
            && ((EventTelemetry)item).Name == "RestRequestReceived"
            && ((EventTelemetry)item).Properties["RestRequestMethod"] == "GET"
            && ((EventTelemetry)item).Properties["RestRoute"] == "api/Book"
            && ((EventTelemetry)item).Properties["RestEntityName"] == "Book"
            && ((EventTelemetry)item).Properties["RestEntityActionOperation"] == "Read"));
    }

    /// <summary>
    /// Tests that telemetry events are tracked for hosted scenarios.
    /// Makes a REST and GraphQL requests and asserts on the telemetry items using supported configuration endpoints.
    /// </summary>
    [DataTestMethod]
    [DataRow(CONFIGURATION_ENDPOINT)]
    [DataRow(CONFIGURATION_ENDPOINT_V2)]
    public async Task TestTrackTelemetryEventsForHostedScenario(string configurationEndpoint)
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

            HttpStatusCode restResponseCode = await GetRestResponsePostConfigHydration(client);

            Assert.AreEqual(expected: HttpStatusCode.OK, actual: restResponseCode);

            HttpStatusCode graphqlResponseCode = await GetGraphQLResponsePostConfigHydration(client);

            Assert.AreEqual(expected: HttpStatusCode.OK, actual: graphqlResponseCode);

        }

        // Asserting on TrackEvent telemetry items.
        Assert.AreEqual(2, telemetryItems.Count());

        Assert.IsTrue(telemetryItems.Any(item =>
            item is EventTelemetry
            && ((EventTelemetry)item).Name == "GraphQLRequestReceived"
            && ((EventTelemetry)item).Properties["GraphQLOperation"] == "Query"
            && ((EventTelemetry)item).Properties["GraphQLEntityOperationName"] == "book_by_pk"
            && ((EventTelemetry)item).Properties["GraphQLRequestMethod"] == "POST"));

        Assert.IsTrue(telemetryItems.Any(item =>
            item is EventTelemetry
            && ((EventTelemetry)item).Name == "RestRequestReceived"
            && ((EventTelemetry)item).Properties["RestRequestMethod"] == "GET"
            && ((EventTelemetry)item).Properties["RestRoute"] == "api/Book"
            && ((EventTelemetry)item).Properties["RestEntityName"] == "Book"
            && ((EventTelemetry)item).Properties["RestEntityActionOperation"] == "Read"));
    }

    /// <summary>
    /// Tests that telemetry events are tracked whenever error is caught.
    /// In this test we try to query an entity without appropriate access and
    /// assert on the failure message in the telemetry event sent to Application Insights.
    /// </summary>
    [TestMethod]
    public async Task TestErrorCaughtEventIsSentForErrors()
    {
        string[] args = new[]
        {
            $"--ConfigFileName={CONFIG_WITH_TELEMETRY}"
        };

        TestServer server = new(Program.CreateWebHostBuilder(args));
        TelemetryClient telemetryClient = server.Services.GetService<TelemetryClient>();
        TelemetryConfiguration telemetryConfiguration = telemetryClient.TelemetryConfiguration;
        List<ITelemetry> telemetryItems = new();
        telemetryConfiguration.TelemetryChannel = new CustomTelemetryChannel(telemetryItems)
        {
            EndpointAddress = "https://unitTest.com/"
        };

        using (HttpClient client = server.CreateClient())
        {
            // Get request on non-accessible entity
            HttpRequestMessage restRequest = new(HttpMethod.Post, "/api/Publisher/id/1?name=Test");
            HttpResponseMessage restResponse = await client.SendAsync(restRequest);
            Assert.AreEqual(HttpStatusCode.Forbidden, restResponse.StatusCode);
        }

        // Asserting on TrackEvent telemetry items.
        Assert.AreEqual(3, telemetryItems.Count());

        Assert.IsTrue(telemetryItems.Any(item =>
            item is EventTelemetry
            && ((EventTelemetry)item).Name == "RestRequestReceived"
            && ((EventTelemetry)item).Properties["RestRequestMethod"] == "POST"
            && ((EventTelemetry)item).Properties["RestRoute"] == "api/Publisher/id/1"
            && ((EventTelemetry)item).Properties["RestEntityName"] == "Publisher"
            && ((EventTelemetry)item).Properties["RestEntityActionOperation"] == "Insert"));

        Assert.IsTrue(telemetryItems.Any(item =>
            item is EventTelemetry
            && ((EventTelemetry)item).Name == "ErrorCaught"
            && ((EventTelemetry)item).Properties["Message"].Contains("Authorization Failure: Access Not Allowed.")));
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
        {
        }

        public void Flush()
        {
        }

        public void Send(ITelemetry item)
        {
            _telemetryItems.Add(item);
        }
    }
}
