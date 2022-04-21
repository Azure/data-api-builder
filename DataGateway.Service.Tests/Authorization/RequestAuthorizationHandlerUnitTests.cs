using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests.Authorization
{
    /// <summary>
    /// Tests that the RequestAuthorizationHandler issues correct AuthZ decisions for REST endpoints.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class RequestAuthorizationHandlerUnitTests
    {
        private Mock<SqlGraphQLFileMetadataProvider> _metadataStore;
        private const string TEST_ENTITY = "TEST_ENTITY";

        #region Positive Tests
        /// <summary>
        /// Unauthenticated GET request to table with anonymous GET operations allowed
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task UnauthenticatedGetRequestToAnonymousGetEntity()
        {
            //Create Unauthenticated user by NOT defining authenticationType
            ClaimsPrincipal user = new(new ClaimsIdentity());

            SetupTable(HttpMethod.Get.ToString(), authZType: AuthorizationType.Anonymous);

            bool result = await IsAuthorizationSuccessful(entityName: TEST_ENTITY, user);

            //Evaluate Result
            Assert.IsTrue(result);
        }

        /// <summary>
        /// Authenticated GET request to table with anonymous GET operations allowed. Should still work.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task AuthenticatedGetRequestToAnonymousGetEntity()
        {
            // Create Authenticated user by defining authenticationType
            // Bearer adheres to JwtBearerDefaults.AuthenticationScheme constant
            ClaimsPrincipal user = new(new ClaimsIdentity(authenticationType: "Bearer"));

            SetupTable(HttpMethod.Get.ToString(), authZType: AuthorizationType.Anonymous);

            bool result = await IsAuthorizationSuccessful(entityName: TEST_ENTITY, user);

            Assert.IsTrue(result);
        }
        #endregion
        #region Negative Tests
        /// <summary>
        /// Unauthenticated GET request to table with Authenticated GET operations enforced.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task AnonymousGetRequestToAuthenticatedGetEntity()
        {
            // Create Unauthenticated user by NOT defining authenticationType
            ClaimsPrincipal user = new(new ClaimsIdentity());

            SetupTable(HttpMethod.Get.ToString(), authZType: AuthorizationType.Authenticated);

            bool result = await IsAuthorizationSuccessful(entityName: TEST_ENTITY, user);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task TableDef_HttpVerbCountZero_DoesNotDefaultAnonymous()
        {
            // Create Unauthenticated user by NOT defining authenticationType
            // User will be populated in httpContext but IsAuthenticated() is FALSE.
            ClaimsPrincipal user = new(new ClaimsIdentity());

            // Create table with no HttpVerbs (permissions) config
            SetupTable(HttpMethod.Get.ToString(), setupHttpVerbs: false);

            bool result = await IsAuthorizationSuccessful(entityName: TEST_ENTITY, user);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task TableDef_HasPerms_NoMatchingAction()
        {
            // Create Unauthenticated user by NOT defining authenticationType
            // User will be populated in httpContext but IsAuthenticated() is FALSE.
            ClaimsPrincipal user = new(new ClaimsIdentity());

            // Create table with permission config that does not match request action.
            // Request is GET by default, so POST will not match.
            SetupTable(HttpMethod.Post.ToString());

            bool result = await IsAuthorizationSuccessful(entityName: TEST_ENTITY, user);

            Assert.IsFalse(result);
        }

        [TestMethod]
        public async Task TableDef_HasHttpVerbs_NoAuthNTypeDefinedForRule()
        {
            // Create Unauthenticated user by NOT defining authenticationType
            // User will be populated in httpContext but IsAuthenticated() is FALSE.
            ClaimsPrincipal user = new(new ClaimsIdentity());

            // httpOperation matches config. Though defaultAuthZRule is true,
            // the result should be false because AuthorizationType enum default is
            // NoAccess.
            SetupTable(HttpMethod.Get.ToString(), defaultAuthZRule: true);

            bool result = await IsAuthorizationSuccessful(entityName: TEST_ENTITY, user);

            Assert.IsFalse(result);
        }
        #endregion
        #region Helper Methods
        /// <summary>
        /// Setup request and authorization context and get Authorization result
        /// </summary>
        /// <param name="entityName">Table/Entity that is being queried.</param>
        /// <param name="user">ClaimsPrincipal / user that has authentication status defined.</param>
        /// <returns></returns>
        private async Task<bool> IsAuthorizationSuccessful(string entityName, ClaimsPrincipal user)
        {
            FindRequestContext request = new(entityName, isList: false);
            AuthorizationHandlerContext context = new(new List<IAuthorizationRequirement> { HttpRestVerbs.GET }, user, request);
            RequestAuthorizationHandler handler =
                new(_metadataStore.Object,
                isMock: true); // indicates the metadata provider specified is a mock object.

            await handler.HandleAsync(context);

            return context.HasSucceeded;
        }

        /// <summary>
        /// Create Test method table definition with operation and authorization rules defined.
        /// If defaultAuthZRule is true, then an AuthorizationRule is created using the first
        /// value in the AuthorizationType enum which is NoAccess.
        /// Using first value in enum imitates scenario when config does not set value
        /// for AuthorizationType.
        /// </summary>
        /// <param name="httpOperation">Allowed Http HttpVerbs for table,</param>
        /// <param name="setupHttpPerms">TableDefinition.HttpVerbs is populated/not null</param>
        /// <param name="authZType">AuthorizationType for Http Operation for table.</param>
        private void SetupTable(
            string httpOperation,
            bool setupHttpVerbs = true,
            bool defaultAuthZRule = false,
            AuthorizationType authZType = AuthorizationType.NoAccess)
        {
            TableDefinition table = new();

            if (setupHttpVerbs && !defaultAuthZRule)
            {
                table.HttpVerbs.Add(httpOperation, CreateAuthZRule(authZType));
            }
            else if (defaultAuthZRule)
            {
                table.HttpVerbs.Add(httpOperation, new AuthorizationRule());
            }

            _metadataStore = new Mock<SqlGraphQLFileMetadataProvider>();
            _metadataStore.Setup(x => x.GetTableDefinition(It.IsAny<string>())).Returns(table);
        }

        /// <summary>
        /// Create authorization rule for the TableDefinition's Operation,
        /// that is configured with the passed in AuthorizationType.
        /// </summary>
        /// <param name="authZType"></param>
        /// <returns></returns>
        private static AuthorizationRule CreateAuthZRule(AuthorizationType authZType)
        {
            AuthorizationRule rule = new();
            rule.AuthorizationType = authZType;
            return rule;
        }
        #endregion
    }
}
