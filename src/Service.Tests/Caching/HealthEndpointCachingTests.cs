// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Service.Tests.Configuration;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Caching;

/// <summary>
/// Validates that the caching of health endpoint behaves as expected.
/// </summary>
[TestClass]
public class HealthEndpointCachingTests
{
    private const string CUSTOM_CONFIG_FILENAME = "custom-config.json";

    [TestCleanup]
    public void CleanupAfterEachTest()
    {
        if (File.Exists(CUSTOM_CONFIG_FILENAME))
        {
            File.Delete(CUSTOM_CONFIG_FILENAME);
        }

        TestHelper.UnsetAllDABEnvironmentVariables();
    }

    /// <summary>
    /// Simulates GET requests to DAB's comprehensive health check endpoint ('/health') and validates the contents of the response.
    /// The expected behavior is that these responses should be different as we supply delay in between them.
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategory.MSSQL)]
    [DataRow(null, DisplayName = "Validation of default cache TTL and delay.")]
    [DataRow(0, DisplayName = "Validation of cache TTL set to 0 and delay.")]
    [DataRow(10, DisplayName = "Validation of cache TTL set to 10 and delay.")]
    public async Task ComprehensiveHealthEndpointCachingValidateWithDelay(int? cacheTtlSeconds)
    {
        SetupCachingTest(cacheTtlSeconds);
        string[] args = new[]
        {
            $"--ConfigFileName={CUSTOM_CONFIG_FILENAME}"
        };

        using (TestServer server = new(Program.CreateWebHostBuilder(args)))
        using (HttpClient client = server.CreateClient())
        {
            HttpRequestMessage healthRequest1 = new(HttpMethod.Get, "/health");
            HttpResponseMessage response = await client.SendAsync(healthRequest1);
            string responseContent1 = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(expected: HttpStatusCode.OK, actual: response.StatusCode, message: "Received unexpected HTTP code from health check endpoint.");

            // Simulate a "delay" to allow the cache to expire (in case available)
            // and send a new request to the health endpoint.
            Task.Delay((cacheTtlSeconds ?? EntityCacheOptions.DEFAULT_TTL_SECONDS) * 1000 + 1000).Wait();

            HttpRequestMessage healthRequest2 = new(HttpMethod.Get, "/health");
            response = await client.SendAsync(healthRequest2);
            string responseContent2 = await response.Content.ReadAsStringAsync();

            // Responses are not the same as a new request was made to the DB (change in responseTimeMs for DB health check)
            Assert.AreNotEqual(responseContent2, responseContent1);
        }
    }

    /// <summary>
    /// Simulates GET request to DAB's comprehensive health check endpoint ('/health') and validates the contents of the response.
    /// The expected behavior is that both these responses should be same in case cache is enabled with no delay between the two requests.
    /// </summary>
    [TestMethod]
    [TestCategory(TestCategory.MSSQL)]
    [DataRow(null, DisplayName = "Validation of default cache TTL and no delay.")]
    [DataRow(0, DisplayName = "Validation of cache TTL set to 0 and no delay.")]
    [DataRow(10, DisplayName = "Validation of cache TTL set to 10 and no delay.")]
    public async Task ComprehensiveHealthEndpointCachingValidateNoDelay(int? cacheTtlSeconds)
    {
        SetupCachingTest(cacheTtlSeconds);
        string[] args = new[]
        {
            $"--ConfigFileName={CUSTOM_CONFIG_FILENAME}"
        };

        using (TestServer server = new(Program.CreateWebHostBuilder(args)))
        using (HttpClient client = server.CreateClient())
        {
            HttpRequestMessage healthRequest1 = new(HttpMethod.Get, "/health");
            HttpResponseMessage response = await client.SendAsync(healthRequest1);
            string responseContent1 = await response.Content.ReadAsStringAsync();
            Assert.AreEqual(expected: HttpStatusCode.OK, actual: response.StatusCode, message: "Received unexpected HTTP code from health check endpoint.");

            // Simulate a "no delay" scenario to make sure that the cache is not expired (in case available)
            // and send a new request to the health endpoint.
            HttpRequestMessage healthRequest2 = new(HttpMethod.Get, "/health");
            response = await client.SendAsync(healthRequest2);
            string responseContent2 = await response.Content.ReadAsStringAsync();

            if (cacheTtlSeconds == 0)
            {
                // Responses are not the same as a new request was made to the DB (change in responseTimeMs for DB health check)
                Assert.AreNotEqual(responseContent2, responseContent1);
            }
            else
            {
                // Response are the same as its coming from Cache (Timestamp would also be the same)
                Assert.AreEqual(responseContent2, responseContent1);
            }
        }
    }

    private static void SetupCachingTest(int? cacheTtlSeconds)
    {
        Entity requiredEntity = new(
            Health: new(enabled: true),
            Source: new("books", EntitySourceType.Table, null, null),
            Rest: new(Enabled: true),
            GraphQL: new("book", "books", true),
            Permissions: new[] { ConfigurationTests.GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
            Relationships: null,
            Mappings: null);

        Dictionary<string, Entity> entityMap = new()
        {
            { "Book", requiredEntity }
        };

        CreateCustomConfigFile(entityMap, cacheTtlSeconds);
    }

    /// <summary>
    /// Helper function to write custom configuration file. with minimal REST/GraphQL global settings
    /// using the supplied entities.
    /// </summary>
    /// <param name="entityMap">Collection of entityName -> Entity object.</param>
    /// <param name="cacheTtlSeconds">flag to enable or disabled REST globally.</param>
    private static void CreateCustomConfigFile(Dictionary<string, Entity> entityMap, int? cacheTtlSeconds)
    {
        DataSource dataSource = new(
            DatabaseType.MSSQL,
            ConfigurationTests.GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL),
            Options: null,
            Health: new(true));
        HostOptions hostOptions = new(Mode: HostMode.Development, Cors: null, Authentication: new() { Provider = nameof(EasyAuthType.StaticWebApps) });

        RuntimeConfig runtimeConfig = new(
            Schema: string.Empty,
            DataSource: dataSource,
            Runtime: new(
                Health: new(enabled: true, cacheTtlSeconds: cacheTtlSeconds),
                Rest: new(Enabled: true),
                GraphQL: new(Enabled: true),
                Host: hostOptions
            ),
            Entities: new(entityMap));

        File.WriteAllText(
            path: CUSTOM_CONFIG_FILENAME,
            contents: runtimeConfig.ToJson());
    }

}
