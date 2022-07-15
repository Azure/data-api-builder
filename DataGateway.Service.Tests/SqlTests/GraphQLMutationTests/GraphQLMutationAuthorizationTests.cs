#nullable enable
using System.Collections.Generic;
using Azure.DataGateway.Auth;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests.SqlTests.GraphQLMutationTests
{
    /// <summary>
    /// Base class for GraphQL Mutation tests targetting Sql databases.
    /// </summary>
    [TestClass]
    public class GraphQLMutationAuthorizationTests : SqlTestBase
    {
        private const string TEST_ENTITY = "TEST_ENTITY";
        private const string TEST_COLUMN_VALUE = "COLUMN_VALUE";
        private const string MIDDLEWARE_CONTEXT_ROLEHEADER_KEY = "role";
        private const string MIDDLEWARE_CONTEXT_ROLEHEADER_VALUE = "roleName";

        [DataTestMethod]
        [DataRow(Config.Operation.Create, true, new string[] { "col1", "col2", "col3" }, new string[] { "col1" }, DisplayName = "Anonymous Mutation Fields")]
        [DataRow(false, new string[] { "col1", "col2", "col3" }, new string[] { "col4" }, DisplayName = "Anonymous Mutation Fields")]

        public void MutationFields_AuthorizationEvaluation(bool isAuthorized, string[] columnsAllowed, string[] columnsRequested)
        {
            SqlMutationEngine engine = SetupTestFixture(isAuthorized);

            // Setup mock mutation input, utilized in BaseSqlQueryStructure.InputArgumentToMutationParams() helper.
            List<ObjectFieldNode> mutationInputRaw = new();
            foreach (string column in columnsRequested)
            {
                mutationInputRaw.Add(new ObjectFieldNode(name: column, value: TEST_COLUMN_VALUE));
            }

            Dictionary<string, object?> parameters = new()
            {
                { SqlMutationEngine.INPUT_ARGUMENT_NAME, mutationInputRaw }
            };

            Dictionary<string, object?> middlewareContextData = new()
            {
                { MIDDLEWARE_CONTEXT_ROLEHEADER_KEY, new StringValues(MIDDLEWARE_CONTEXT_ROLEHEADER_VALUE) }
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
                    mutationOperation: Config.Operation.Create
                );

                authorizationResult = true;
            }
            catch (DataGatewayException)
            {
                Assert.IsFalse(isAuthorized, message: "Mutation fields authorized erroneously.");
            }

            Assert.AreEqual(actual: authorizationResult, expected: isAuthorized, message: "Mutation field authorization incorrectly evaluated.");
        }

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        private SqlMutationEngine SetupTestFixture(bool areColumnsAllowed)
        {
            Mock<IQueryEngine> _queryEngine = new();
            Mock<ISqlMetadataProvider> _sqlMetadataProvider = new();
            Mock<IQueryExecutor> _queryExecutor = new();
            Mock<IQueryBuilder> _queryBuilder = new();

            // Creates Mock AuthorizationResolver to return a preset result based on [TestMethod] input.
            Mock<IAuthorizationResolver> _authorizationResolver = new();
            _authorizationResolver.Setup(x => x.AreColumnsAllowedForAction(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<IEnumerable<string>>() // Can be any IEnumerable<string>, as find request result field list is depedent on AllowedColumns.
                )).Returns(areColumnsAllowed);

            return new SqlMutationEngine(
                _queryEngine.Object,
                _queryExecutor.Object,
                _queryBuilder.Object,
                _sqlMetadataProvider.Object,
                _authorizationResolver.Object
                );
        }
    }
}
