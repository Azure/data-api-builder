// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Authorization.REST
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
        [TestMethod]
        [DataRow(true, true, DisplayName = "Valid Role Context Succeeds Authorization")]
        [DataRow(false, false, DisplayName = "Invalid Role Context Fails Authorization")]
        public async Task RoleContextPermissionsRequirementTest(bool expectedAuthorizationResult, bool isValidRoleContext)
        {
            Mock<IAuthorizationResolver> authorizationResolver = new();
            authorizationResolver.Setup(x => x.IsValidRoleContext(It.IsAny<HttpContext>())).Returns(isValidRoleContext);

            HttpContext httpContext = CreateHttpContext();

            bool actualAuthorizationResult = await IsAuthorizationSuccessfulAsync(
                requirement: new RoleContextPermissionsRequirement(),
                resource: httpContext,
                resolver: authorizationResolver.Object,
                httpContext: httpContext);

            Assert.AreEqual(expectedAuthorizationResult, actualAuthorizationResult);
        }

        /// <summary>
        /// Tests that a user role with operations specified as ["*"] will be authorized for all http methods
        /// </summary>
        /// <param name="httpMethod"> the http method that we are checking if the client is authorized to use </param>
        [TestMethod]
        [DataRow(HttpConstants.GET)]
        [DataRow(HttpConstants.POST)]
        [DataRow(HttpConstants.PUT)]
        [DataRow(HttpConstants.PATCH)]
        [DataRow(HttpConstants.DELETE)]
        public async Task TestWildcardResolvesAsAllOperations(string httpMethod)
        {
            AuthorizationResolver authorizationResolver = SetupAuthResolverWithWildcardOperation();
            HttpContext httpContext = CreateHttpContext(httpMethod: httpMethod, clientRole: "admin");

            bool authorizationResult = await IsAuthorizationSuccessfulAsync(
                requirement: new EntityRoleOperationPermissionsRequirement(),
                resource: AuthorizationHelpers.TEST_ENTITY,
                resolver: authorizationResolver,
                httpContext: httpContext);

            Assert.IsTrue(authorizationResult);
        }

        /// <summary>
        /// Ensure that passing a wildcard operation does not break policy parsing
        /// (ensure we bypass dictionary lookup OperationToColumnMap[operation] for the passed in CRUD operation)
        /// Expect an empty string to be returned as the policy associated with a wildcard
        /// </summary>
        [TestMethod]
        [DataRow(HttpConstants.GET)]
        [DataRow(HttpConstants.POST)]
        [DataRow(HttpConstants.PUT)]
        [DataRow(HttpConstants.PATCH)]
        [DataRow(HttpConstants.DELETE)]
        public void TestWildcardPolicyResolvesToEmpty(string httpMethod)
        {
            AuthorizationResolver authorizationResolver = SetupAuthResolverWithWildcardOperation();
            HttpContext httpContext = CreateHttpContext(httpMethod: httpMethod, clientRole: "admin");

            Assert.AreEqual(expected: string.Empty, actual: authorizationResolver.ProcessDBPolicy(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: "admin",
                operation: RestService.HttpVerbToOperations(httpVerbName: httpMethod),
                httpContext: httpContext)
            );
        }

        /// <summary>
        /// Calls the AuthorizationResolver to evaluate whether a role and operation are allowed.
        ///     (1) HttpMethod resolves to one or two CRUD operations, requirement fails when >0 operations fails the AuthorizationResolver call.
        ///         i.e. PUT resolves to Create and Update
        ///         i.e. GET resolves to Read
        /// </summary>
        /// <param name="httpMethod">Operation type of request</param>
        /// <param name="expectedAuthorizationResult">Whether authorization is expected to succeed.</param>
        /// <param name="isValidCreateRoleOperation">Whether Role/Operation pair is allowed for Read authorization config.</param>
        /// <param name="isValidReadRoleOperation">Whether Role/Operation pair is allowed per authorization config.</param>
        /// <param name="isValidUpdateRoleOperation">Whether Role/Operation pair is allowed per authorization config.</param>
        /// <param name="isValidDeleteRoleOperation">Whether Role/Operation pair is allowed per authorization config.</param>
        /// <returns></returns>
        [TestMethod]
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
        public async Task EntityRoleOperationPermissionsRequirementTest(
            string httpMethod,
            bool expectedAuthorizationResult,
            bool isValidCreateRoleOperation,
            bool isValidReadRoleOperation,
            bool isValidUpdateRoleOperation,
            bool isValidDeleteRoleOperation)
        {
            Mock<IAuthorizationResolver> authorizationResolver = new();
            authorizationResolver.Setup(x => x.AreRoleAndOperationDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                EntityActionOperation.Create
                )).Returns(isValidCreateRoleOperation);
            authorizationResolver.Setup(x => x.AreRoleAndOperationDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                EntityActionOperation.Read
                )).Returns(isValidReadRoleOperation);
            authorizationResolver.Setup(x => x.AreRoleAndOperationDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                EntityActionOperation.Update
                )).Returns(isValidUpdateRoleOperation);
            authorizationResolver.Setup(x => x.AreRoleAndOperationDefinedForEntity(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                EntityActionOperation.Delete
                )).Returns(isValidDeleteRoleOperation);

            HttpContext httpContext = CreateHttpContext(httpMethod);

            bool actualAuthorizationResult = await IsAuthorizationSuccessfulAsync(
                requirement: new EntityRoleOperationPermissionsRequirement(),
                resource: AuthorizationHelpers.TEST_ENTITY,
                resolver: authorizationResolver.Object,
                httpContext: httpContext);

            Assert.AreEqual(expectedAuthorizationResult, actualAuthorizationResult);
        }

        /// <summary>
        /// Validates that authorizing the EntityRoleOperationPermissionsRequirement,
        /// any resource that does not cast to DatabaseObject results in an exception.
        /// </summary>
        [TestMethod]
        public async Task EntityRoleOperationResourceTest()
        {
            Mock<IAuthorizationResolver> authorizationResolver = new();
            HttpContext httpContext = CreateHttpContext();

            bool actualAuthorizationResult = await IsAuthorizationSuccessfulAsync(
                requirement: new EntityRoleOperationPermissionsRequirement(),
                resource: null,
                resolver: authorizationResolver.Object,
                httpContext: httpContext
            );

            Assert.IsFalse(actualAuthorizationResult);

            bool actualExceptionThrown = false;
            try
            {
                actualAuthorizationResult = await IsAuthorizationSuccessfulAsync(
                    requirement: new EntityRoleOperationPermissionsRequirement(),
                    resource: new object(),
                    resolver: authorizationResolver.Object,
                    httpContext: httpContext
                );
            }
            catch (DataApiBuilderException)
            {
                actualExceptionThrown = true;
            }

            Assert.IsTrue(actualExceptionThrown);
        }

        /// <summary>
        /// Tests column level authorization permissions for Find requests with no $f filter query string parameter.
        /// - Request contains any subset of columns requested, which are in the allowed list of columns -> Authorization successful
        /// - Request contains any column requested, which does not appear in the allowed list of columns -> Authorization failure
        /// - After authorization, RestAuthorizationHandler modifies FieldsToBeReturned with list of allowed columns, so that results
        ///   only contain fields allowed by permissions.
        /// FORMAT WARNING Disabled: To make test input easier to read,
        /// whitespace checking is ignored for the [DataRow] definitions.
        /// </summary>
        /// <param name="columnsRequestedInput">List of columns that appear in a request {URL, QueryString, Body}</param>
        # pragma warning disable format
        [TestMethod]
        // Positive Tests where authorization succeeds for Find requests with no $f filter query string parameter
        [DataRow(new string[] { "col1", "col2", "col3", "col4" }, DisplayName = "Find - Request all of Allowed Columns")]
        [DataRow(new string[] { "col1", "col2", "col3"         }, DisplayName = "Find - Request 3/4 subset of Allowed Columns")]
        [DataRow(new string[] { "col1", "col2"                 }, DisplayName = "Find - Request 2/4 subset of Allowed Columns")]
        [DataRow(new string[] { "col1"                         }, DisplayName = "Find - Request 1/4 subset of Allowed Columns")]
        [DataRow(new string[] {                                }, DisplayName = "Find - No column filter for results")]
        // Negative tests where authorization fails for Find requests with no $f filter query string parameter
        [DataRow(new string[] { "col1", "col2", "col3", "col4", "col5" }, DisplayName = "Find - Request all allowed + 1 disallowed column(s)")]
        [DataRow(new string[] { "col1", "col5", "col6", "col7", "col9" }, DisplayName = "Find - Request 1 allowed + > 1 disallowed column(s)")]
        [DataRow(new string[] { }, false, false, DisplayName = "Find - Request on entity with no included columns. The request url contains no key,select,orderby,filter.")]
        #pragma warning restore format
        public async Task FindColumnPermissionsTests(string[] columnsRequestedInput,
            bool areAllowedExposedColumns = true,
            bool expectedAuthorizationResult = true)
        {
            IEnumerable<string> columnsRequested = new List<string>(
                columnsRequestedInput);
            IEnumerable<string> allowedColumns = new List<string>();

            if (areAllowedExposedColumns)
            {
                allowedColumns = new List<string>(new string[] { "col1", "col2", "col3", "col4" });
            }

            bool areColumnsAllowed = true;

            // Creates Mock AuthorizationResolver to return a preset result based on [TestMethod] input.
            Mock<IAuthorizationResolver> authorizationResolver = new();
            authorizationResolver.Setup(x => x.AreColumnsAllowedForOperation(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                EntityActionOperation.Read,
                It.IsAny<IEnumerable<string>>() // Can be any IEnumerable<string>, as find request result field list is depedent on AllowedColumns.
                )).Returns(areColumnsAllowed);
            authorizationResolver.Setup(x => x.GetAllowedExposedColumns(
                AuthorizationHelpers.TEST_ENTITY,
                AuthorizationHelpers.TEST_ROLE,
                EntityActionOperation.Read
                )).Returns(allowedColumns);

            string httpMethod = HttpConstants.GET;
            HttpContext httpContext = CreateHttpContext(httpMethod);
            RestRequestContext stubRestRequestContext = CreateRestRequestContext(columnsRequested);

            // Perform Authorization Check, the result is used to validate behavior.
            bool actualAuthorizationResult = await IsAuthorizationSuccessfulAsync(
               requirement: new ColumnsPermissionsRequirement(),
               resource: stubRestRequestContext,
               resolver: authorizationResolver.Object,
               httpContext: httpContext);

            Assert.AreEqual(expectedAuthorizationResult, actualAuthorizationResult, message: "Unexpected Authorization Result.");

            // Ensure the FieldsToBeReturned, which the AuthorizationResolver modifies for Find requests,
            // is equivalent to the allowedColumns list. This test *does not* mock requests which predefine a query string field filter ($f)
            CollectionAssert.AreEquivalent(expected: (ICollection)allowedColumns, actual: stubRestRequestContext.FieldsToBeReturned, message: "FieldsToBeReturned not subset of allowed columns.");
        }

        #region Helper Methods
        /// <summary>
        /// Setup request and authorization context and get Authorization result
        /// </summary>
        /// <param name="entityName">Table/Entity that is being queried.</param>
        /// <param name="user">ClaimsPrincipal / user that has authentication status defined.</param>
        /// <returns>True/False whether Authorization Result Succeeded</returns>
        private static async Task<bool> IsAuthorizationSuccessfulAsync(
            IAuthorizationRequirement requirement,
            object resource,
            IAuthorizationResolver resolver,
            HttpContext httpContext)
        {
            // Setup Mock and Stub Objects
            ClaimsPrincipal user = new(new ClaimsIdentity(authenticationType: "Bearer"));
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            Mock<ILogger<RestAuthorizationHandler>> logger = new();
            httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

            AuthorizationHandlerContext context = new(new List<IAuthorizationRequirement> { requirement }, user, resource);
            RestAuthorizationHandler handler = new(resolver, httpContextAccessor.Object, logger.Object);

            await handler.HandleAsync(context);

            return context.HasSucceeded;
        }

        /// <summary>
        /// Create Mock HttpContext object for use in test fixture.
        /// </summary>
        /// <param name="httpMethod">Optionally set the httpVerb of the request.</param>
        /// <param name="clientRole">Optionally set the role membership of the authenticated role.</param>
        /// <returns>Mocked HttpContext object</returns>
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

        /// <summary>
        /// Creates RestRequestContext with test input defined columns, with TableDefinition and DatabaseObject
        /// created for usage within the RestAuthorizationHandler's ColumnsPermissionsRequirement handling.
        /// </summary>
        /// <param name="columnsRequested">Cumulative list of columns present in a request.</param>
        /// <returns>Stubbed RestRequestContext object</returns>
        private static RestRequestContext CreateRestRequestContext(
            IEnumerable<string> columnsRequested
            )
        {
            SourceDefinition tableDef = new();
            tableDef.SourceEntityRelationshipMap.Add(AuthorizationHelpers.TEST_ENTITY, new());
            DatabaseObject stubDbObj = new DatabaseTable()
            {
                TableDefinition = tableDef
            };

            RestRequestContext stubRestContext = new FindRequestContext(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                dbo: stubDbObj,
                isList: false
                );
            stubRestContext.CumulativeColumns.UnionWith(columnsRequested);

            return stubRestContext;
        }

        /// <summary>
        /// Sets up an authorization resolver with a config that specifies the wildcard ("*") as the test entity's operations.
        /// Explicitly use this instead of AuthorizationHelpers.InitRuntimeConfig() because we want to create operations as
        /// array of string instead of array of object.
        /// </summary>
        private static AuthorizationResolver SetupAuthResolverWithWildcardOperation()
        {
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig(
                entityName: AuthorizationHelpers.TEST_ENTITY,
                roleName: "admin",
                operation: EntityActionOperation.All);
            return AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);
        }
        #endregion
    }
}
