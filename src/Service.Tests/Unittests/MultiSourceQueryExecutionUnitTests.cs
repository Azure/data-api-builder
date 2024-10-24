// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Directives;
using Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes;
using Azure.DataApiBuilder.Service.Services;
using Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Helpers;
using Azure.Identity;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Execution.Processing;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Unittests
{
    [TestClass]
    public class MultiSourceQueryExecutionUnitTests
    {
        string _query = @"{
                clients_by_pk(Index: 3) {
                    FirstName
                }
                customers_by_pk(Index: 3) {
                    Name
                }
            }";

        private const string DATA_SOURCE_NAME_1 = "db1";
        private const string DATA_SOURCE_NAME_2 = "db2";
        private const string ENTITY_NAME_1 = "Clients";
        private const string ENTITY_NAME_2 = "Customers";
        private const string QUERY_NAME_1 = "clients_by_pk";
        private const string QUERY_NAME_2 = "customers_by_pk";

        /// <summary>
        /// Validates successful execution of a query against multiple sources.
        /// 1. Tries to use a built schema to execute a query against multiple sources.
        /// 2. Mocks a config and maps two entities to two different data sources.
        /// 3. Mocks a query engine for each data source and returns a result for each request.
        /// 4. Validates that when a query is triggered, the correct query engine is used to execute the query.
        /// 5. Verifies that the result of two seperate queries to two seperate db's is accurately stored on graphql response data.
        /// </summary>
        [TestMethod]
        public async Task TestMultiSourceQuery()
        {
            RuntimeConfig mockConfig1 = GenerateMockRuntimeConfigForMultiDbScenario();

            // Creating mock query engine to return a result for request to respective entities
            // Attempting to validate that in multi-db scenario request is routed to use the correct query engine.
            string jsonResult = "{\"FirstName\": \"db1\"}";

            JsonDocument document1 = JsonDocument.Parse(jsonResult);
            Tuple<JsonDocument, IMetadata> mockReturn1 = new(document1, PaginationMetadata.MakeEmptyPaginationMetadata());

            string jsonResult2 = "{\"Name\":\"db2\"}";
            JsonDocument document2 = JsonDocument.Parse(jsonResult2);
            Tuple<JsonDocument, IMetadata> mockReturn2 = new(document2, PaginationMetadata.MakeEmptyPaginationMetadata());

            Mock<IQueryEngine> sqlQueryEngine = new();
            sqlQueryEngine.Setup(x => x.ExecuteAsync(It.IsAny<IMiddlewareContext>(), It.IsAny<IDictionary<string, object>>(), DATA_SOURCE_NAME_1)).Returns(Task.FromResult(mockReturn1));

            Mock<IQueryEngine> cosmosQueryEngine = new();
            cosmosQueryEngine.Setup(x => x.ExecuteAsync(It.IsAny<IMiddlewareContext>(), It.IsAny<IDictionary<string, object>>(), DATA_SOURCE_NAME_2)).Returns(Task.FromResult(mockReturn2));

            Mock<IQueryEngineFactory> queryEngineFactory = new();
            queryEngineFactory.Setup(x => x.GetQueryEngine(DatabaseType.MySQL)).Returns(sqlQueryEngine.Object);
            queryEngineFactory.Setup(x => x.GetQueryEngine(DatabaseType.CosmosDB_NoSQL)).Returns(cosmosQueryEngine.Object);

            Mock<IMutationEngineFactory> mutationEngineFactory = new();

            Mock<RuntimeConfigLoader> mockLoader = new(null, null);
            mockLoader.Setup(x => x.TryLoadKnownConfig(out mockConfig1, It.IsAny<bool>())).Returns(true);

            RuntimeConfigProvider provider = new(mockLoader.Object);

            // Using a sample schema file to test multi-source query.
            // Schema file contains some sample entities that we can test against.
            string graphQLSchema = File.ReadAllText("MultiSourceTestSchema.gql");
            ISchemaBuilder schemaBuilder = SchemaBuilder.New().AddDocumentFromString(graphQLSchema)
                .AddAuthorizeDirectiveType()
                .AddDirectiveType<ModelDirectiveType>() // Add custom directives used by DAB.
                .AddDirectiveType<RelationshipDirectiveType>()
                .AddDirectiveType<PrimaryKeyDirectiveType>()
                .AddDirectiveType<DefaultValueDirectiveType>()
                .AddDirectiveType<AutoGeneratedDirectiveType>()
                .AddType<OrderByType>()
                .AddType<DefaultValueType>()
                .TryAddTypeInterceptor(new ResolverTypeInterceptor(new ExecutionHelper(queryEngineFactory.Object, mutationEngineFactory.Object, provider)));
            ISchema schema = schemaBuilder.Create();
            IExecutionResult result = await schema.MakeExecutable().ExecuteAsync(_query);

            // client is mapped as belonging to the sql data source.
            // customer is mapped as belonging to the cosmos data source.
            Assert.AreEqual(1, sqlQueryEngine.Invocations.Count, "Sql query engine should be invoked for multi-source query as an entity belongs to sql db.");
            Assert.AreEqual(1, cosmosQueryEngine.Invocations.Count, "Cosmos query engine should be invoked for multi-source query as an entity belongs to cosmos db.");

            Assert.IsNull(result.Errors, "There should be no errors in processing of multisource query.");
            QueryResult queryResult = (QueryResult)result;
            Assert.IsNotNull(queryResult.Data, "Data should be returned for multisource query.");
            IReadOnlyDictionary<string, object> data = queryResult.Data;
            Assert.IsTrue(data.TryGetValue(QUERY_NAME_1, out object queryNode1), $"Query node for {QUERY_NAME_1} should have data populated.");
            Assert.IsTrue(data.TryGetValue(QUERY_NAME_2, out object queryNode2), $"Query node for {QUERY_NAME_2} should have data populated.");

            ResultMap queryMap1 = (ResultMap)queryNode1;
            ResultMap queryMap2 = (ResultMap)queryNode2;

            // validate that the data returend for the queries we did matches the moq data we set up for the respective query engines.
            Assert.AreEqual("db1", queryMap1[0].Value, $"Data returned for {QUERY_NAME_1} is incorrect for multi-source query");
            Assert.AreEqual("db2", queryMap2[0].Value, $"Data returned for {QUERY_NAME_2} is incorrect for multi-source query");
        }

        /// <summary>
        /// Validates successful execution of a query against multiple sources for rest scenario.
        /// 1. Mocks a config and maps two entities to two different data sources.
        /// 2. Mocks a query engine for each data source and returns a result for each request.
        /// 3. Validates that when a query is triggered, the correct query engine is used to execute the query.
        /// 4. Validates that the executeasync method of the correct query engine is invoked for the request.
        /// </summary>
        [TestMethod]
        public async Task TestMultiSourceQueryRest()
        {
            RuntimeConfig mockConfig1 = GenerateMockRuntimeConfigForMultiDbScenario();

            // Creating mock query engine to return a result for request to respective entities
            // Attempting to validate that in multi-db scenario request is routed to use the correct query engine.
            string jsonResult = "[{\"FirstName\": \"db1\"}]";
            string jsonResult2 = "[{\"Name\":\"db2\"}]";

            JsonDocument document1 = JsonDocument.Parse(jsonResult);
            JsonDocument document2 = JsonDocument.Parse(jsonResult2);

            Mock<IQueryEngine> sqlQueryEngine = new();
            Mock<IQueryEngine> cosmosQueryEngine = new();
            Mock<IQueryEngineFactory> queryEngineFactory = new();
            queryEngineFactory.Setup(x => x.GetQueryEngine(DatabaseType.MySQL)).Returns(sqlQueryEngine.Object);
            queryEngineFactory.Setup(x => x.GetQueryEngine(DatabaseType.CosmosDB_NoSQL)).Returns(cosmosQueryEngine.Object);

            Mock<IMutationEngineFactory> mutationEngineFactory = new();

            Mock<RuntimeConfigLoader> mockLoader = new(null, null);
            mockLoader.Setup(x => x.TryLoadKnownConfig(out mockConfig1, It.IsAny<bool>())).Returns(true);

            RuntimeConfigProvider provider = new(mockLoader.Object);

            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            Mock<ISqlMetadataProvider> sqlMetadataProviderDb1 = new();
            Mock<ISqlMetadataProvider> sqlMetadataProviderDb2 = new();
            Mock<ILogger<AuthorizationResolver>> authLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            Mock<IAuthorizationService> authorizationService = new();
            Mock<DatabaseObject> databaseObject1 = new();
            Mock<DatabaseObject> databaseObject2 = new();
            Dictionary<string, DatabaseObject> databaseObjects1 = new()
            {
                { ENTITY_NAME_1, databaseObject1.Object }
            };
            Dictionary<string, DatabaseObject> databaseObjects2 = new()
            {
                { ENTITY_NAME_2, databaseObject2.Object }
            };
            DefaultHttpContext context = new();
            httpContextAccessor.Setup(_ => _.HttpContext).Returns(context);
            AuthorizationResolver authorizationResolver = new(provider, metadataProviderFactory.Object);
            Dictionary<string, string> _pathToEntityMock = new() { { ENTITY_NAME_1, ENTITY_NAME_1 }, { ENTITY_NAME_2, ENTITY_NAME_2 } };

            FindRequestContext findRequestContext1 = new(ENTITY_NAME_1, databaseObject1.Object, true);
            FindRequestContext findRequestContext2 = new(ENTITY_NAME_2, databaseObject2.Object, true);
            sqlQueryEngine.Setup(x => x.ExecuteAsync(It.Is<FindRequestContext>(ctx => ctx.EntityName == ENTITY_NAME_1))).Returns(Task.FromResult(document1));
            cosmosQueryEngine.Setup(x => x.ExecuteAsync(It.Is<FindRequestContext>(ctx => ctx.EntityName == ENTITY_NAME_2))).Returns(Task.FromResult(document2));
            sqlMetadataProviderDb1.Setup(x => x.EntityToDatabaseObject).Returns(databaseObjects1);
            sqlMetadataProviderDb2.Setup(x => x.EntityToDatabaseObject).Returns(databaseObjects2);
            sqlMetadataProviderDb1.Setup(x => x.GetLinkingEntities()).Returns(new Dictionary<string, Entity>());
            sqlMetadataProviderDb2.Setup(x => x.GetLinkingEntities()).Returns(new Dictionary<string, Entity>());
            sqlMetadataProviderDb1.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.MySQL);
            sqlMetadataProviderDb2.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.CosmosDB_NoSQL);
            metadataProviderFactory.Setup(x => x.GetMetadataProvider(DATA_SOURCE_NAME_1)).Returns(sqlMetadataProviderDb1.Object);
            metadataProviderFactory.Setup(x => x.GetMetadataProvider(DATA_SOURCE_NAME_2)).Returns(sqlMetadataProviderDb2.Object);
            authorizationService.Setup(x => x.AuthorizeAsync(It.IsAny<ClaimsPrincipal>(), It.IsAny<object>(), It.IsAny<IEnumerable<IAuthorizationRequirement>>())).Returns(Task.FromResult(AuthorizationResult.Success()));
            RequestValidator requestValidator = new(metadataProviderFactory.Object, provider);

            // Setup REST Service
            RestService restService = new(
                queryEngineFactory.Object,
                mutationEngineFactory.Object,
                metadataProviderFactory.Object,
                httpContextAccessor.Object,
                authorizationService.Object,
                provider,
                requestValidator);

            // client is mapped as belonging to the sql data source.
            // customer is mapped as belonging to the cosmos data source.
            await restService.ExecuteAsync(ENTITY_NAME_1, EntityActionOperation.Read, null);

            Assert.AreEqual(1, sqlQueryEngine.Invocations.Count, "Sql query engine should be invoked for multi-source query as entity belongs to sql db.");
            Assert.AreEqual(0, cosmosQueryEngine.Invocations.Count, "Cosmos query engine should not be invoked for multi-source query as entity belongs to sql db.");
            sqlQueryEngine.Verify(x => x.ExecuteAsync(It.Is<FindRequestContext>(ctx => ctx.EntityName == ENTITY_NAME_1)), Times.Once);

            IActionResult result = await restService.ExecuteAsync(ENTITY_NAME_2, EntityActionOperation.Read, null);
            Assert.AreEqual(1, cosmosQueryEngine.Invocations.Count, "Cosmos query engine should be invoked for multi-source query as entity belongs to cosmos db.");
            Assert.AreEqual(1, sqlQueryEngine.Invocations.Count, "Sql query engine should not be invoked again for multi-source query as entity2 belongs to cosmos db.");
            cosmosQueryEngine.Verify(x => x.ExecuteAsync(It.Is<FindRequestContext>(ctx => ctx.EntityName == ENTITY_NAME_2)), Times.Once);
        }

        /// <summary>
        /// Test to ensure that the correct access token is being set when multiple data sources are used.
        /// </summary>
        [TestMethod]
        public async Task TestMultiSourceTokenSet()
        {
            string defaultSourceConnectionString = "Server =<>;Database=<>;";
            string childConnectionString = "Server =child;Database=child;";

            string DATA_SOURCE_NAME_1 = "db1";
            string db1AccessToken = "AccessToken1";
            string DATA_SOURCE_NAME_2 = "db2";
            string db2AccessToken = "AccessToken2";

            Dictionary<string, DataSource> dataSourceNameToDataSource = new()
            {
                { DATA_SOURCE_NAME_1, new(DatabaseType.MSSQL, defaultSourceConnectionString, new())},
                { DATA_SOURCE_NAME_2, new(DatabaseType.MSSQL, childConnectionString, new()) }
            };

            RuntimeConfig mockConfig = new(
               Schema: "",
               DataSource: new(DatabaseType.MSSQL, defaultSourceConnectionString, Options: new()),
               Runtime: new(
                   Rest: new(),
                   GraphQL: new(),
                   Host: new(Cors: null, Authentication: null)
               ),
               DefaultDataSourceName: DATA_SOURCE_NAME_1,
               DataSourceNameToDataSource: dataSourceNameToDataSource,
               EntityNameToDataSourceName: new(),
               Entities: new(new Dictionary<string, Entity>())
               );

            Mock<RuntimeConfigLoader> mockLoader = new(null, null);
            mockLoader.Setup(x => x.TryLoadKnownConfig(out mockConfig, It.IsAny<bool>())).Returns(true);
            mockLoader.Object.RuntimeConfig = mockConfig;

            RuntimeConfigProvider provider = new(mockLoader.Object);
            provider.TryGetConfig(out RuntimeConfig _);
            provider.TrySetAccesstoken(db1AccessToken, DATA_SOURCE_NAME_1);
            provider.TrySetAccesstoken(db2AccessToken, DATA_SOURCE_NAME_2);

            Mock<DbExceptionParser> dbExceptionParser = new(provider);
            Mock<ILogger<MsSqlQueryExecutor>> queryExecutorLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            MsSqlQueryExecutor msSqlQueryExecutor = new(provider, dbExceptionParser.Object, queryExecutorLogger.Object, httpContextAccessor.Object);

            using SqlConnection conn = new(defaultSourceConnectionString);
            await msSqlQueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, DATA_SOURCE_NAME_1);
            Assert.AreEqual(expected: db1AccessToken, actual: conn.AccessToken, "Data source connection failed to be set with correct access token");

            using SqlConnection conn2 = new(childConnectionString);
            await msSqlQueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn2, DATA_SOURCE_NAME_2);
            Assert.AreEqual(expected: db2AccessToken, actual: conn2.AccessToken, "Data source connection failed to be set with correct access token");

            await msSqlQueryExecutor.SetManagedIdentityAccessTokenIfAnyAsync(conn, string.Empty);
            Assert.AreEqual(expected: db1AccessToken, actual: conn.AccessToken, "Data source connection failed to be set with default access token when source name provided is empty.");
        }

        private static RuntimeConfig GenerateMockRuntimeConfigForMultiDbScenario()
        {
            // Set up mock config where we have two entities both mapping to different data sources.
            Dictionary<string, Entity> entities = new()
            {
                { ENTITY_NAME_1, GraphQLTestHelpers.GenerateEmptyEntity() },
                { ENTITY_NAME_2, GraphQLTestHelpers.GenerateEmptyEntity() }
            };

            Dictionary<string, DataSource> dataSourceNameToDataSource = new()
            {
                { DATA_SOURCE_NAME_1, new(DatabaseType.MySQL, "Server =<>;Database=<>;User=xyz;Password=xxx", new())},
                { DATA_SOURCE_NAME_2, new(DatabaseType.CosmosDB_NoSQL, "Server =<>;Database=<>;User=xyz;Password=xxx", new()) }
            };

            Dictionary<string, string> entityNameToDataSourceName = new()
            {
                { ENTITY_NAME_1, DATA_SOURCE_NAME_1 },
                { ENTITY_NAME_2, DATA_SOURCE_NAME_2 }
            };

            RuntimeConfig mockConfig1 = new(
               Schema: "",
               DataSource: new(DatabaseType.MySQL, "Server =<>;Database=<>;User=xyz;Password=xxx", new()),
               Runtime: new(
                   Rest: new(),
                   GraphQL: new(),
                   // use prod mode to avoid having to mock config file watcher
                   Host: new(Cors: null, Authentication: null, HostMode.Production)
               ),
               DefaultDataSourceName: DATA_SOURCE_NAME_1,
               DataSourceNameToDataSource: dataSourceNameToDataSource,
               EntityNameToDataSourceName: entityNameToDataSourceName,
               Entities: new(entities)
               );

            return mockConfig1;
        }

        /// <summary>
        /// Needed for the callback that is required
        /// to make use of out parameter with mocking.
        /// Without use of delegate the out param will
        /// not be populated with the correct value.
        /// This delegate is for the callback used
        /// with the mocked MetadataProvider.
        /// </summary>
        /// <param name="entityPath">The entity path.</param>
        /// <param name="entity">Name of entity.</param>
        delegate void metaDataCallback(string entityPath, out string entity);
    }
}
