#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Tests.Authentication.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
        #region Tests
        /// <summary>
        /// Ensures a valid AppService EasyAuth header/value results in HTTP 200 or HTTP 403.
        /// HTTP 401 will not occur when EasyAuth is correctly configured (AppService environment and runtime configuration).
        /// When EasyAuth is configured and an authorization header is sent, the authorization header should be ignored
        /// and zero token validation errors should be observed.
        /// </summary>
        [DataTestMethod]
        [DataRow(false, DisplayName = "Valid AppService EasyAuth payload - 200")]
        [DataRow(true, DisplayName = "Valid AppService EasyAuth header and authorization header - 200")]
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
        /// Invalid AppService EasyAuth payloads elicit a 401 Unauthorized response, indicating failed authentication.
        /// </summary>
        [DataTestMethod]
        [DataRow("", DisplayName = "Empty JSON not serializable to AppServiceClientPrincipal")]
        [DataRow("eyJtZXNzYWdlIjogImhlbGxvIHdvcmxkIn0=", DisplayName = "JSON not serializable to AppServiceClientPrincipal")]
        [DataRow("aGVsbG8sIHdvcmxkIQ==", DisplayName = "Non-JSON Base64 encoded string not serializable to AppServiceClientPrincipal")]
        [DataRow("eyJhdXRoX3R5cCI6IiIsIm5hbWVfdHlwIjoidW5pcXVlX25hbWUiLCJyb2xlX3R5cCI6InJvbGVzIn0=", DisplayName = "Missing value for property Auth_typ")]
        [DataRow("eyJhdXRoX3R5cCI6IG51bGwsIm5hbWVfdHlwIjoidW5pcXVlX25hbWUiLCJyb2xlX3R5cCI6InJvbGVzIn0=", DisplayName = "Null value for property Auth_typ")]
        [DataRow("eyJhdXRoX3R5cCI6ICIiLCJuYW1lX3R5cCI6InVuaXF1ZV9uYW1lIiwicm9sZV90eXAiOiJyb2xlcyJ9", DisplayName = "Empty value for property Auth_typ")]
        public async Task TestInvalidAppServiceEasyAuthToken(string easyAuthPayload)
        {
            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(token: easyAuthPayload, EasyAuthType.AppService);

            Assert.AreEqual(expected: (int)HttpStatusCode.Unauthorized, actual: postMiddlewareContext.Response.StatusCode);
            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.AreEqual(expected: false, actual: postMiddlewareContext.User.Identity.IsAuthenticated);
        }

        /// <summary>
        /// Ensures authentication fails when no EasyAuth header is present because
        /// a correctly configured EasyAuth environment guarantees that only authenticated requests
        /// will contain an EasyAuth header.
        /// </summary>
        /// <param name="easyAuthType">AppService/StaticWebApps</param>
        [DataTestMethod]
        [DataRow(EasyAuthType.AppService)]
        [DataRow(EasyAuthType.StaticWebApps)]
        public async Task TestMissingEasyAuthHeader(EasyAuthType easyAuthType)
        {
            HttpContext postMiddlewareContext = await SendRequestAndGetHttpContextState(token: null, easyAuthType);

            Assert.AreEqual(expected: (int)HttpStatusCode.OK, actual: postMiddlewareContext.Response.StatusCode);
            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.AreEqual(expected: false, actual: postMiddlewareContext.User.Identity.IsAuthenticated);
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
        /// Tests we honor the existing value of the X-MS-API-ROLE header when the ClaimsPrincipal
        /// is a member of the role authenticated. Otherwise, ensure the X-MS-API-ROLE header is
        /// set as 'anonymous'
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

            // Validate state of HttpContext after being processed by authentication middleware.
            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.AreEqual(expected: addAuthenticated, postMiddlewareContext.User.Identity.IsAuthenticated);
            Assert.AreEqual(expected: (int)HttpStatusCode.OK, actual: postMiddlewareContext.Response.StatusCode);
            Assert.AreEqual(expected: addAuthenticated ? clientRoleHeader : AuthorizationType.Anonymous.ToString(),
                actual: postMiddlewareContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER],
                ignoreCase: true);
        }

        /// <summary>
        /// - Ensures an invalid EasyAuth header payload results in HTTP 401 Unauthorized response
        /// A correctly configured EasyAuth environment guarantees an EasyAuth payload for authenticated requests.
        /// - Ensures a missing EasyAuth header results in HTTP OK and User.IsAuthenticated == false.
        /// - Also, validate that if other auth headers are present (Authorization Bearer token), that it is never considered
        /// when the runtime is configured for EasyAuth authentication.
        /// </summary>
        /// <param name="easyAuthPayload">EasyAuth header value</param>
        [DataTestMethod]
        [DataRow("", DisplayName = "No EasyAuth payload -> 401 Unauthorized")]
        [DataRow("ey==", DisplayName = "Invalid EasyAuth payload -> 401 Unauthorized")]
        [DataRow(null, DisplayName = "No EasyAuth header provided -> 200 OK, Anonymous request")]
        [DataRow("", true, DisplayName = "No EasyAuth payload, include authorization header")]
        [DataRow("ey==", true, DisplayName = "Corrupt EasyAuth header value provided, include authorization header")]
        [DataRow(null, true, DisplayName = "No EasyAuth header provided, include authorization header -> 200 OK, Anonymous request")]
        [TestMethod]
        public async Task TestInvalidStaticWebAppsEasyAuthToken(string easyAuthPayload, bool sendAuthorizationHeader = false)
        {
            HttpContext postMiddlewareContext =
                await SendRequestAndGetHttpContextState(
                    easyAuthPayload,
                    EasyAuthType.StaticWebApps,
                    sendAuthorizationHeader);

            // Validate state of HttpContext after being processed by authentication middleware.
            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.IsFalse(postMiddlewareContext.User.Identity.IsAuthenticated);

            // A missing EasyAuth header results in an anonymous request.
            int expectedStatusCode = (easyAuthPayload is not null) ? (int)HttpStatusCode.Unauthorized : (int)HttpStatusCode.OK;
            Assert.AreEqual(expected: expectedStatusCode, actual: postMiddlewareContext.Response.StatusCode);
            string expectedResolvedRoleHeader = (easyAuthPayload is not null) ? string.Empty : AuthorizationResolver.ROLE_ANONYMOUS;
            string actualResolvedRoleHeader = postMiddlewareContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].ToString();
            Assert.AreEqual(expected: expectedResolvedRoleHeader, actual: actualResolvedRoleHeader, ignoreCase: true);
        }

        /// <summary>
        /// Test to validate that the request is appropriately treated as anonymous/authenticated
        /// in development mode depending on the value feature flag we have in the config file.
        /// (Enabled/Disabled) in test DisplayName indicates state of feature flag.
        /// </summary>
        /// <param name="authenticateDevModeRequests">Boolean value indicating whether to turn on feature flag
        /// used to treat requests as authenticated by default.</param>
        /// <param name="expectedClientRoleHeader">Expected value of X-MS-API-ROLE header.</param>
        /// <param name="clientRoleHeader">Value of X-MS-API-ROLE header specified in request.</param>
        [DataTestMethod]
        [DataRow(false, "Anonymous", null, DisplayName = "Disabled, no auth headers -> 401 Unauthorized")]
        [DataRow(true, "Authenticated", null, DisplayName = "Enabled, no auth headers -> clientRoleHeader set to (authenticated)")]
        [DataRow(true, "author", "author", DisplayName = "Enabled, honor the clientRoleHeader (author)")]
        [DataRow(true, "Anonymous", "Anonymous", DisplayName = "Enabled, honor the clientRoleHeader (anonymous)")]
        public async Task AuthenticateDevModeRequests_FeatureFlagTests(
            bool authenticateDevModeRequests,
            string expectedClientRoleHeader,
            string clientRoleHeader)
        {
            HttpContext postMiddlewareContext =
                await SendRequestAndGetHttpContextState(
                    token: null,
                    easyAuthType: EasyAuthType.StaticWebApps,
                    clientRoleHeader: clientRoleHeader,
                    treatRequestAsAuthenticated: authenticateDevModeRequests);

            // Validate state of HttpContext after being processed by authentication middleware.
            Assert.IsNotNull(postMiddlewareContext.User.Identity);
            Assert.AreEqual(expected: (int)HttpStatusCode.OK, actual: postMiddlewareContext.Response.StatusCode);

            string resolvedRoleHeader = postMiddlewareContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].ToString();
            if (!string.IsNullOrWhiteSpace(resolvedRoleHeader))
            {
                Assert.AreEqual(expected: expectedClientRoleHeader, actual: resolvedRoleHeader);
            }

            // Validates that AuthenticationMiddleware adds the clientRoleHeader as a role claim ONLY when the DevModeAuthNFlag is set.
            if (clientRoleHeader is not null)
            {
                Assert.AreEqual(expected: authenticateDevModeRequests, actual: postMiddlewareContext.User.IsInRole(clientRoleHeader));
            }
        }
        #endregion

        #region Helper Methods
        /// <summary>
        /// Creates the TestServer with the minimum middleware setup necessary to
        /// test EasyAuth authentication mechanisms.
        /// Sends a request with an EasyAuth header to the TestServer created.
        /// </summary>
        /// <param name="token">The EasyAuth header value(base64 encoded token) to test against the TestServer</param>
        /// <param name="sendAuthorizationHeader">Whether to add authorization header to header dictionary</param>
        /// <returns></returns>
        public static async Task<HttpContext> SendRequestAndGetHttpContextState(
            string? token,
            EasyAuthType easyAuthType,
            bool sendAuthorizationHeader = false,
            string? clientRoleHeader = null,
            bool treatRequestAsAuthenticated = false,
            bool useAuthorizationMiddleware = false)
        {
            using IHost host = await WebHostBuilderHelper.CreateWebHost(easyAuthType.ToString(), treatRequestAsAuthenticated, useAuthorizationMiddleware);
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
