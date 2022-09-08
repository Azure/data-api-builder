#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.AuthenticationHelpers;
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
using static Azure.DataApiBuilder.Service.AuthenticationHelpers.StaticWebAppsAuthentication;

namespace Azure.DataApiBuilder.Service.Tests.Authentication
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
        /// Ensures a valid AppService EasyAuth header/value does NOT result in HTTP 401 Unauthenticated response.
        /// 403 is okay, as it indicates authorization level failure, not authentication.
        /// When an authorization header is sent, it contains an invalid value, if the runtime returns an error
        /// then there is improper JWT validation occurring.
        /// </summary>
        [DataTestMethod]
        [DataRow(false, DisplayName = "Valid AppService EasyAuth header only")]
        [DataRow(true, DisplayName = "Valid AppService EasyAuth header and authorization header")]
        [TestMethod]
        public async Task TestValidAppServiceEasyAuthToken(bool sendAuthorizationHeader)
        {
            string generatedToken = AuthTestHelper.CreateAppServiceEasyAuthToken();
            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(
                generatedToken,
                EasyAuthType.AppService,
                sendAuthorizationHeader);
            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.IsTrue(postMiddlewareContext.User.Identity.IsAuthenticated);
            Assert.AreEqual(expected: (int)HttpStatusCode.OK,
                actual: postMiddlewareContext.Response.StatusCode);
            Assert.AreEqual(
                expected: AuthorizationType.Authenticated.ToString(),
                actual: postMiddlewareContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER],
                ignoreCase: true);
        }

        /// <summary>
        /// Ensures a valid StaticWebApps EasyAuth header/value does NOT result in HTTP 401 Unauthorized response.
        /// 403 is okay, as it indicates authorization level failure, not authentication.
        /// When an authorization header is sent, it contains an invalid value, if the runtime returns an error
        /// then there is improper JWT validation occurring.
        /// </summary>
        [DataTestMethod]
        [DataRow(false, true, DisplayName = "Valid StaticWebApps EasyAuth header only")]
        [DataRow(true, true, DisplayName = "Valid StaticWebApps EasyAuth header and authorization header")]
        [TestMethod]
        public async Task TestValidStaticWebAppsEasyAuthToken(bool sendAuthorizationHeader, bool addAuthenticated)
        {
            string generatedToken = AuthTestHelper.CreateStaticWebAppsEasyAuthToken(addAuthenticated);
            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(
                generatedToken,
                EasyAuthType.StaticWebApps,
                sendAuthorizationHeader);
            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.IsTrue(postMiddlewareContext.User.Identity.IsAuthenticated);
            Assert.AreEqual(
                expected: (int)HttpStatusCode.OK,
                actual: postMiddlewareContext.Response.StatusCode);
            Assert.AreEqual(
                expected: AuthorizationType.Authenticated.ToString(),
                actual: postMiddlewareContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER],
                ignoreCase: true);
        }

        /// <summary>
        /// Ensures SWA token payload claims are processed by
        /// validating that those claims are present
        /// on the authenticated .NET ClaimsPrincipal object.
        /// Demonstrates using the immutable claim values tid and oid
        /// as a combined key for uniquely identifying the API's data
        /// and determining whether a user should be granted access to that data.
        /// </summary>
        /// <seealso cref="https://docs.microsoft.com/azure/active-directory/develop/access-tokens#validate-user-permission"/>
        [TestMethod]
        public async Task TestStaticWebAppsEasyAuthTokenClaims()
        {
            string objectIdClaimType = "oid";
            string objectId = "f35eaa76-b8e6-4c7c-99a2-5aeeeee9ba58";

            string tenantIdClaimType = "tid";
            string tenantId = "8f902aef-2c06-42c9-a3d0-bc31f04a3dca";

            List<SWAPrincipalClaim> payloadClaims = new();
            payloadClaims.Add(new SWAPrincipalClaim() { Typ = objectIdClaimType, Val = objectId });
            payloadClaims.Add(new SWAPrincipalClaim() { Typ = tenantIdClaimType, Val = tenantId });

            string generatedToken = AuthTestHelper.CreateStaticWebAppsEasyAuthToken(
                addAuthenticated: true,
                claims: payloadClaims
                );

            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(
                generatedToken,
                EasyAuthType.StaticWebApps);

            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.IsTrue(postMiddlewareContext.User.Identity.IsAuthenticated);
            Assert.AreEqual(expected: true, actual: postMiddlewareContext.User.HasClaim(type: objectIdClaimType, value: objectId));
            Assert.AreEqual(expected: true, actual: postMiddlewareContext.User.HasClaim(type: tenantIdClaimType, value: tenantId));
            Assert.AreEqual(expected: (int)HttpStatusCode.OK, actual: postMiddlewareContext.Response.StatusCode);
        }

        /// <summary>
        /// Validates that null claim type and/or null claim value
        /// are not processed claims on the .NET ClaimsPrincipal object,
        /// due lack of null claim type/ claim value support on the ClaimsPrincipal.
        /// Validates that empty string for claim type and/or value
        /// is processed successfully.
        /// </summary>
        /// <param name="claimType">string representation of claim type</param>
        /// <param name="claimValue">string representation of claim value</param>
        /// <seealso cref="https://docs.microsoft.com/dotnet/api/system.security.claims.claim.type?view=net-6.0"/>
        /// <seealso cref="https://docs.microsoft.com/dotnet/api/system.security.claims.claim.value?view=net-6.0"/>
        [DataTestMethod]
        [DataRow(null, null, false, DisplayName = "Claim type/value null - not processed")]
        [DataRow("tid", null, false, DisplayName = "Claim value null -  not processed")]
        [DataRow(null, "8f902aef-2c06-42c9-a3d0-bc31f04a3dca", false, DisplayName = "Claim type null - not processed")]
        [DataRow("", "8f902aef-2c06-42c9-a3d0-bc31f04a3dca", true, DisplayName = "Claim type empty string - will process")]
        [DataRow("tid", "", true, DisplayName = "Claim value empty string - will process")]
        [DataRow("", "", true, DisplayName = "Claim type/value empty string -  will process")]

        public async Task TestStaticWebAppsEasyAuth_IncompleteTokenClaims(string? claimType, string? claimValue, bool expectProcessedClaim)
        {
            List<SWAPrincipalClaim> payloadClaims = new();
            payloadClaims.Add(new SWAPrincipalClaim() { Typ = claimType, Val = claimValue });

            string generatedToken = AuthTestHelper.CreateStaticWebAppsEasyAuthToken(
                addAuthenticated: true,
                claims: payloadClaims
                );

            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(
                generatedToken,
                EasyAuthType.StaticWebApps);

            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.IsTrue(postMiddlewareContext.User.Identity.IsAuthenticated);

            Assert.AreEqual(
                expected: expectProcessedClaim,
                actual: postMiddlewareContext.User.Claims
                    .Where(claim => claim.Type == claimType && claim.Value == claimValue)
                    .Any());

            Assert.AreEqual(expected: (int)HttpStatusCode.OK, actual: postMiddlewareContext.Response.StatusCode);
        }

        /// <summary>
        /// When the user request is a valid token but only has an anonymous role,
        /// we still return OK. We assign the client role header to be anonymous.
        /// </summary>
        [TestMethod]
        public async Task TestValidStaticWebAppsEasyAuthTokenWithAnonymousRoleOnly()
        {
            string generatedToken =
                AuthTestHelper.CreateStaticWebAppsEasyAuthToken(addAuthenticated: false);
            HttpContext postMiddlewareContext =
                await SendRequestAndGetHttpContextState(generatedToken, EasyAuthType.StaticWebApps);
            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.IsFalse(postMiddlewareContext.User.Identity.IsAuthenticated);
            Assert.AreEqual(expected: (int)HttpStatusCode.OK, actual: postMiddlewareContext.Response.StatusCode);
            Assert.AreEqual(expected: AuthorizationType.Anonymous.ToString(),
                actual: postMiddlewareContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER],
                ignoreCase: true);
        }

        /// <summary>
        /// Tests we honor the presence of X-MS-API-ROLE header when role is authenticated
        /// otherwise - replace it as anonymous.
        /// </summary>
        [DataTestMethod]
        [DataRow(false, "author",
            DisplayName = "Anonymous role - X-MS-API-ROLE is not honored")]
        [DataRow(true, "author",
            DisplayName = "Authenticated role - existing X-MS-API-ROLE is honored")]
        [TestMethod]
        public async Task TestClientRoleHeaderPresence(bool addAuthenticated, string clientRoleHeader)
        {
            string generatedToken = AuthTestHelper.CreateStaticWebAppsEasyAuthToken(addAuthenticated);
            HttpContext postMiddlewareContext =
                await SendRequestAndGetHttpContextState(
                    generatedToken,
                    EasyAuthType.StaticWebApps,
                    clientRoleHeader: clientRoleHeader);
            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.AreEqual(expected: addAuthenticated, postMiddlewareContext.User.Identity.IsAuthenticated);
            Assert.AreEqual(expected: (int)HttpStatusCode.OK, actual: postMiddlewareContext.Response.StatusCode);
            Assert.AreEqual(expected: addAuthenticated ? clientRoleHeader : AuthorizationType.Anonymous.ToString(),
                actual: postMiddlewareContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER],
                ignoreCase: true);
        }

        /// <summary>
        /// - Ensures an invalid/no EasyAuth header/value results in HTTP 200 OK response
        /// but with the X-MS-API-ROLE assigned to be anonymous.
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
            HttpContext postMiddlewareContext =
                await SendRequestAndGetHttpContextState(
                    token,
                    EasyAuthType.StaticWebApps,
                    sendAuthorizationHeader);
            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.IsFalse(postMiddlewareContext.User.Identity.IsAuthenticated);
            Assert.AreEqual(expected: (int)HttpStatusCode.OK, actual: postMiddlewareContext.Response.StatusCode);
            Assert.AreEqual(expected: AuthorizationType.Anonymous.ToString(),
                actual: postMiddlewareContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER],
                ignoreCase: true);
        }

        /// <summary>
        /// Test to validate that the request is appropriately treated as anonymous/authenticated
        /// in development mode depending on the value feature switch we have in the config file.
        /// </summary>
        /// <param name="treatRequestAsAuthenticated">Boolean value indicating whether to treat the
        /// request as authenticated by default.</param>
        /// <param name="expectedClientRoleHeader">Expected value of X-MS-API-ROLE header.</param>
        /// <param name="clientRoleHeader">Value of X-MS-API-ROLE header specified in request.</param>
        /// <returns></returns>
        [DataTestMethod]
        [DataRow(true, "Authenticated", null,
            DisplayName = "EasyAuth- Treat request as authenticated in development mode")]
        [DataRow(false, "Anonymous", null,
            DisplayName = "EasyAuth- Treat request as anonymous in development mode")]
        [DataRow(true, "author", "author",
            DisplayName = "EasyAuth- Treat request as authenticated in development mode " +
            "and honor the clienRoleHeader")]
        [DataRow(true, "Anonymous", "Anonymous",
            DisplayName = "EasyAuth- Treat request as authenticated in development mode " +
            "and honor the clienRoleHeader even when specified as anonymous")]
        public async Task TestAuthenticatedRequestInDevelopmentMode(
            bool treatRequestAsAuthenticated,
            string expectedClientRoleHeader,
            string clientRoleHeader)
        {
            HttpContext postMiddlewareContext =
                await SendRequestAndGetHttpContextState(
                    token: null,
                    easyAuthType: EasyAuthType.StaticWebApps,
                    clientRoleHeader: clientRoleHeader,
                    treatRequestAsAuthenticated: treatRequestAsAuthenticated);
            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.AreEqual(expected: (int)HttpStatusCode.OK, actual: postMiddlewareContext.Response.StatusCode);
            Assert.AreEqual(expected: expectedClientRoleHeader,
                actual: postMiddlewareContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].ToString());
        }

        #endregion

        #region Helper Methods
        /// <summary>
        /// Configures test server with bare minimum middleware
        /// </summary>
        /// <returns>IHost</returns>
        private static async Task<IHost> CreateWebHostEasyAuth(
            EasyAuthType easyAuthType,
            bool treatAsAuthenticatedRequest)
        {
            // Setup RuntimeConfigProvider object for the pipeline.
            Mock<ILogger<RuntimeConfigProvider>> configProviderLogger = new();
            Mock<RuntimeConfigPath> runtimeConfigPath = new();
            Mock<RuntimeConfigProvider> runtimeConfigProvider = new(runtimeConfigPath.Object,
                configProviderLogger.Object);
            runtimeConfigProvider.Setup(x => x.IsAuthenticatedDevModeRequest()).
                Returns(treatAsAuthenticatedRequest);

            return await new HostBuilder()
                .ConfigureWebHost(webBuilder =>
                {
                    webBuilder
                        .UseTestServer()
                        .ConfigureServices(services =>
                        {
                            services.AddAuthentication(defaultScheme: EasyAuthAuthenticationDefaults.AUTHENTICATIONSCHEME)
                                    .AddEasyAuthAuthentication(easyAuthType);
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
        private static async Task<HttpContext> SendRequestAndGetHttpContextState(
            string? token,
            EasyAuthType easyAuthType,
            bool sendAuthorizationHeader = false,
            string? clientRoleHeader = null,
            bool treatRequestAsAuthenticated = false)
        {
            using IHost host = await CreateWebHostEasyAuth(easyAuthType, treatRequestAsAuthenticated);
            TestServer server = host.GetTestServer();

            return await server.SendAsync(context =>
            {
                if (token is not null)
                {
                    StringValues headerValue = new(new string[] { $"{token}" });
                    KeyValuePair<string, StringValues> easyAuthHeader = new(AuthenticationConfig.CLIENT_PRINCIPAL_HEADER, headerValue);
                    context.Request.Headers.Add(easyAuthHeader);
                }

                if (sendAuthorizationHeader)
                {
                    KeyValuePair<string, StringValues> easyAuthHeader = new("Authorization", "Bearer eyxyz");
                    context.Request.Headers.Add(easyAuthHeader);
                }

                if (clientRoleHeader is not null)
                {
                    KeyValuePair<string, StringValues> easyAuthHeader =
                        new(AuthorizationResolver.CLIENT_ROLE_HEADER, clientRoleHeader);
                    context.Request.Headers.Add(easyAuthHeader);
                }

                context.Request.Scheme = "https";
            });
        }
        #endregion
    }
}
