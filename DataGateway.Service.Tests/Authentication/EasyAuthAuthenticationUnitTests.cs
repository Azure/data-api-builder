#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.AuthenticationHelpers;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataGateway.Service.AuthenticationHelpers.EasyAuthAuthentication;

namespace Azure.DataGateway.Service.Tests.Authentication
{
    /// <summary>
    /// Tests how the runtime handles the presence/no presence of an EasyAuth header
    /// when EasyAuth is configured for authentication.
    /// </summary>
    [TestClass]
    public class EasyAuthAuthenticationUnitTests
    {
        #region Positive Tests
        /// <summary>
        /// Ensures a valid EasyAuth header/value does NOT result in HTTP 401 Unauthorized response.
        /// 403 is okay, as it indicates authorization level failure, not authentication.
        /// When an authorization header is sent, it contains an invalid value, if the runtime returns an error
        /// then there is improper JWT validation occurring.
        /// </summary>
        [DataTestMethod]
        [DataRow(false, DisplayName = "Valid EasyAuth header only")]
        [DataRow(true, DisplayName = "Valid EasyAuth header and authorization header")]
        [TestMethod]
        public async Task TestValidEasyAuthToken(bool sendAuthorizationHeader)
        {
            string generatedToken = CreateEasyAuthToken();
            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(generatedToken);
            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.IsTrue(postMiddlewareContext.User.Identity.IsAuthenticated);
            Assert.AreEqual(expected: (int)HttpStatusCode.OK, actual: postMiddlewareContext.Response.StatusCode);
        }
        #endregion
        #region Negative Tests
        /// <summary>
        /// - Ensures an invalid/no EasyAuth header/value results in HTTP 401 Unauthorized response.
        /// 403 is NOT okay here, this indicates authentication incorrectly succeeded, and authorization
        /// rules are being checked.
        /// - Also, validate that if other auth headers are present (Authorization Bearer token), that it is never considered
        /// when the runtime is configured for EasyAuth authentication.
        /// </summary>
        /// <param name="token">EasyAuth header value</param>
        /// <returns></returns>
        [DataTestMethod]
        [DataRow("", DisplayName = "No EasyAuth header value provided")]
        [DataRow("ey==", DisplayName = "Corrupt EasyAuth header value provided")]
        [DataRow(null, DisplayName = "No EasyAuth header provided")]
        [DataRow("", true, DisplayName = "No EasyAuth header value provided, include authorization header")]
        [DataRow("ey==", true, DisplayName = "Corrupt EasyAuth header value provided, include authorization header")]
        [DataRow(null, true, DisplayName = "No EasyAuth header provided, include authorization header")]
        [TestMethod]
        public async Task TestInvalidEasyAuthToken(string token, bool sendAuthorizationHeader = false)
        {
            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(token, sendAuthorizationHeader);
            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.IsFalse(postMiddlewareContext.User.Identity.IsAuthenticated);
            Assert.AreEqual(expected: (int)HttpStatusCode.Unauthorized, actual: postMiddlewareContext.Response.StatusCode);
        }

        #endregion
        #region Helper Methods
        /// <summary>
        /// Configures test server with bare minimum middleware
        /// </summary>
        /// <returns>IHost</returns>
        private static async Task<IHost> CreateWebHostEasyAuth()
        {
            return await new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .ConfigureServices(services =>
                        {
                            services.AddAuthentication(defaultScheme: EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME)
                                .AddEasyAuthAuthentication();
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
                            app.UseMiddleware<AuthenticationMiddleware>();

                            // app.Run acts as terminating middleware to return 200 if we reach it. Without this,
                            // the Middleware pipeline will return 404 by default.
                            app.Run(async (context) =>
                            {
                                context.Response.StatusCode = (int)HttpStatusCode.OK;
                                await context.Response.WriteAsync("Successfully validated token!");
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
        /// <param name="token">The EasyAuth header value(base64 encoded token) to test against the TestServer</param>
        /// <param name="sendAuthorizationHeader">Whether to add authorization header to header dictionary</param>
        /// <returns></returns>
        private static async Task<HttpContext> SendRequestAndGetHttpContextState(string? token, bool sendAuthorizationHeader = false)
        {
            using IHost host = await CreateWebHostEasyAuth();
            TestServer server = host.GetTestServer();

            return await server.SendAsync(context =>
            {
                if (token is not null)
                {
                    StringValues headerValue = new(new string[] { $"{token}" });
                    KeyValuePair<string, StringValues> easyAuthHeader = new(EasyAuthAuthentication.EASYAUTHHEADER, headerValue);
                    context.Request.Headers.Add(easyAuthHeader);
                }

                if (sendAuthorizationHeader)
                {
                    KeyValuePair<string, StringValues> easyAuthHeader = new("Authorization", "Bearer eyxyz");
                    context.Request.Headers.Add(easyAuthHeader);
                }

                context.Request.Scheme = "https";
            });
        }

        /// <summary>
        /// Creates a mocked EasyAuth token, namely, the value of the header injected by EasyAuth.
        /// </summary>
        /// <returns>A Base64 encoded string of a serialized EasyAuthClientPrincipal object</returns>
        private static string CreateEasyAuthToken()
        {
            EasyAuthClaim emailClaim = new()
            {
                Val = "apple@contoso.com",
                Typ = ClaimTypes.Upn
            };

            EasyAuthClaim roleClaim = new()
            {
                Val = "Anonymous",
                Typ = ClaimTypes.Role
            };

            List<EasyAuthClaim> claims = new();
            claims.Add(emailClaim);
            claims.Add(roleClaim);

            EasyAuthClientPrincipal token = new()
            {
                Auth_typ = "aad",
                Name_typ = "Apple Banana",
                Claims = claims,
                Role_typ = ClaimTypes.Role
            };

            string serializedToken = JsonSerializer.Serialize(value: token);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(serializedToken));
        }
        #endregion
    }
}
