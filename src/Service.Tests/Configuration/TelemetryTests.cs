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

[TestClass]
public class TelemetryTests
{
    private const string TEST_APP_INSIGHTS_CONN_STRING = "InstrumentationKey=testKey;IngestionEndpoint=https://unitTest.com/;LiveEndpoint=https://unittest2.com/";

    private readonly static TelemetryOptions _testTelemetryOptions = new(new ApplicationInsightsOptions(true, TEST_APP_INSIGHTS_CONN_STRING));

    [TestMethod]
    public async Task TestTrackTelemetryEventsForNonHostedScenario()
    {
        DataSource dataSource = new(DatabaseType.MSSQL,
            GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

        RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, new(), new());
        configuration = configuration with { Runtime = configuration.Runtime with { Telemetry = _testTelemetryOptions } };

        const string CUSTOM_CONFIG = "custom-config.json";
        File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

        string[] args = new[]
        {
            $"--ConfigFileName={CUSTOM_CONFIG}"
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

    [TestMethod]
    public async Task TestTrackTelemetryEventsForHostedScenario()
    {
        DataSource dataSource = new(DatabaseType.MSSQL,
            GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

        RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, new(), new());
        configuration = configuration with { Runtime = configuration.Runtime with { Telemetry = _testTelemetryOptions } };

        const string CUSTOM_CONFIG = "custom-config.json";
        File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

        string[] args = new[]
        {
            $"--ConfigFileName={CUSTOM_CONFIG}"
        };

        // Instantiate new server with no runtime config for post-startup configuration hydration tests.
        TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
        TelemetryClient telemetryClient = server.Services.GetService<TelemetryClient>();
        TelemetryConfiguration telemetryConfiguration = telemetryClient.TelemetryConfiguration;
        List<ITelemetry> telemetryItems = new();
        telemetryConfiguration.TelemetryChannel = new CustomTelemetryChannel(telemetryItems);

        using (HttpClient client = server.CreateClient())
        {
            JsonContent content = GetPostStartupConfigParams(TestCategory.MSSQL, configuration, "/configuration");

            HttpResponseMessage postResult =
            await client.PostAsync("/configuration", content);
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

    [TestMethod]
    public async Task TestErrorCaughtEventIsSentForErrors()
    {
        DataSource dataSource = new(DatabaseType.MSSQL,
            GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

        RuntimeConfig configuration = InitMinimalRuntimeConfig(dataSource, new(), new());
        configuration = configuration with { Runtime = configuration.Runtime with { Telemetry = _testTelemetryOptions } };

        const string CUSTOM_CONFIG = "custom-config.json";
        File.WriteAllText(CUSTOM_CONFIG, configuration.ToJson());

        string[] args = new[]
        {
            $"--ConfigFileName={CUSTOM_CONFIG}"
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
