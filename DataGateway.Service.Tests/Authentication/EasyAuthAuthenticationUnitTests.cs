#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Runtime.Serialization.Json;
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
    [TestClass]
    public class EasyAuthAuthenticationUnitTests
    {
        #region Positive Tests
        /// <summary>
        /// Ensures a valid EasyAuth header/value does NOT result in HTTP 401 Unauthorized response.
        /// 403 is okay, as it indicates authorization level failure, not authentication.
        /// </summary>
        [TestMethod]
        public async Task TestValidEasyAuthToken()
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
        /// Ensures an invalid/no EasyAuth header/value results in HTTP 401 Unauthorized response.
        /// 403 is NOT okay here, this indicates authentication incorrectly succeeded, and authorization
        /// rules are being checked.
        /// </summary>
        /// <param name="token">EasyAuth header value</param>
        /// <returns></returns>
        [DataTestMethod]
        [DataRow("", DisplayName = "No EasyAuth header value provided")]
        [DataRow("ey==", DisplayName = "Corrupt EasyAuth header value provided")]
        [DataRow(null, DisplayName = "No EasyAuth header provided")]
        [TestMethod]
        public async Task TestInvalidEasyAuthToken(string token)
        {
            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(token);
            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.IsFalse(postMiddlewareContext.User.Identity.IsAuthenticated);
            Assert.AreEqual(expected: (int)HttpStatusCode.Forbidden, actual: postMiddlewareContext.Response.StatusCode);
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
                            //app.UseMiddleware<JwtAuthenticationMiddleware>();

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
        /// <returns></returns>
        private static async Task<HttpContext> SendRequestAndGetHttpContextState(string? token)
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
                Val = "apple@contoso.com",
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
