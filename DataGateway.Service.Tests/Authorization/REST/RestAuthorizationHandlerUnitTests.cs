using System.Collections;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Models;
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

        /// <summary>
        /// Calls the AuthorizationResolver to evaluate whether a role and action are allowed.
        ///     (1) HttpMethod resolves to one or two CRUD Actions, requirement fails when >0 Actions fails the AuthorizationResolver call.
        ///         i.e. PUT resolves to Create and Update
        ///         i.e. GET resolves to Read
        /// </summary>
        /// <param name="httpMethod">Action type of request</param>
        /// <param name="expectedAuthorizationResult">Whether authorization is expected to succeed.</param>
        /// <param name="isValidCreateRoleAction">Whether Role/Action pair is allowed per authorization config.</param>
        /// <param name="isValidReadRoleAction">Whether Role/Action pair is allowed per authorization config.</param>
        /// <param name="isValidUpdateRoleAction">Whether Role/Action pair is allowed per authorization config.</param>
        /// <param name="isValidDeleteRoleAction">Whether Role/Action pair is allowed per authorization config.</param>
        /// <returns></returns>
        [DataTestMethod]
        // Positive Tests
        [DataRow(HttpConstants.POST, true, true, false, false, false, DisplayName = "POST Operation with Create Permissions")]
        [DataRow(HttpConstants.PATCH, true, true, false, true, false, DisplayName = "PATCH Operation with Create,Update permissions")]
        [DataRow(HttpConstants.PUT, true, true, false, true, false, DisplayName = "PUT Operation with create, update permissions.")]
        [DataRow(HttpConstants.GET, true, false, true, false, false, DisplayName = "GET Operation with read permissions")]
        [DataRow(HttpConstants.DELETE, true, false, false, false, true, DisplayName = "DELETE Operation with delete permissions")]
        // Negative Tests
        [DataRow(HttpConstants.PUT, false, false, false, false, false, DisplayName = "PUT Operation with no permissions")]
        [DataRow(HttpConstants.PUT, false, true, false, false, false, DisplayName = "PUT Operation with create permissions")]
        [DataRow(HttpConstants.PUT, false, false, false, true, false, DisplayName = "PUT Operation with update permissions")]
        [DataRow(HttpConstants.PATCH, false, false, false, false, false, DisplayName = "PATCH Operation with no permissions")]
        [DataRow(HttpConstants.PATCH, false, true, false, false, false, DisplayName = "PATCH Operation with create permissions")]
        [DataRow(HttpConstants.PATCH, false, false, false, true, false, DisplayName = "PATCH Operation with update permissions")]
        [DataRow(HttpConstants.DELETE, false, false, false, false, false, DisplayName = "DELETE Operation with no permissions")]
        [DataRow(HttpConstants.GET, false, false, false, false, false, DisplayName = "GET Operation with create permissions")]
        [DataRow(HttpConstants.POST, false, false, false, false, false, DisplayName = "POST Operation with update permissions")]
        [TestMethod]
        public async Task EntityRoleActionPermissionsRequirementTest(
            string httpMethod,
            bool expectedAuthorizationResult,
            bool isValidCreateRoleAction,
            bool isValidReadRoleAction,
            bool isValidUpdateRoleAction,
            bool isValidDeleteRoleAction)
        {
            Mock<IAuthorizationResolver> authorizationResolver = new();
            authorizationResolver.Setup(x => x.AreRoleAndActionDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.CREATE
                )).Returns(isValidCreateRoleAction);
            authorizationResolver.Setup(x => x.AreRoleAndActionDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.READ
                )).Returns(isValidReadRoleAction);
            authorizationResolver.Setup(x => x.AreRoleAndActionDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.UPDATE
                )).Returns(isValidUpdateRoleAction);
            authorizationResolver.Setup(x => x.AreRoleAndActionDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.DELETE
                )).Returns(isValidDeleteRoleAction);

            HttpContext httpContext = CreateHttpContext(httpMethod);
            TableDefinition tableDef = new();
            tableDef.SourceEntityRelationshipMap.Add(AuthorizationHelpers.TEST_ENTITY, new());
            DatabaseObject stubDbObj = new()
            {
                TableDefinition = tableDef
            };

            bool actualAuthorizationResult = await IsAuthorizationSuccessful(
                requirement: new EntityRoleActionPermissionsRequirement(),
                resource: stubDbObj,
                resolver: authorizationResolver.Object,
                httpContext: httpContext);

            Assert.AreEqual(expectedAuthorizationResult, actualAuthorizationResult);
        }

        /// <summary>
        /// Validates that authorizing the EntityRoleActionPermissionsRequirement,
        /// any resource that does not cast to DatabaseObject results in an exception.
        /// </summary>
        [TestMethod]
        public async Task EntityRoleActionResourceTest()
        {
            Mock<IAuthorizationResolver> authorizationResolver = new();
            HttpContext httpContext = CreateHttpContext();

            bool actualAuthorizationResult = await IsAuthorizationSuccessful(
                requirement: new EntityRoleActionPermissionsRequirement(),
                resource: null,
                resolver: authorizationResolver.Object,
                httpContext: httpContext
            );

            Assert.AreEqual(false, actualAuthorizationResult);

            bool actualExceptionThrown = false;
            try
            {
                actualAuthorizationResult = await IsAuthorizationSuccessful(
                    requirement: new EntityRoleActionPermissionsRequirement(),
                    resource: new object(),
                    resolver: authorizationResolver.Object,
                    httpContext: httpContext
                );
            }
            catch (DataGatewayException)
            {
                actualExceptionThrown = true;
            }

            Assert.AreEqual(true, actualExceptionThrown);
        }

        /// <summary>
        /// Tests column level authorization permissions for Find requests.
        /// </summary>
        /// <returns></returns>
        # pragma warning disable format
        [DataTestMethod]
        [DataRow(new string[] { "col1", "col2", "col3", "col4" }, false, DisplayName = "Find - Request all of Allowed Columns")]
        [DataRow(new string[] { "col1", "col2", "col3"         }, false, DisplayName = "Find - Request 3/4 subset of Allowed Columns")]
        [DataRow(new string[] { "col1", "col2"                 }, false, DisplayName = "Find - Request 2/4 subset of Allowed Columns")]
        [DataRow(new string[] { "col1"                         }, false, DisplayName = "Find - Request 1/4 subset of Allowed Columns")]
        [DataRow(new string[] {                                }, false, DisplayName = "Find - No column filter for results")]
        [DataRow(new string[] { "col1", "col2", "col3", "col4" }, true, DisplayName = "FindMany - Request all of Allowed Columns")]
        [DataRow(new string[] { "col1", "col2", "col3"         }, true, DisplayName = "FindMany - Request 3/4 subset of Allowed Columns")]
        [DataRow(new string[] { "col1", "col2"                 }, true, DisplayName = "FindMany - Request 2/4 subset of Allowed Columns")]
        [DataRow(new string[] { "col1"                         }, true, DisplayName = "FindMany - Request 1/4 subset of Allowed Columns")]
        [DataRow(new string[] {                                }, true, DisplayName = "FindMany - No column filter for results")]
        # pragma warning restore format
        [TestMethod]
        public async Task FindColumnPermissionsTests(string[] columnsRequestedInput, bool isFindManyRequest)
        {
            IEnumerable<string> columnsRequested = new List<string>(
                columnsRequestedInput);
            IEnumerable<string> allowedColumns = new List<string>(
               new string[] { "col1", "col2", "col3", "col4" });
            bool areColumnsAllowed = true;
            bool expectedAuthorizationResult = true;
            string httpMethod = HttpConstants.GET;

            Mock<IAuthorizationResolver> authorizationResolver = new();
            authorizationResolver.Setup(x => x.AreColumnsAllowedForAction(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.READ,
                It.IsAny<IEnumerable<string>>() //Can be any IEnumerable<string>, as find request result field list is depedent on AllowedColumns.
                )).Returns(areColumnsAllowed);
            authorizationResolver.Setup(x => x.GetAllowedColumns(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                ActionType.READ
                )).Returns(allowedColumns);

            HttpContext httpContext = CreateHttpContext(httpMethod);
            TableDefinition tableDef = new();
            tableDef.SourceEntityRelationshipMap.Add(AuthorizationHelpers.TEST_ENTITY, new());
            DatabaseObject stubDbObj = new()
            {
                TableDefinition = tableDef
            };

            RestRequestContext stubRestContext = new FindRequestContext(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                dbo: stubDbObj,
                isList: false
                );
            stubRestContext.CumulativeColumns.UnionWith(columnsRequested);

            bool actualAuthorizationResult = await IsAuthorizationSuccessful(
               requirement: new ColumnsPermissionsRequirement(),
               resource: stubRestContext,
               resolver: authorizationResolver.Object,
               httpContext: httpContext);

            Assert.AreEqual(expectedAuthorizationResult, actualAuthorizationResult, message: "Unexpected Authorization Result.");
            CollectionAssert.AreEquivalent((ICollection)allowedColumns, stubRestContext.FieldsToBeReturned, message: "FieldsToBeReturned not subset of allowed columns.");
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
        private static HttpContext CreateHttpContext(
            string httpMethod = HttpConstants.GET,
            string clientRole = AuthorizationHelpers.TEST_ROLE)
        {
            Mock<HttpContext> httpContext = new();
            httpContext.Setup(x => x.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER])
                .Returns(clientRole);
            httpContext.Setup(x => x.Request.Method).Returns(httpMethod);
            return httpContext.Object;
        }
        #endregion
    }
}
