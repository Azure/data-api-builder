// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Service.Tests.Authentication.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authentication
{
    /// <summary>
    /// Tests the behavior of using SimulatorAuthenticationHandler
    /// for authentication and that it properly injects an authenticated
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
        /// Creates the TestServer with the minimum middleware setup necessary to
        /// test the "Simulator" authentication provider's authentication mechanisms.
        /// Sends a request with a clientRoleHeader to the TestServer created.
        /// </summary>
        /// <param name="clientRole">Name of role to include in header.</param>
        public static async Task<HttpContext> SendRequestAndGetHttpContextState(string? clientRole = null)
        {
            using IHost host = await WebHostBuilderHelper.CreateWebHost(
                provider: AuthenticationOptions.SIMULATOR_AUTHENTICATION,
                useAuthorizationMiddleware: true);

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
