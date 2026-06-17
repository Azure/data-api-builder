// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.Tests.Configuration.ConfigurationEndpoints;
using static Azure.DataApiBuilder.Service.Tests.Configuration.TestConfigFileReader;

namespace Azure.DataApiBuilder.Service.Tests.Configuration;

/// <summary>
/// Tests for the security mitigation on the POST /configuration endpoint (CWE-306).
/// Verifies that the endpoint is restricted to loopback addresses and optionally
/// gated behind a bootstrap token (DAB_CONFIG_AUTH_TOKEN / X-DAB-CONFIG-AUTH header).
/// </summary>
[TestClass]
public class ConfigurationEndpointAuthorizationTests
{
    // Names of environment variables this test class manipulates. Snapshotted in
    // TestInitialize and restored in TestCleanup so each test starts and ends from
    // the same global state regardless of what other tests in the assembly do.
    private const string ASPNETCORE_ENVIRONMENT_VAR = "ASPNETCORE_ENVIRONMENT";
    private const string DAB_ENVIRONMENT_VAR = "DAB_ENVIRONMENT";

    private string _originalAuthToken;
    private string _originalAspNetEnvironment;
    private string _originalDabEnvironment;

    [TestInitialize]
    public void Initialize()
    {
        // Snapshot the originals so they can be restored verbatim in TestCleanup.
        _originalAuthToken = Environment.GetEnvironmentVariable(Startup.CONFIG_AUTH_TOKEN_ENV_VAR);
        _originalAspNetEnvironment = Environment.GetEnvironmentVariable(ASPNETCORE_ENVIRONMENT_VAR);
        _originalDabEnvironment = Environment.GetEnvironmentVariable(DAB_ENVIRONMENT_VAR);

        // Other tests in the assembly (e.g. TestLoadingLocalCosmosSettings) set
        // ASPNETCORE_ENVIRONMENT / DAB_ENVIRONMENT without cleanup. Those env vars cause
        // FileSystemRuntimeConfigLoader to find an environment-specific dab-config.*.json on
        // disk and auto-initialize the runtime, which makes POST /configuration return
        // 409 Conflict before our security middleware can be exercised. Clear them so each
        // test starts from an uninitialized runtime; the originals are restored in cleanup.
        Environment.SetEnvironmentVariable(ASPNETCORE_ENVIRONMENT_VAR, null);
        Environment.SetEnvironmentVariable(DAB_ENVIRONMENT_VAR, null);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable(Startup.CONFIG_AUTH_TOKEN_ENV_VAR, _originalAuthToken);
        Environment.SetEnvironmentVariable(ASPNETCORE_ENVIRONMENT_VAR, _originalAspNetEnvironment);
        Environment.SetEnvironmentVariable(DAB_ENVIRONMENT_VAR, _originalDabEnvironment);
    }

    /// <summary>
    /// Validates IsConfigurationRequestAuthorized across the full matrix of inputs:
    /// remote IP (loopback v4/v6, private/public, null/in-process), configured bootstrap
    /// token, and provided X-DAB-CONFIG-AUTH header value.
    /// </summary>
    /// <param name="remoteIp">
    /// Source IP for the simulated request. Use null to model an in-process call
    /// (e.g. TestServer) where there is no underlying TCP connection.
    /// </param>
    /// <param name="configuredToken">Value to set DAB_CONFIG_AUTH_TOKEN to, or null to leave unset.</param>
    /// <param name="providedHeader">Value of the X-DAB-CONFIG-AUTH header, or null to omit.</param>
    /// <param name="expected">Expected authorization result.</param>
    [DataTestMethod]
    // --- No bootstrap token configured ---
    [DataRow("127.0.0.1", null, null, true, DisplayName = "Loopback IPv4, no token => allow")]
    [DataRow("::1", null, null, true, DisplayName = "Loopback IPv6, no token => allow")]
    [DataRow("::ffff:127.0.0.1", null, null, true, DisplayName = "IPv4-mapped IPv6 loopback (dual-stack Kestrel), no token => allow")]
    [DataRow(null, null, null, true, DisplayName = "In-process (null IP), no token => allow")]
    [DataRow("192.168.1.100", null, null, false, DisplayName = "Private IPv4, no token => deny")]
    [DataRow("10.0.0.1", null, null, false, DisplayName = "Private IPv4 (10/8), no token => deny")]
    [DataRow("172.17.0.1", null, null, false, DisplayName = "Docker bridge IP, no token => deny")]
    [DataRow("203.0.113.50", null, null, false, DisplayName = "Public IPv4, no token => deny")]
    // --- Bootstrap token configured ---
    [DataRow("127.0.0.1", "secret", "secret", true, DisplayName = "Loopback + correct token => allow")]
    [DataRow("127.0.0.1", "secret", "wrong", false, DisplayName = "Loopback + wrong token => deny")]
    [DataRow("127.0.0.1", "secret", null, false, DisplayName = "Loopback + missing header => deny")]
    [DataRow("192.168.1.50", "secret", "secret", false, DisplayName = "Private IPv4 + correct token => still deny (non-loopback)")]
    [DataRow("192.168.1.50", "secret", "wrong", false, DisplayName = "Private IPv4 + wrong token => deny")]
    [DataRow("203.0.113.50", "secret", "secret", false, DisplayName = "Public IPv4 + correct token => still deny (non-loopback)")]
    [DataRow("203.0.113.50", "secret", "wrong", false, DisplayName = "Public IPv4 + wrong token => deny")]
    [DataRow("203.0.113.50", "secret", null, false, DisplayName = "Public IPv4 + missing header => deny")]
    [DataRow(null, "secret", null, false, DisplayName = "In-process + missing header => deny")]
    [DataRow(null, "secret", "secret", true, DisplayName = "In-process + correct token => allow")]
    public void IsConfigurationRequestAuthorized_Matrix(
        string remoteIp,
        string configuredToken,
        string providedHeader,
        bool expected)
    {
        // Arrange
        Environment.SetEnvironmentVariable(Startup.CONFIG_AUTH_TOKEN_ENV_VAR, configuredToken);

        DefaultHttpContext httpContext = new();
        httpContext.Connection.RemoteIpAddress = remoteIp is null ? null : IPAddress.Parse(remoteIp);
        if (providedHeader is not null)
        {
            httpContext.Request.Headers[Startup.CONFIG_AUTH_HEADER] = providedHeader;
        }

        // Act
        bool result = Startup.IsConfigurationRequestAuthorized(httpContext);

        // Assert
        Assert.AreEqual(expected, result);
    }

    /// <summary>
    /// End-to-end test against the full middleware pipeline via TestServer. Drives both
    /// /configuration (v1) and /configuration/v2 with every token combination and verifies
    /// the HTTP status code returned by the security middleware.
    /// </summary>
    /// <param name="configurationEndpoint">The endpoint being tested.</param>
    /// <param name="configuredToken">Value to set DAB_CONFIG_AUTH_TOKEN to, or null to leave unset.</param>
    /// <param name="providedHeader">Value of the X-DAB-CONFIG-AUTH header, or null to omit.</param>
    /// <param name="expectedStatus">Expected HTTP status code from the POST.</param>
    [DataTestMethod, TestCategory(TestCategory.COSMOSDBNOSQL)]
    // No token configured -- backward-compatible loopback success
    [DataRow(CONFIGURATION_ENDPOINT, null, null, HttpStatusCode.OK)]
    [DataRow(CONFIGURATION_ENDPOINT_V2, null, null, HttpStatusCode.OK)]
    // Token configured and correct -- allowed
    [DataRow(CONFIGURATION_ENDPOINT, "integration-token", "integration-token", HttpStatusCode.OK)]
    [DataRow(CONFIGURATION_ENDPOINT_V2, "integration-token", "integration-token", HttpStatusCode.OK)]
    // Token configured but wrong -- forbidden
    [DataRow(CONFIGURATION_ENDPOINT, "correct-token", "wrong-token", HttpStatusCode.Forbidden)]
    [DataRow(CONFIGURATION_ENDPOINT_V2, "correct-token", "wrong-token", HttpStatusCode.Forbidden)]
    // Token configured but header missing -- forbidden
    [DataRow(CONFIGURATION_ENDPOINT, "required-token", null, HttpStatusCode.Forbidden)]
    [DataRow(CONFIGURATION_ENDPOINT_V2, "required-token", null, HttpStatusCode.Forbidden)]
    public async Task EndToEnd_PostConfiguration_StatusCodeMatrix(
        string configurationEndpoint,
        string configuredToken,
        string providedHeader,
        HttpStatusCode expectedStatus)
    {
        // Arrange -- ASPNETCORE_ENVIRONMENT / DAB_ENVIRONMENT are already cleared by
        // TestInitialize so the runtime starts uninitialized and our middleware is
        // exercised. Only the per-row auth token still needs to be set here; TestCleanup
        // restores the original value.
        Environment.SetEnvironmentVariable(Startup.CONFIG_AUTH_TOKEN_ENV_VAR, configuredToken);

        using TestServer server = new(Program.CreateWebHostFromInMemoryUpdatableConfBuilder(Array.Empty<string>()));
        using HttpClient httpClient = server.CreateClient();

        HttpRequestMessage request = new(HttpMethod.Post, configurationEndpoint)
        {
            Content = BuildPostContent(configurationEndpoint),
        };
        if (providedHeader is not null)
        {
            request.Headers.Add(Startup.CONFIG_AUTH_HEADER, providedHeader);
        }

        // Act
        HttpResponseMessage postResult = await httpClient.SendAsync(request);

        // Assert
        Assert.AreEqual(expectedStatus, postResult.StatusCode);
    }

    /// <summary>
    /// Builds the JSON post body appropriate for the given configuration endpoint version.
    /// </summary>
    private static JsonContent BuildPostContent(string endpoint)
    {
        Config.ObjectModel.RuntimeConfig config = ReadCosmosConfigurationFromFile() with { Schema = "@env('schema')" };
        const string graphqlSchema = @"
                type Entity {
                    id: ID!
                    name: String!
                }
                ";

        if (endpoint == CONFIGURATION_ENDPOINT)
        {
            return JsonContent.Create(new ConfigurationPostParameters(
                Configuration: config.ToJson(),
                Schema: graphqlSchema,
                ConnectionString: "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
                AccessToken: null));
        }

        if (endpoint == CONFIGURATION_ENDPOINT_V2)
        {
            return JsonContent.Create(new ConfigurationPostParametersV2(
                Configuration: config.ToJson(),
                ConfigurationOverrides: "{}",
                Schema: graphqlSchema,
                AccessToken: null));
        }

        throw new ArgumentException($"Unknown endpoint: {endpoint}", nameof(endpoint));
    }
}
