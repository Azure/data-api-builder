using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests.Authorization.REST
{
    /// <summary>
    /// Unit tests performed on the RestAuthorizationHandler that confirm
    /// the AuthorizationResult is Success/Failure where expected.
    /// </summary>
    [TestClass]
    public class RestAuthorizationHandlerUnitTests
    {
        /// <summary>
        /// Validates RestAuthorizationHandler computes expected AuthorizationResult(success/failure)
        /// from the result of a response from the AuthorizationResolver.
        ///
        /// If the AuthorizationResolver returns true for IsValidRoleContext,
        /// then the AuthorizationResult for RoleContextPermissionsRequirement is "Success"
        /// </summary>
        /// <param name="expectedAuthorizationResult"></param>
        /// <param name="isValidRoleContext"></param>
        /// <returns></returns>
        [DataTestMethod]
        [DataRow(true, true, DisplayName = "Valid Role Context Succeeds Authorization")]
        [DataRow(false, false, DisplayName = "Invalid Role Context Fails Authorization")]
        [TestMethod]
        public async Task RoleContextPermissionsRequirementTest(bool expectedAuthorizationResult, bool isValidRoleContext)
        {
            Mock<IAuthorizationResolver> authorizationResolver = new();
            authorizationResolver.Setup(x => x.IsValidRoleContext(It.IsAny<HttpContext>())).Returns(isValidRoleContext);

            HttpContext httpContext = CreateHttpContext();

            bool actualAuthorizationResult = await IsAuthorizationSuccessful(
                requirement: new RoleContextPermissionsRequirement(),
                resource: httpContext,
                resolver: authorizationResolver.Object,
                httpContext: httpContext);

            Assert.AreEqual(expectedAuthorizationResult, actualAuthorizationResult);
        }

        #region Helper Methods
        /// <summary>
        /// Setup request and authorization context and get Authorization result
        /// </summary>
        /// <param name="entityName">Table/Entity that is being queried.</param>
        /// <param name="user">ClaimsPrincipal / user that has authentication status defined.</param>
        /// <returns></returns>
        private static async Task<bool> IsAuthorizationSuccessful(
            IAuthorizationRequirement requirement,
            object resource,
            IAuthorizationResolver resolver,
            HttpContext httpContext)
        {
            // Setup Mock and Stub Objects
            ClaimsPrincipal user = new(new ClaimsIdentity(authenticationType: "Bearer"));
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

            AuthorizationHandlerContext context = new(new List<IAuthorizationRequirement> { requirement }, user, resource);
            RestAuthorizationHandler handler = new(resolver, httpContextAccessor.Object);

            await handler.HandleAsync(context);

            return context.HasSucceeded;
        }

        /// <summary>
        /// Create Mock HttpContext object for use in test fixture.
        /// </summary>
        /// <returns></returns>
        private static HttpContext CreateHttpContext()
        {
            Mock<HttpContext> httpContext = new();
            return httpContext.Object;
        }
        #endregion
    }
}
