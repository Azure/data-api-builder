using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Models.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.Authorization
{
    [TestClass]
    public class RestAuthorizationHandlerUnitTests
    {
        private IAuthorizationResolver _authorizationResolver;
        private IHttpContextAccessor _contextAccessor;

        [ClassInitialize]
        public void InitializeTestFixture(
            IAuthorizationResolver authZResolver,
            IHttpContextAccessor httpContextAccessor)
        {
            _authorizationResolver = authZResolver;
            _contextAccessor = httpContextAccessor;
        }
        // Mock
        // IAuthorizationResolver authZResolver,
        // IHttpContextAccessor httpContextAccessor)

        // Implement
        // Authorization Context
        // AuthZ metadata?

        // RestAuthHandler needs to be tested for
        // - only allows one requirement at a time
        // - if one requirement fails, entire context fails
        // - if one requirement fails, then no others should be allowed to pass the context?
        // - if one requirement passes, entire context does not pass?
        [TestMethod]
        public async void StageOneTest()
        {
            // Create Authenticated user by defining authenticationType
            // Bearer adheres to JwtBearerDefaults.AuthenticationScheme constant
            ClaimsPrincipal user = new(new ClaimsIdentity(authenticationType: "Bearer"));

            bool result = await IsAuthorizationSuccessful(entityName: "books", user);

            Assert.IsTrue(result);
        }

        #region Helper Methods
        /// <summary>
        /// Setup request and authorization context and get Authorization result
        /// </summary>
        /// <param name="entityName">Table/Entity that is being queried.</param>
        /// <param name="user">ClaimsPrincipal / user that has authentication status defined.</param>
        /// <returns></returns>
        private async Task<bool> IsAuthorizationSuccessful(string entityName, ClaimsPrincipal user)
        {
            AuthorizationMetadata authZData = new(
                RoleName: "Reader",
                EntityName: entityName,
                ActionName: "Create",
                Columns: new List<string>(new string[] { "id", "title", "publisherId" }));

            AuthorizationHandlerContext context = new(new List<IAuthorizationRequirement> { new Stage1PermissionsRequirement() }, user, authZData);
            RestAuthorizationHandler handler = new(_authorizationResolver, _contextAccessor);

            await handler.HandleAsync(context);

            return context.HasSucceeded;
        }
        #endregion
    }
}
