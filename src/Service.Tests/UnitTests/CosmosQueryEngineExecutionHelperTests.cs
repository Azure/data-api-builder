// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Language;
using HotChocolate.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class CosmosQueryEngineExecutionHelperTests
    {
        [TestMethod]
        public async Task ExecuteListAsync_ReturnsAllItemsFromCosmosPages()
        {
            JObject first = JObject.Parse(@"{ ""id"": ""1"" }");
            JObject second = JObject.Parse(@"{ ""id"": ""2"" }");
            Mock<FeedResponse<JObject>> page = new();
            page.Setup(x => x.GetEnumerator()).Returns(() => new[] { first, second }.AsEnumerable().GetEnumerator());
            Mock<FeedIterator<JObject>> iterator = new();
            iterator.SetupSequence(x => x.HasMoreResults).Returns(true).Returns(false);
            iterator.Setup(x => x.ReadNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(page.Object);
            Mock<Container> container = CreateQueryContainer(iterator.Object);
            Mock<Database> database = new();
            database.Setup(x => x.GetContainer("books")).Returns(container.Object);
            Mock<CosmosClient> client = new();
            client.Setup(x => x.GetDatabase("db")).Returns(database.Object);
            CosmosClientProvider clientProvider =
                (CosmosClientProvider)RuntimeHelpers.GetUninitializedObject(typeof(CosmosClientProvider));
            typeof(CosmosClientProvider).GetField("<Clients>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(clientProvider, new Dictionary<string, CosmosClient?> { ["cosmos"] = client.Object });

            CosmosSqlMetadataProvider metadata = CreateMetadataProvider();
            Mock<IMetadataProviderFactory> metadataFactory = new();
            metadataFactory.Setup(x => x.GetMetadataProvider("cosmos")).Returns(metadata);
            RuntimeConfig configuration = new(
                Schema: string.Empty,
                DataSource: new DataSource(DatabaseType.CosmosDB_NoSQL, string.Empty),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>()));
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(configuration);
            CosmosQueryEngine engine = new(
                clientProvider,
                metadataFactory.Object,
                Mock.Of<IAuthorizationResolver>(),
                new GQLFilterParser(runtimeConfigProvider, metadataFactory.Object),
                runtimeConfigProvider,
                null!);

            ISchemaBuilder schemaBuilder = SchemaBuilder.New()
                .AddDocumentFromString("type Query { books: [Book!]! } type Book { id: String }")
                .AddResolver("Book", "id", _ => "id")
                .AddResolver("Query", "books", async context =>
                {
                    Tuple<IEnumerable<System.Text.Json.JsonDocument>, IMetadata> result =
                        await engine.ExecuteListAsync(
                            (IMiddlewareContext)context,
                            new Dictionary<string, object> { ["id"] = "1" },
                            "cosmos");
                    return result.Item1.Select(document => new
                    {
                        id = document.RootElement.GetProperty("id").GetString()
                    });
                });

            IOperationRequest request = OperationRequestBuilder.New()
                .SetDocument("{ books { id } }")
                .SetGlobalState(nameof(HttpContext), CreateHttpContext())
                .Build();
            IExecutionResult executionResult = await schemaBuilder.Create().MakeExecutable().ExecuteAsync(request);

            OperationResult operationResult = executionResult.ExpectOperationResult();
            Assert.AreEqual(0, operationResult.Errors.Count, string.Join(" | ", operationResult.Errors.Select(error => error.ToString())));
            container.Verify(x => x.GetItemQueryIterator<JObject>(
                It.IsAny<QueryDefinition>(),
                It.IsAny<string>(),
                It.IsAny<QueryRequestOptions>()), Times.Once);
        }

        [TestMethod]
        public async Task ExecuteQueryAsync_NoCrossPartitionResultsReturnsNull()
        {
            Mock<FeedResponse<JObject>> page = new();
            page.SetupGet(x => x.Count).Returns(0);
            Mock<FeedIterator<JObject>> iterator = new();
            iterator.Setup(x => x.ReadNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(page.Object);
            iterator.SetupGet(x => x.HasMoreResults).Returns(false);
            Mock<Container> container = CreateQueryContainer(iterator.Object);

            JObject? result = await InvokeExecuteQueryAsync(
                CreateStructure(), new QueryRequestOptions(), container.Object, string.Empty, string.Empty);

            Assert.IsNull(result);
        }

        [TestMethod]
        public async Task ExecuteQueryAsync_PaginatedCrossPartitionResultIncludesContinuation()
        {
            JObject first = JObject.Parse(@"{ ""id"": ""1"" }");
            JObject second = JObject.Parse(@"{ ""id"": ""2"" }");
            Mock<FeedResponse<JObject>> page = new();
            page.Setup(x => x.GetEnumerator()).Returns(() => new[] { first, second }.AsEnumerable().GetEnumerator());
            page.SetupGet(x => x.ContinuationToken).Returns("next-token");
            Mock<FeedIterator<JObject>> iterator = new();
            iterator.Setup(x => x.ReadNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(page.Object);
            Mock<Container> container = CreateQueryContainer(iterator.Object);
            CosmosQueryStructure structure = CreateStructure(isPaginated: true);
            structure.MaxItemCount = 2;
            structure.Continuation = "cHJldmlvdXMtdG9rZW4=";
            QueryRequestOptions options = new();

            JObject result = (await InvokeExecuteQueryAsync(
                structure, options, container.Object, string.Empty, string.Empty))!;

            Assert.AreEqual(2, options.MaxItemCount);
            Assert.AreEqual("bmV4dC10b2tlbg==", result[QueryBuilder.PAGINATION_TOKEN_FIELD_NAME]!.Value<string>());
            Assert.IsTrue(result[QueryBuilder.HAS_NEXT_PAGE_FIELD_NAME]!.Value<bool>());
            Assert.AreEqual(2, ((JArray)result[QueryBuilder.PAGINATION_FIELD_NAME]!).Count);
            container.Verify(x => x.GetItemQueryIterator<JObject>(
                It.IsAny<QueryDefinition>(),
                "previous-token",
                options), Times.Once);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task QueryByIdAndPartitionKey_SuccessReturnsExpectedShape(bool isPaginated)
        {
            JObject item = JObject.Parse(@"{ ""id"": ""1"" }");
            Mock<ItemResponse<JObject>> response = new();
            response.SetupGet(x => x.Resource).Returns(item);
            Mock<Container> container = new();
            container.Setup(x => x.ReadItemAsync<JObject>(
                    "1",
                    It.IsAny<PartitionKey>(),
                    It.IsAny<ItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .ReturnsAsync(response.Object);

            JObject result = await InvokeQueryByIdAndPartitionKey(container.Object, isPaginated);

            if (isPaginated)
            {
                Assert.IsFalse(result[QueryBuilder.HAS_NEXT_PAGE_FIELD_NAME]!.Value<bool>());
                Assert.AreEqual("1", result[QueryBuilder.PAGINATION_FIELD_NAME]![0]!["id"]!.Value<string>());
            }
            else
            {
                Assert.AreSame(item, result);
            }
        }

        [TestMethod]
        public async Task QueryByIdAndPartitionKey_NotFoundReturnsNull()
        {
            Mock<Container> container = new();
            container.Setup(x => x.ReadItemAsync<JObject>(
                    It.IsAny<string>(),
                    It.IsAny<PartitionKey>(),
                    It.IsAny<ItemRequestOptions>(),
                    It.IsAny<CancellationToken>()))
                .ThrowsAsync(new CosmosException("missing", HttpStatusCode.NotFound, 0, string.Empty, 0));

            JObject? result = await InvokeQueryByIdAndPartitionKey(container.Object, false);

            Assert.IsNull(result);
        }

        private static Mock<Container> CreateQueryContainer(FeedIterator<JObject> iterator)
        {
            Mock<Container> container = new();
            container.Setup(x => x.GetItemQueryIterator<JObject>(
                    It.IsAny<QueryDefinition>(),
                    It.IsAny<string>(),
                    It.IsAny<QueryRequestOptions>()))
                .Returns(iterator);
            return container;
        }

        private static CosmosSqlMetadataProvider CreateMetadataProvider()
        {
            CosmosSqlMetadataProvider provider =
                (CosmosSqlMetadataProvider)RuntimeHelpers.GetUninitializedObject(typeof(CosmosSqlMetadataProvider));
            Entity entity = new(
                Source: new EntitySource("db.books", EntitySourceType.Table, null, null),
                GraphQL: new EntityGraphQLOptions("Book", "Books"),
                Fields: null,
                Rest: new EntityRestOptions(Enabled: true),
                Permissions: Array.Empty<EntityPermission>(),
                Mappings: null,
                Relationships: null);
            SetPrivateField(provider, "_runtimeConfigEntities", new RuntimeEntities(new Dictionary<string, Entity> { ["Book"] = entity }));
            SetPrivateField(provider, "_cosmosDb", new CosmosDbNoSQLDataSourceOptions("db", "books", null, null));
            SetPrivateField(provider, "_databaseType", DatabaseType.CosmosDB_NoSQL);
            provider.GraphQLSchemaRoot = Utf8GraphQLParser.Parse("type Book { id: String }");
            provider.EntityWithJoins = new Dictionary<string, List<EntityDbPolicyCosmosModel>>();
            return provider;
        }

        private static DefaultHttpContext CreateHttpContext()
        {
            DefaultHttpContext context = new();
            context.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = AuthorizationResolver.ROLE_ANONYMOUS;
            return context;
        }

        private static void SetPrivateField(object instance, string name, object value)
        {
            instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(instance, value);
        }

        private static CosmosQueryStructure CreateStructure(bool isPaginated = false)
        {
            CosmosQueryStructure structure =
                (CosmosQueryStructure)RuntimeHelpers.GetUninitializedObject(typeof(CosmosQueryStructure));
            structure.IsPaginated = isPaginated;
            return structure;
        }

        private static async Task<JObject?> InvokeExecuteQueryAsync(
            CosmosQueryStructure structure,
            QueryRequestOptions options,
            Container container,
            string id,
            string partitionKey)
        {
            MethodInfo method = typeof(CosmosQueryEngine).GetMethod(
                "ExecuteQueryAsync",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            return await (Task<JObject>)method.Invoke(
                null,
                new object[] { structure, new QueryDefinition("SELECT * FROM c"), options, container, id, partitionKey })!;
        }

        private static async Task<JObject> InvokeQueryByIdAndPartitionKey(Container container, bool isPaginated)
        {
            MethodInfo method = typeof(CosmosQueryEngine).GetMethod(
                "QueryByIdAndPartitionKey",
                BindingFlags.Static | BindingFlags.NonPublic)!;
            return await (Task<JObject>)method.Invoke(
                null,
                new object[] { container, "1", "tenant", isPaginated })!;
        }
    }
}
