// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.Tests.Configuration.ConfigurationTests;

namespace Azure.DataApiBuilder.Service.Tests.Configuration.Telemetry;

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

        _configuration = InitMinimalRuntimeConfig(dataSource, graphqlOptions: new(), restOptions: new(), mcpOptions: new());

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
    /// <note>
    /// Commenting Assert on Request Telemetry as it is flaky, sometimes passing sometimes failing.
    /// while on manual testing it is working fine and we see all the request telemetryItems in Application Insights.
    /// Issue to track the fix for this test: https://github.com/Azure/data-api-builder/issues/1734
    [TestMethod]
    public async Task TestTelemetryItemsAreSentCorrectly_NonHostedScenario()
    {
        SetUpTelemetryInConfig(CONFIG_WITH_TELEMETRY, isTelemetryEnabled: true, TEST_APP_INSIGHTS_CONN_STRING);

        string[] args = new[]
        {
            $"--ConfigFileName={CONFIG_WITH_TELEMETRY}"
        };

        ITelemetryChannel telemetryChannel = new CustomTelemetryChannel()
        {
            EndpointAddress = "https://localhost/"
        };
        Startup.CustomTelemetryChannel = telemetryChannel;
        using (TestServer server = new(Program.CreateWebHostBuilder(args)))
        {
            await TestRestAndGraphQLRequestsOnServerInNonHostedScenario(server);
            Assert.IsTrue(server.Services.GetService<TelemetryClient>() is not null);
        }

        List<ITelemetry> telemetryItems = ((CustomTelemetryChannel)telemetryChannel).GetTelemetryItems();

        // Assert that we are sending Traces/Requests/Exceptions
        Assert.IsTrue(telemetryItems.Any(item => item is TraceTelemetry));
        // Assert.IsTrue(telemetryItems.Any(item => item is RequestTelemetry));
        Assert.IsTrue(telemetryItems.Any(item => item is ExceptionTelemetry));

        // Asserting on Trace telemetry items.
        // Checking for the Logs for the two entities Book and Publisher are correctly sent to Application Insights.
        Assert.IsTrue(telemetryItems.Any(item =>
            item is TraceTelemetry
            && ((TraceTelemetry)item).Message.Equals("[Book] REST path: /api/Book")
            && ((TraceTelemetry)item).SeverityLevel == SeverityLevel.Information));

        Assert.IsTrue(telemetryItems.Any(item =>
            item is TraceTelemetry
            && ((TraceTelemetry)item).Message.Equals("[Publisher] REST path: /api/Publisher")
            && ((TraceTelemetry)item).SeverityLevel == SeverityLevel.Information));

        // Asserting on Request telemetry items.
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

        // Assert on the Exceptions telemetry items.
        Assert.AreEqual(1, telemetryItems.Count(item => item is ExceptionTelemetry));
        Assert.IsTrue(telemetryItems.Any(item =>
            item is ExceptionTelemetry
            && ((ExceptionTelemetry)item).Message.Equals("Authorization Failure: Access Not Allowed.")));
    }

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

        ITelemetryChannel telemetryChannel = new CustomTelemetryChannel();
        Startup.CustomTelemetryChannel = telemetryChannel;

        using (TestServer server = new(Program.CreateWebHostBuilder(args)))
        {
            await TestRestAndGraphQLRequestsOnServerInNonHostedScenario(server);
            // Telemetry client should be null if telemetry is disabled
            // Using an EXOR here to assert this.
            Assert.IsTrue(server.Services.GetService<TelemetryClient>() is null ^ isTelemetryEnabled);
        }

        List<ITelemetry> telemetryItems = ((CustomTelemetryChannel)telemetryChannel).GetTelemetryItems();

        // Assert that we are not sending any Traces/Requests/Exceptions to Telemetry
        Assert.IsTrue(telemetryItems.IsNullOrEmpty());
    }

    /// <summary>
    /// This method is just used as helper for other test methods to execute REST and GRaphQL requests
    /// which trigger the logging system to emit logs.
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

            await client.SendAsync(graphQLRequest);

            // POST request on non-accessible entity
            HttpRequestMessage restRequest = new(HttpMethod.Post, "/api/Publisher/id/1?name=Test");
            await client.SendAsync(restRequest);
        }
    }

    /// <summary>
    /// The class is a custom telemetry channel to capture telemetry items and assert on them.
    /// </summary>
    private class CustomTelemetryChannel : ITelemetryChannel
    {
        private List<ITelemetry> _telemetryItems = new();

        public CustomTelemetryChannel()
        { }

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

        public List<ITelemetry> GetTelemetryItems()
        {
            return _telemetryItems;
        }
    }
}
