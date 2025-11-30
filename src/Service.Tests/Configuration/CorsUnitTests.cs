// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VerifyMSTest;

namespace Azure.DataApiBuilder.Service.Tests.Configuration;

/// <summary>
/// Tests that Cors is read correctly from configuration and server is configured as expected
/// Server configuration is verified through sending HTTP requests to generated test server
/// and comparing expected and received header values
///
/// Behavior of origins:["*"] resolving to "AllowAllOrigins" vs origins:["*", "any other specific host"] not doing so
/// is verified through testing. Only documentation found relating to this:
/// https://docs.microsoft.com/en-us/cli/azure/webapp/cors?view=azure-cli-latest
/// """To allow all, use "*" and remove all other origins from the list"""
/// </summary>
[TestClass]
public class CorsUnitTests
    : VerifyBase
{

    #region Positive Tests

    /// <summary>
    /// Verify correct deserialization of Cors record
    /// </summary>
    [TestMethod]
    public Task TestCorsConfigReadCorrectly()
    {
        IFileSystem fileSystem = new MockFileSystem(new Dictionary<string, MockFileData>
        {
            { FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(TestHelper.INITIAL_CONFIG) }
        });

        FileSystemRuntimeConfigLoader loader = new(fileSystem);
        Assert.IsTrue(loader.TryLoadConfig(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, out RuntimeConfig runtimeConfig), "Load runtime config.");

        Config.ObjectModel.HostOptions hostGlobalSettings = runtimeConfig.Runtime?.Host;
        return Verify(hostGlobalSettings);
    }

    /// <summary>
    /// Testing against the simulated test server whether an Access-Control-Allow-Origin header is present on the response
    /// Expect the server to populate and send back the Access-Control-Allow-Origin header since http://localhost:3000 should be present in the origins list
    /// Access-Control-Allow-Origin echos specific origin of request, unless server configured to allow all origins, in which case it will respond with '*'
    /// <param name="allowedOrigins"> the allowed origins for the server to check against </param>
    /// DataRow 1: valid because all origins accepted
    /// DataRow 2: valid because specific host present in origins list
    /// DataRow 3: valid because specific host present in origins list (wildcard ignored - expected behavior, see https://docs.microsoft.com/en-us/cli/azure/webapp/cors?view=azure-cli-latest)
    /// </summary>
    [TestMethod]
    [DataRow(new string[] { "*" }, DisplayName = "Test allow origin with wildcard")]
    [DataRow(new string[] { "http://localhost:3000" }, DisplayName = "Test allow specific origin")]
    [DataRow(new string[] { "http://localhost:3000", "*", "invalid host" }, DisplayName = "Test allow specific origin with wilcard")]
    public async Task TestAllowedOriginHeaderPresent(string[] allowedOrigins)
    {
        IHost host = await CreateCorsConfiguredWebHost(allowedOrigins, false);

        TestServer server = host.GetTestServer();
        HttpContext returnContext = await server.SendAsync(context =>
        {
            KeyValuePair<string, StringValues> originHeader = new("Origin", "http://localhost:3000");
            context.Request.Headers.Add(originHeader);
        });

        Assert.IsNotNull(returnContext.Response.Headers.AccessControlAllowOrigin);
        Assert.AreEqual<string>(expected: allowedOrigins[0], actual: returnContext.Response.Headers.AccessControlAllowOrigin);
    }

    /// <summary>
    /// Simple test if AllowCredentials option correctly toggles Access-Control-Allow-Credentials header
    /// Access-Control-Allow-Credentials header should be toggled to "true" on server config allowing credentials
    /// Only requests from valid origins (based on server's allowed origins) receive this header
    /// </summary>
    [TestMethod]
    public async Task TestAllowedCredentialsHeaderPresent()
    {
        IHost host = await CreateCorsConfiguredWebHost(new string[] { "http://localhost:3000" }, true);

        TestServer server = host.GetTestServer();
        HttpContext returnContext = await server.SendAsync(context =>
        {
            KeyValuePair<string, StringValues> originHeader = new("Origin", "http://localhost:3000");
            context.Request.Headers.Add(originHeader);
        });

        Assert.AreEqual<string>(expected: "true", actual: returnContext.Response.Headers.AccessControlAllowCredentials);
    }

    #endregion

    #region Negative Tests

    /// <summary>
    /// Testing against the simulated test server whether an Access-Control-Allow-Origin header is present on the response
    /// Expect header to exist but be empty on response to requests from origins not present in server's origins list
    /// <param name="allowedOrigins"> the allowed origins for the server to check against </param>
    /// DataRow 1: invalid because no origins present
    /// DataRow 2: invalid because of mismatched scheme (http vs https)
    /// DataRow 3: invalid because specific host is not present (* does not resolve to all origins if it is not the sole value supplied - expected, see https://docs.microsoft.com/en-us/cli/azure/webapp/cors?view=azure-cli-latest)
    /// </summary>
    [TestMethod]
    [DataRow(new string[] { "" }, DisplayName = "Test invalid origin empty origins")]
    [DataRow(new string[] { "https://localhost:3000" }, DisplayName = "Test invalid origin mismatch scheme")]
    [DataRow(new string[] { "*", "" }, DisplayName = "Test invalid origin ignored wildcard")]
    public async Task TestAllowOriginHeaderAbsent(string[] allowedOrigins)
    {
        IHost host = await CreateCorsConfiguredWebHost(allowedOrigins, false);

        TestServer server = host.GetTestServer();
        HttpContext returnContext = await server.SendAsync(context =>
        {
            KeyValuePair<string, StringValues> originHeader = new("Origin", "http://localhost:3000");
            context.Request.Headers.Add(originHeader);
        });
        Assert.AreEqual<int>(expected: 0, actual: returnContext.Response.Headers.AccessControlAllowOrigin.Count);
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Spins up a minimal Cors-configured WebHost using the same method as Startup
    /// <param name="testOrigins"/> The allowed origins the test server will respond with an Access-Control-Allow-Origin header </param>
    /// <param name="allowCredentials"/> Whether the test server should allow credentials to be included in requests </param>
    /// </summary>
    public static async Task<IHost> CreateCorsConfiguredWebHost(string[] testOrigins, bool allowCredentials)
    {
        string MyAllowSpecificOrigins = "MyAllowSpecificOrigins";
        return await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseTestServer()
                    .ConfigureServices(services =>
                    {
                        services.AddCors(options =>
                        {
                            options.AddPolicy(name: MyAllowSpecificOrigins,
                                CORSPolicyBuilder =>
                                {
                                    Startup.ConfigureCors(CORSPolicyBuilder, new CorsOptions(testOrigins, allowCredentials));
                                });
                        });
                    })
                    .Configure(app =>
                    {
                        app.UseCors(MyAllowSpecificOrigins);
                    });
            })
            .StartAsync();

    }

    #endregion

}
