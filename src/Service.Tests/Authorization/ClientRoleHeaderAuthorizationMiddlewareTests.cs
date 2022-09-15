using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
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
        /// </summary>
        /// <param name="addAuthenticated">Whether to conditionally add the authenticated and/or other custom roles</param>
        /// <param name="assignedUserRole">Role name to add to the token role membership payload</param>
        /// <param name="clientRoleHeader">Role name to include in client role header.</param>
        /// <param name="expectAuthorized">Expect HTTP 200 vs. 403 response code.</param>
        /// <returns></returns>
        [DataTestMethod]
        [DataRow(true, "PrivateRole", "PrivateRole", true)]
        [DataRow(true, "Anonymous", "Anonymous", true)]
        [DataRow(true, "Authenticated", "Anonymous", true)]
        [DataRow(true, "Anonymous", "Authenticated", true)]
        [DataRow(true, "PrivateRole", "OtherRole", false)]
        public async Task TestEasyAuthRoleHeaderAuthorization(
            bool addAuthenticated,
            string assignedUserRole,
            string clientRoleHeader,
            bool expectAuthorized)
        {
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
