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
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.Tokens;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.Tests.Configuration.ConfigurationTests;

namespace Azure.DataApiBuilder.Service.Tests.Configuration;

/// <summary>
/// Contains tests for telemetry functionality.
/// </summary>
[TestClass, TestCategory(TestCategory.MSSQL)]
public class TelemetryTests
{
    public TestContext TestContext { get; set; }
    private const string TEST_APP_INSIGHTS_CONN_STRING = "InstrumentationKey=testKey;IngestionEndpoint=https://localhost/;LiveEndpoint=https://localhost/";

    private const string CONFIG_WITH_TELEMETRY = "dab-telemetry-test-config.json";
    private const string CONFIG_WITHOUT_TELEMETRY = "dab-no-telemetry-test-config.json";
    private static RuntimeConfig _configuration;

    /// <summary>
    /// Creates runtime config file with specified telemetry options.
    /// </summary>
    /// <param name="configFileName">Name of the config file to be created.</param>
    /// <param name="isTelemetryEnabled">Whether telemetry is enabled or not.</param>
    /// <param name="telemetryConnectionString">Telemetry connection string.</param> 
    public static void SetUpTelemetryInConfig(string configFileName, bool isTelemetryEnabled, string telemetryConnectionString)
    {
        DataSource dataSource = new(DatabaseType.MSSQL,
            GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL), Options: null);

        _configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions: new(), restOptions: new());

        TelemetryOptions _testTelemetryOptions = new(new ApplicationInsightsOptions(isTelemetryEnabled, telemetryConnectionString));
        _configuration = _configuration with { Runtime = _configuration.Runtime with { Telemetry = _testTelemetryOptions } };

        File.WriteAllText(configFileName, _configuration.ToJson());
    }

    /// <summary>
    /// Cleans up the test environment by deleting the runtime config with telemetry options.
    /// </summary>
    [TestCleanup]
    public void CleanUpTelemetryConfig()
    {
        File.Delete(CONFIG_WITH_TELEMETRY);
        File.Delete(CONFIG_WITHOUT_TELEMETRY);
        Startup.AppInsightsOptions = new();
        Startup.CustomTelemetryChannel = null;
    }

    /// <summary>
    /// Test for non-hosted scenario.
    /// Tests that different telemetry items such as Traces or logs, Exceptions and Requests
    /// are correctly sent to application Insights when enabled.
    /// Also asserting on their respective properties.
    /// </summary>
    [TestMethod]
    public async Task TestTelemetryItemsAreSentCorrectly_NonHostedScenario()
    {
        SetUpTelemetryInConfig(CONFIG_WITH_TELEMETRY, isTelemetryEnabled: true, TEST_APP_INSIGHTS_CONN_STRING);

        string[] args = new[]
        {
            $"--ConfigFileName={CONFIG_WITH_TELEMETRY}"
        };

        List<ITelemetry> telemetryItems = new();
        ITelemetryChannel telemetryChannel = new CustomTelemetryChannel(telemetryItems);
        Startup.CustomTelemetryChannel = telemetryChannel;
        using (TestServer server = new(Program.CreateWebHostBuilder(args)))
        {
            await TestRestAndGraphQLRequestsOnServerInNonHostedScenario(server);
        }

        ((CustomTelemetryChannel)Startup.CustomTelemetryChannel).Flush();

        Console.WriteLine(telemetryItems.Any(item => item is TraceTelemetry));
        Console.WriteLine(telemetryItems.Any(item => item is RequestTelemetry));
        Console.WriteLine(telemetryItems.Any(item => item is ExceptionTelemetry));
        Console.WriteLine(telemetryItems.Count(item => item is TraceTelemetry));
        Console.WriteLine(telemetryItems.Count(item => item is ExceptionTelemetry));
        Console.WriteLine(telemetryItems.Count(item => item is RequestTelemetry));

        // Assert that we are sending Traces/Requests/Exceptions
        Assert.IsTrue(telemetryItems.Any(item => item is TraceTelemetry));
        Assert.IsTrue(telemetryItems.Any(item => item is RequestTelemetry));
        Assert.IsTrue(telemetryItems.Any(item => item is ExceptionTelemetry));

        // Asserting on count Exception/Request telemetry items.
        // Assert.AreEqual(1, telemetryItems.Count(item => item is ExceptionTelemetry));
        // Assert.AreEqual(2, telemetryItems.Count(item => item is RequestTelemetry));

        // Assert.IsTrue(telemetryItems.Any(item =>
        //     item is RequestTelemetry
        //     && ((RequestTelemetry)item).Name.Equals("POST /graphql")
        //     && ((RequestTelemetry)item).ResponseCode.Equals("200")
        //     && ((RequestTelemetry)item).Url.PathAndQuery.Equals("/graphql")));

        // Assert.IsTrue(telemetryItems.Any(item =>
        //     item is RequestTelemetry
        //     && ((RequestTelemetry)item).Name.Equals("POST Rest/Insert [route]")
        //     && ((RequestTelemetry)item).ResponseCode.Equals("403")
        //     && ((RequestTelemetry)item).Url.PathAndQuery.Equals("/api/Publisher/id/1?name=Test")));

        // Assert.IsTrue(telemetryItems.Any(item =>
        //     item is ExceptionTelemetry
        //     && ((ExceptionTelemetry)item).Message.Equals("Authorization Failure: Access Not Allowed.")));
    }

    /// <summary>
    /// Testing the Hosted Scenario for both configuration endpoint.
    /// Tests that different telemetry items such as Traces or logs, Exceptions and Requests
    /// are correctly sent to application Insights when enabled.
    /// Also asserting on their respective properties.
    /// </summary>
    //[DataTestMethod]
    //[DataRow(CONFIGURATION_ENDPOINT)]
    //[DataRow(CONFIGURATION_ENDPOINT_V2)]
    //public async Task TestTelemetryItemsAreSentCorrectly_HostedScenario(string configurationEndpoint)
    //{
    //    // Disable parallel execution for this test method
    //    TestContext.Properties.Add("ParallelScope", "Individual");

    //    SetUpTelemetryInConfig(CONFIG_WITH_TELEMETRY, isTelemetryEnabled: true, TEST_APP_INSIGHTS_CONN_STRING);

    //    // Instantiate new server with no runtime config for post-startup configuration hydration tests.
    //    List<ITelemetry> telemetryItems = new();
    //    Startup.CustomTelemetryChannel = new CustomTelemetryChannel(telemetryItems);

    //    using(TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>())))
    //    using (HttpClient client = server.CreateClient())
    //    {
    //        JsonContent content = GetPostStartupConfigParams(TestCategory.MSSQL, _configuration, configurationEndpoint);

    //        HttpResponseMessage postResult =
    //        await client.PostAsync(configurationEndpoint, content);
    //        Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

    //        HttpStatusCode restResponseCode = await GetRestResponsePostConfigHydration(client, "Publisher/id/1");

    //        Assert.AreEqual(expected: HttpStatusCode.Forbidden, actual: restResponseCode);

    //        HttpStatusCode graphqlResponseCode = await GetGraphQLResponsePostConfigHydration(client);

    //        Assert.AreEqual(expected: HttpStatusCode.OK, actual: graphqlResponseCode);
    //    }

    //    ((CustomTelemetryChannel)Startup.CustomTelemetryChannel).Flush();

    //    Console.WriteLine(JsonSerializer.Serialize(telemetryItems));

    //    // Assert that we are sending Traces/Requests/Exceptions
    //    Assert.IsTrue(telemetryItems.Any(item => item is TraceTelemetry));
    //    Assert.IsTrue(telemetryItems.Any(item => item is RequestTelemetry));
    //    Assert.IsTrue(telemetryItems.Any(item => item is ExceptionTelemetry));

    //    // Asserting on count of Exception/Request telemetry items.
    //    Assert.AreEqual(1, telemetryItems.Count(item => item is ExceptionTelemetry));
    //    Assert.AreEqual(3, telemetryItems.Count(item => item is RequestTelemetry));

    //    Assert.IsTrue(telemetryItems.Any(item =>
    //        item is RequestTelemetry
    //        && ((RequestTelemetry)item).Name.Equals("POST Configuration/Index")
    //        && ((RequestTelemetry)item).ResponseCode.Equals("200")
    //        && ((RequestTelemetry)item).Url.PathAndQuery.Equals(configurationEndpoint)));

    //    Assert.IsTrue(telemetryItems.Any(item =>
    //        item is RequestTelemetry
    //        && ((RequestTelemetry)item).Name.Equals("POST /graphql")
    //        && ((RequestTelemetry)item).ResponseCode.Equals("200")
    //        && ((RequestTelemetry)item).Url.PathAndQuery.Equals("/graphql")));

    //    Assert.IsTrue(telemetryItems.Any(item =>
    //        item is RequestTelemetry
    //        && ((RequestTelemetry)item).Name.Equals("GET Rest/Find [route]")
    //        && ((RequestTelemetry)item).ResponseCode.Equals("403")
    //        && ((RequestTelemetry)item).Url.PathAndQuery.Equals("/api/Publisher/id/1")));

    //    Assert.IsTrue(telemetryItems.Any(item =>
    //        item is ExceptionTelemetry
    //        && ((ExceptionTelemetry)item).Message.Equals("Authorization Failure: Access Not Allowed.")));
    //}

    /// <summary>
    /// Validates that no telemetry data is sent to CustomTelemetryChannel when 
    /// Appsights is disabled OR when no valid connectionstring is provided.
    /// </summary>
    /// <param name="isTelemetryEnabled">Whether telemetry is enabled or not.</param>
    /// <param name="telemetryConnectionString">Telemetry connection string.</param>
    [DataTestMethod]
    [DataRow(false, "", DisplayName = "Configuration without a connection string and with Application Insights disabled.")]
    [DataRow(true, "", DisplayName = "Configuration without a connection string, but with Application Insights enabled.")]
    [DataRow(false, TEST_APP_INSIGHTS_CONN_STRING, DisplayName = "Configuration with a connection string, but with Application Insights disabled.")]
    public async Task TestNoTelemetryItemsSentWhenDisabled_NonHostedScenario(bool isTelemetryEnabled, string telemetryConnectionString)
    {
        SetUpTelemetryInConfig(CONFIG_WITHOUT_TELEMETRY, isTelemetryEnabled, telemetryConnectionString);

        string[] args = new[]
        {
           $"--ConfigFileName={CONFIG_WITHOUT_TELEMETRY}"
       };

        List<ITelemetry> telemetryItems = new();
        ITelemetryChannel telemetryChannel = new CustomTelemetryChannel(telemetryItems);
        Startup.CustomTelemetryChannel = telemetryChannel;

        using (TestServer server = new(Program.CreateWebHostBuilder(args)))
        {
            await TestRestAndGraphQLRequestsOnServerInNonHostedScenario(server);
        }

        // Assert that we are not sending any Traces/Requests/Exceptions to Telemetry
        Assert.IsTrue(telemetryItems.IsNullOrEmpty());
    }

    /// <summary>
    /// This method tests the ability of the server to handle GraphQL and REST requests,
    /// and verifies that the server returns the expected response status codes.
    /// Makes a valid GraphQL Request and one Invalid REST Request.
    /// </summary>
    private static async Task TestRestAndGraphQLRequestsOnServerInNonHostedScenario(TestServer server)
    {
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

            // POST request on non-accessible entity
            HttpRequestMessage restRequest = new(HttpMethod.Post, "/api/Publisher/id/1?name=Test");
            HttpResponseMessage restResponse = await client.SendAsync(restRequest);
            Assert.AreEqual(HttpStatusCode.Forbidden, restResponse.StatusCode);
        }
    }

    /// <summary>
    /// The class is a custom telemetry channel to capture telemetry items and assert on them.
    /// </summary>
    private class CustomTelemetryChannel : ITelemetryChannel
    {
        private List<ITelemetry> _telemetryItems = new();

        public CustomTelemetryChannel(List<ITelemetry> telemetryItems)
        {
            _telemetryItems = telemetryItems;
        }

        public bool? DeveloperMode { get; set; }

        public string EndpointAddress { get; set; }

        public void Dispose()
        { }

        public void Flush()
        {
        }

        public void Send(ITelemetry item)
        {
            _telemetryItems.Add(item);
        }
    }
}
