#nullable enable
using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.DataApiBuilder.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL
{
    /// <summary>
    /// Unit Tests validating mutation field authorization for GraphQL.
    /// Ensures the authorization decision from the authorizationResolver properly triggers
    /// an exception for failure (DataApiBuilderException.Forbidden), and proceeds normally for success.
    /// </summary>
    [TestClass]
    public class GraphQLMutationAuthorizationTests
    {
        private const string TEST_ENTITY = "TEST_ENTITY";
        private const string TEST_COLUMN_VALUE = "COLUMN_VALUE";
        private const string MIDDLEWARE_CONTEXT_ROLEHEADER_VALUE = "roleName";

        /// <summary>
        /// This test ensures that data passed into AuthorizeMutationFields() within the SqlMutationEngine
        /// are evaluated and provided to the authorization resolver for an authorization decision.
        /// If authorization fails, an exception is thrown and this test validates that scenario.
        /// If authorization succeeds, no exceptions are thrown for authorization, and function resolves silently.
        /// </summary>
        /// <param name="isAuthorized"></param>
        /// <param name="columnsAllowed"></param>
        /// <param name="columnsRequested"></param>
        /// <param name="operation"></param>
        [DataTestMethod]
        [DataRow(true, new string[] { "col1", "col2", "col3" }, new string[] { "col1" }, Config.Operation.Create, DisplayName = "Create Mutation Field Authorization - Success, Columns Allowed")]
        [DataRow(false, new string[] { "col1", "col2", "col3" }, new string[] { "col1" }, Config.Operation.Create, DisplayName = "Create Mutation Field Authorization - Failure, Columns Forbidden")]
        [DataRow(true, new string[] { "col1", "col2", "col3" }, new string[] { "col1" }, Config.Operation.UpdateGraphQL, DisplayName = "Update Mutation Field Authorization - Success, Columns Allowed")]
        [DataRow(false, new string[] { "col1", "col2", "col3" }, new string[] { "col4" }, Config.Operation.UpdateGraphQL, DisplayName = "Update Mutation Field Authorization - Failure, Columns Forbidden")]
        [DataRow(true, new string[] { "col1", "col2", "col3" }, new string[] { "col1" }, Config.Operation.Delete, DisplayName = "Delete Mutation Field Authorization - Success, since authorization to perform the" +
            "delete mutation operation occurs prior to column evaluation in the request pipeline.")]
        public void MutationFields_AuthorizationEvaluation(bool isAuthorized, string[] columnsAllowed, string[] columnsRequested, Config.Operation operation)
        {
            SqlMutationEngine engine = SetupTestFixture(isAuthorized);

            // Setup mock mutation input, utilized in BaseSqlQueryStructure.InputArgumentToMutationParams() helper.
            // This takes the test's "columnsRequested" and adds them to the mutation input.
            List<ObjectFieldNode> mutationInputRaw = new();
            foreach (string column in columnsRequested)
            {
                mutationInputRaw.Add(new ObjectFieldNode(name: column, value: TEST_COLUMN_VALUE));
            }

            Dictionary<string, object?> parameters = new()
            {
                { MutationBuilder.INPUT_ARGUMENT_NAME, mutationInputRaw }
            };

            Dictionary<string, object?> middlewareContextData = new()
            {
                { AuthorizationResolver.CLIENT_ROLE_HEADER, new StringValues(MIDDLEWARE_CONTEXT_ROLEHEADER_VALUE) }
            };

            Mock<IMiddlewareContext> graphQLMiddlewareContext = new();
            graphQLMiddlewareContext.Setup(x => x.ContextData).Returns(middlewareContextData);

            bool authorizationResult = false;
            try
            {
                engine.AuthorizeMutationFields(
                    graphQLMiddlewareContext.Object,
                    parameters,
                    entityName: TEST_ENTITY,
                    mutationOperation: operation
                );

                authorizationResult = true;
            }
            catch (DataApiBuilderException dgException)
            {
                Console.Error.WriteLine(dgException.Message);
                Assert.IsFalse(isAuthorized, message: "Mutation fields authorized erroneously, no exception expected.");
            }

            Assert.AreEqual(actual: authorizationResult, expected: isAuthorized, message: "Mutation field authorization incorrectly evaluated.");
        }

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        private static SqlMutationEngine SetupTestFixture(bool isAuthorized)
        {
            Mock<IQueryEngine> _queryEngine = new();
            Mock<ISqlMetadataProvider> _sqlMetadataProvider = new();
            Mock<IQueryExecutor> _queryExecutor = new();
            Mock<IQueryBuilder> _queryBuilder = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            Mock<ILogger<SqlMutationEngine>> _mutationEngineLogger = new();
            DefaultHttpContext context = new();
            httpContextAccessor.Setup(_ => _.HttpContext).Returns(context);

            // Creates Mock AuthorizationResolver to return a preset result based on [TestMethod] input.
            Mock<IAuthorizationResolver> _authorizationResolver = new();
            _authorizationResolver.Setup(x => x.AreColumnsAllowedForOperation(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<Operation>(),
                It.IsAny<IEnumerable<string>>()
                )).Returns(isAuthorized);

            return new SqlMutationEngine(
                _queryEngine.Object,
                _queryExecutor.Object,
                _queryBuilder.Object,
                _sqlMetadataProvider.Object,
                _authorizationResolver.Object,
                httpContextAccessor.Object,
                _mutationEngineLogger.Object
                );
        }
    }
}
