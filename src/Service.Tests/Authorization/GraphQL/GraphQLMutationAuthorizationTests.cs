// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable
using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Mutations;
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
        [DataTestMethod]
        [DataRow(true, new string[] { "col1", "col2", "col3" }, new string[] { "col1" }, EntityActionOperation.UpdateGraphQL, DisplayName = "Update Mutation Field Authorization - Success, Columns Allowed")]
        [DataRow(false, new string[] { "col1", "col2", "col3" }, new string[] { "col4" }, EntityActionOperation.UpdateGraphQL, DisplayName = "Update Mutation Field Authorization - Failure, Columns Forbidden")]
        [DataRow(true, new string[] { "col1", "col2", "col3" }, new string[] { "col1" }, EntityActionOperation.Delete, DisplayName = "Delete Mutation Field Authorization - Success, since authorization to perform the" +
            "delete mutation operation occurs prior to column evaluation in the request pipeline.")]
        public void MutationFields_AuthorizationEvaluation(bool isAuthorized, string[] columnsAllowed, string[] columnsRequested, EntityActionOperation operation)
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
                { MutationBuilder.ITEM_INPUT_ARGUMENT_NAME, mutationInputRaw }
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
                engine.AuthorizeMutation(
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
        private static SqlMutationEngine SetupTestFixture(bool isAuthorized)
        {
            Mock<IQueryEngine> _queryEngine = new();
            Mock<IQueryExecutor> _queryExecutor = new();
            Mock<IQueryBuilder> _queryBuilder = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            Mock<ILogger<SqlMutationEngine>> _mutationEngineLogger = new();
            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(string.Empty));
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader);
            DefaultHttpContext context = new();
            Mock<IMetadataProviderFactory> _metadataProviderFactory = new();
            Mock<GQLFilterParser> _gQLFilterParser = new(provider, _metadataProviderFactory.Object);
            Mock<IAbstractQueryManagerFactory> _queryManagerFactory = new();
            Mock<IQueryEngineFactory> _queryEngineFactory = new();

            httpContextAccessor.Setup(_ => _.HttpContext).Returns(context);

            // Creates Mock AuthorizationResolver to return a preset result based on [TestMethod] input.
            Mock<IAuthorizationResolver> _authorizationResolver = new();
            _authorizationResolver.Setup(x => x.AreColumnsAllowedForOperation(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<EntityActionOperation>(),
                It.IsAny<IEnumerable<string>>()
                )).Returns(isAuthorized);

            return new SqlMutationEngine(
                _queryManagerFactory.Object,
                _metadataProviderFactory.Object,
                _queryEngineFactory.Object,
                _authorizationResolver.Object,
                _gQLFilterParser.Object,
                httpContextAccessor.Object,
                provider);
        }
    }
}
