// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Tests.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization
{
    /// <summary>
    /// 
    /// </summary>
    [TestClass]
    public class ClientRoleHeaderAuthorizationMiddlewareTests
    {
        /// <summary>
        /// Tests that ClientRoleHeaderAuthorizationMiddleware rejects
        /// requests where the ClaimsPrincipal user object's role membership
        /// does not include the role defined in the client role header.
        /// HTTP Status expected to be 403 Forbidden.
        /// Anonymous requests will have any client role header overwritten as
        /// anonymous, and will succeed, which justifies setting expectAuthorized=true.
        /// </summary>
        /// <param name="addAuthenticated">Whether to conditionally add the authenticated and/or other custom roles</param>
        /// <param name="assignedUserRole">Role name to add to the token role membership payload</param>
        /// <param name="clientRoleHeader">Role name to include in client role header.</param>
        /// <param name="expectAuthorized">Expect HTTP 200 vs. 403 response code.</param>
        /// <returns></returns>
        [TestMethod]
        [DataRow(false, "", "Anonymous", true,
            DisplayName = "Anonymous Request w/ 'anonymous client role header -> 200")]
        [DataRow(false, "", "PrivateRole", true,
            DisplayName = "Anonymous Request where specified client role header is overwritten as anonymous -> 200")]
        [DataRow(true, "PrivateRole", "PrivateRole", true,
            DisplayName = "Authenticated Request where client role header does matches role membership -> 200")]
        [DataRow(true, "Anonymous", "Anonymous", true,
            DisplayName = "Authenticated Request where 'anonymous' client role header matches role membership -> 200")]
        [DataRow(true, "Authenticated", "Anonymous", true,
            DisplayName = "Authenticated Request specifying 'anonymous' client role header -> 200")]
        [DataRow(true, "Anonymous", "Authenticated", true,
            DisplayName = "Authenticated Request w/ custom client role header -> 200")]
        [DataRow(true, "PrivateRole", "OtherRole", false,
            DisplayName = "Authenticated Request where client role header does not match role membership -> 403")]
        public async Task TestEasyAuthRoleHeaderAuthorization(
            bool addAuthenticated,
            string assignedUserRole,
            string clientRoleHeader,
            bool expectAuthorized)
        {
            // Static Web Apps (SWA) Tokens will always contain the anonymous role.
            // https://learn.microsoft.com/en-us/azure/static-web-apps/authentication-authorization
            string generatedToken = AuthTestHelper.CreateStaticWebAppsEasyAuthToken(
                addAuthenticated,
                specificRole: assignedUserRole);

            HttpContext postMiddlewareContext = await EasyAuthAuthenticationUnitTests.SendRequestAndGetHttpContextState(
                generatedToken,
                EasyAuthType.StaticWebApps,
                clientRoleHeader: clientRoleHeader,
                useAuthorizationMiddleware: true);

            ValidateHttpContextMetadata(postMiddlewareContext, expectAuthorized);
        }

        private static void ValidateHttpContextMetadata(HttpContext context, bool expectAuthorized)
        {
            Assert.IsNotNull(context.User.Identity);
            Assert.AreEqual(
                expected: expectAuthorized ? (int)HttpStatusCode.OK : (int)HttpStatusCode.Forbidden,
                actual: context.Response.StatusCode);
        }
    }
}
