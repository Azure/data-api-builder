using System.Collections.Generic;
using System.Net.Http;
using System.Security.Claims;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests.REST
{
    /// <summary>
    /// Tests that the FindRequestAuthorizationHandler issues correct AuthZ decisions for REST endpoints.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class FindRequestAuthorizationHandlerUnitTests
    {
        private Mock<IMetadataStoreProvider> _metadataStore;
        private static OperationAuthorizationRequirement _isAuthenticatedRequirement;

        [ClassInitialize]
        public static void InitializeTestFixture(TestContext context)
        {
            _isAuthenticatedRequirement = Operations.GET;
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

            bool result = await AuthorizationSuccessful(entityName: "books", user);

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

            bool result = await AuthorizationSuccessful(entityName: "books", user);

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

            bool result = await AuthorizationSuccessful(entityName: "books", user);

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
            AuthorizationHandlerContext context = new(new List<IAuthorizationRequirement> { Operations.GET }, user, request);
            FindRequestAuthorizationHandler handler = new(_metadataStore.Object);

            await handler.HandleAsync(context);

            return context.HasSucceeded;
        }

        /// <summary>
        /// Create Test method table definition with operation and authorization rules defined.
        /// </summary>
        /// <param name="httpOperation">Allowed Http Operations for table,</param>
        /// <param name="authZType">AuthorizationType for Http Operation for table.</param>
        private void SetupTable(string httpOperation, AuthorizationType authZType)
        {
            TableDefinition table = new();
            table.Operations.Add(httpOperation, CreateAuthZRule(authZType));

            _metadataStore = new Mock<IMetadataStoreProvider>();
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
