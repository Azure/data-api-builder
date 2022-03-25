using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Services;
using Azure.DataGateway.Service.Tests.SqlTests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests.REST
{
    /// <summary>
    /// Tests that the RequestAuthorizationHandler issues correct AuthZ decisions for REST endpoints.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class RequestAuthorizationHandlerUnitTests : SqlTestBase
    {
        private Mock<SqlGraphQLFileMetadataProvider> _metadataStore;

        [ClassInitialize]
        public static async Task InitializeTestFixture(TestContext context)
        {
            await InitializeTestFixture(context, TestCategory.MSSQL);
        }

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

            SetupTable(HttpMethod.Get.ToString(), AuthorizationType.Anonymous);

            bool result = await IsAuthorizationSuccessful(entityName: "books", user);

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
            //Create Authenticated user by defining authenticationType
            ClaimsPrincipal user = new(new ClaimsIdentity(authenticationType: "aad"));

            SetupTable(HttpMethod.Get.ToString(), AuthorizationType.Authenticated);

            bool result = await IsAuthorizationSuccessful(entityName: "books", user);

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
            //Create Unauthenticated user by NOT defining authenticationType
            ClaimsPrincipal user = new(new ClaimsIdentity());

            SetupTable(HttpMethod.Get.ToString(), AuthorizationType.Authenticated);

            bool result = await IsAuthorizationSuccessful(entityName: "books", user);

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
            RequestAuthorizationHandler handler = new(_metadataStore.Object);

            await handler.HandleAsync(context);

            return context.HasSucceeded;
        }

        /// <summary>
        /// Create Test method table definition with operation and authorization rules defined.
        /// </summary>
        /// <param name="httpOperation">Allowed Http HttpVerbs for table,</param>
        /// <param name="authZType">AuthorizationType for Http Operation for table.</param>
        private void SetupTable(string httpOperation, AuthorizationType authZType)
        {
            TableDefinition table = new();
            table.HttpVerbs.Add(httpOperation, CreateAuthZRule(authZType));

            _metadataStore = new Mock<SqlGraphQLFileMetadataProvider>(_metadataStoreProvider);
            _metadataStore.Setup(x => x.GetTableDefinition(It.IsAny<string>())).Returns(table);
        }

        /// <summary>
        /// Create authorization rule for the TableDefinition's Operation.
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
