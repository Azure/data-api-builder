using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Azure.DataGateway.Service.Tests.CosmosTests
{
    public class TestBase
    {
        internal const string DATABASE_NAME = "graphqldb";
        internal static GraphQLService _graphQLService;
        internal static CosmosClientProvider _clientProvider;
        internal static MetadataStoreProviderForTest _metadataStoreProvider;
        internal static CosmosQueryEngine _queryEngine;
        internal static CosmosMutationEngine _mutationEngine;
        internal static GraphQLController _controller;
        internal static CosmosClient Client { get; private set; }

        [ClassInitialize]
        public static void Init(TestContext context)
        {
            _clientProvider = new CosmosClientProvider(TestHelper.ConfigPath);
            _metadataStoreProvider = new MetadataStoreProviderForTest
            {
                GraphQLSchema = File.ReadAllText("schema.gql")
            };
            _queryEngine = new CosmosQueryEngine(_clientProvider, _metadataStoreProvider);
            _mutationEngine = new CosmosMutationEngine(_clientProvider, _metadataStoreProvider);
            _graphQLService = new GraphQLService(
                TestHelper.ConfigPath,
                _queryEngine,
                _mutationEngine,
                _metadataStoreProvider,
                new DocumentCache(),
                new Sha256DocumentHashProvider(),
                sqlMetadataProvider: null);
            _controller = new GraphQLController(_graphQLService);
            Client = _clientProvider.Client;
        }

        /// <summary>
        /// Creates items on the specified container
        /// </summary>
        /// <param name="dbName">the database name</param>
        /// <param name="containerName">the container name</param>
        /// <param name="numItems">number of items to be created</param>
        internal static List<string> CreateItems(string dbName, string containerName, int numItems)
        {
            List<String> idList = new();
            for (int i = 0; i < numItems; i++)
            {
                string uid = Guid.NewGuid().ToString();
                idList.Add(uid);
                dynamic sourceItem = TestHelper.GetItem(uid);
                Client.GetContainer(dbName, containerName)
                    .CreateItemAsync(sourceItem, new PartitionKey(uid)).Wait();
            }

            return idList;
        }

        private static DefaultHttpContext GetHttpContextWithBody(string data)
        {
            HttpRequestMessage request = new();
            MemoryStream stream = new(Encoding.UTF8.GetBytes(data));
            request.Method = HttpMethod.Post;
            DefaultHttpContext httpContext = new()
            {
                Request = { Body = stream, ContentLength = stream.Length }
            };
            return httpContext;
        }

        /// <summary>
        /// Creates and registers a mutation resolver
        /// </summary>
        /// <param name="id">name of the mutation</param>
        /// <param name="databaseName">the database name</param>
        /// <param name="containerName">the container name</param>
        /// <param name="operationType">the type of operation. Defaults to UPSERT</param>
        internal static void RegisterMutationResolver(string id,
           string databaseName,
           string containerName,
           string operationType = "UPSERT")
        {
            string resolverJson = JObject.FromObject(new
            {
                id,
                databaseName,
                containerName,
                operationType,
            }).ToString();
            MutationResolver mutationResolver = JsonConvert.DeserializeObject<MutationResolver>(resolverJson);
            _metadataStoreProvider.StoreMutationResolver(mutationResolver);
        }

        /// <summary>
        /// Creates and registers a GraphQLType
        /// </summary>
        /// <param name="id">name of the mutation</param>
        /// <param name="databaseName">the database name</param>
        /// <param name="containerName">the container name</param>
        /// <param name="isPaginationType">is the type a pagination type</param>
        internal static void RegisterGraphQLType(string id,
           string databaseName,
           string containerName,
           bool isPaginationType = false)
        {
            string resolverJson = JObject.FromObject(new
            {
                databaseName,
                containerName,
                isPaginationType
            }).ToString();
            GraphQLType gqlType = JsonConvert.DeserializeObject<GraphQLType>(resolverJson);
            _metadataStoreProvider.StoreGraphQLType(id, gqlType);
        }

        /// <summary>
        /// Executes the GraphQL request and returns the results
        /// </summary>
        /// <param name="queryName"> Name of the GraphQL query/mutation</param>
        /// <param name="query"> The GraphQL query/mutation</param>
        /// <param name="variables">Variables to be included in the GraphQL request. If null, no variables property is included in the request, to pass an empty object provide an empty dictionary</param>
        /// <returns></returns>
        internal static async Task<JsonElement> ExecuteGraphQLRequestAsync(string queryName, string query, Dictionary<string, object> variables = null)
        {
            string queryJson = variables == null ?
                JObject.FromObject(new { query }).ToString() :
                JObject.FromObject(new
                {
                    query,
                    variables
                }).ToString();
            _controller.ControllerContext.HttpContext = GetHttpContextWithBody(queryJson);
            JsonElement graphQLResult = await _controller.PostAsync();

            if (graphQLResult.TryGetProperty("errors", out JsonElement errors))
            {
                Assert.Fail(errors.GetRawText());
            }

            return graphQLResult.GetProperty("data").GetProperty(queryName);
        }
    }
}
