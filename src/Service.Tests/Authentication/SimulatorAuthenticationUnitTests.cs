#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.AuthenticationHelpers;
using Azure.DataApiBuilder.Service.AuthenticationHelpers.AuthenticationSimulator;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Authentication
{
    /// <summary>
    /// Tests the behavior of using SimulatorAuthenticationHandler
    /// for authentication and that it properly injects and authenticated
    /// ClaimsPrincipal object with role membership containing the client role header
    /// value, if present.
    /// </summary>
    [TestClass]
    public class SimulatorAuthenticationUnitTests
    {
        #region Tests
        /// <summary>
        /// Test to validate that the request is authenticated and that
        /// authorization is evaulated in the context of the role defined
        /// in the client role header when SimulatorAuthentication is configured.
        /// </summary>
        /// <param name="clientRoleHeader">Value of X-MS-API-ROLE header specified in request.</param>
        /// <returns></returns>
        [DataTestMethod]
        [DataRow("Anonymous")]
        [DataRow("Authenticated")]
        [DataRow("Policy_Tester_01")]
        public async Task TestAuthenticatedRequestInDevelopmentMode(string clientRoleHeader)
        {
            HttpContext postMiddlewareContext =
                await SendRequestAndGetHttpContextState(clientRole: clientRoleHeader);

            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.IsTrue(postMiddlewareContext.User.Identity.IsAuthenticated);
            Assert.AreEqual(expected: (int)HttpStatusCode.OK, actual: postMiddlewareContext.Response.StatusCode);
            Assert.AreEqual(expected: clientRoleHeader,
                actual: postMiddlewareContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].ToString());

            Assert.IsTrue(postMiddlewareContext.User.IsInRole(clientRoleHeader));
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Configures test server with bare minimum middleware
        /// </summary>
        /// <returns>IHost</returns>
        public static async Task<IHost> CreateWebHostAuthenticationSimulator()
        {
            // Setup RuntimeConfigProvider object for the pipeline.
            Mock<ILogger<RuntimeConfigProvider>> configProviderLogger = new();
            Mock<RuntimeConfigPath> runtimeConfigPath = new();
            Mock<RuntimeConfigProvider> runtimeConfigProvider = new(runtimeConfigPath.Object,
                configProviderLogger.Object);

            return await new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .ConfigureServices(services =>
                        {
                            services.AddAuthentication(defaultScheme: SimulatorAuthenticationDefaults.AUTHENTICATIONSCHEME)
                                    .AddSimulatorAuthentication();
                            services.AddSingleton(runtimeConfigProvider.Object);
                            services.AddAuthorization();
                        })
                        .ConfigureLogging(o =>
                        {
                            o.AddFilter(levelFilter => levelFilter <= LogLevel.Information);
                            o.AddDebug();
                            o.AddConsole();
                        })
                        .Configure(app =>
                        {
                            app.UseAuthentication();
                            app.UseClientRoleHeaderAuthenticationMiddleware();
                            app.UseAuthorization();
                            app.UseClientRoleHeaderAuthorizationMiddleware();

                            // app.Run acts as terminating middleware to return 200 if we reach it. Without this,
                            // the Middleware pipeline will return 404 by default.
                            app.Run(async (context) =>
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.OK;
                                await context.Response.WriteAsync("Successfully injected authenticated user.");
                                await context.Response.StartAsync();
                            });
                        });
                })
                .StartAsync();
        }

        /// <summary>
        /// Creates the TestServer with the minimum middleware setup necessary to
        /// test EasyAuth authentication mechanisms.
        /// Sends a request with an EasyAuth header to the TestServer created.
        /// </summary>
        /// <param name="clientRole">Name of role to include in header.</param>
        /// <returns></returns>
        public static async Task<HttpContext> SendRequestAndGetHttpContextState(string? clientRole = null)
        {
            using IHost host = await CreateWebHostAuthenticationSimulator();
            TestServer server = host.GetTestServer();

            return await server.SendAsync(context =>
            {
                if (clientRole is not null)
                {
                    KeyValuePair<string, StringValues> clientRoleHeader =
                        new(AuthorizationResolver.CLIENT_ROLE_HEADER, clientRole);
                    context.Request.Headers.Add(clientRoleHeader);
                }

                context.Request.Scheme = "https";
            });
        }
        #endregion
    }
}
