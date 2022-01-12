using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;
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
            _clientProvider = new CosmosClientProvider(TestHelper.DataGatewayConfig);
            _metadataStoreProvider = new MetadataStoreProviderForTest();
            string jsonString = File.ReadAllText("schema.gql");
            _metadataStoreProvider.GraphqlSchema = jsonString;
            _queryEngine = new CosmosQueryEngine(_clientProvider, _metadataStoreProvider);
            _mutationEngine = new CosmosMutationEngine(_clientProvider, _metadataStoreProvider);
            _graphQLService = new GraphQLService(_queryEngine, _mutationEngine, _metadataStoreProvider);
            _controller = new GraphQLController(_graphQLService);
            Client = _clientProvider.Client;
        }

        /// <summary>
        /// Creates items on the specified container
        /// </summary>
        /// <param name="dbName">the database name</param>
        /// <param name="containerName">the container name</param>
        /// <param name="numItems">number of items to be created</param>
        internal static void CreateItems(string dbName, string containerName, int numItems)
        {
            for (int i = 0; i < numItems; i++)
            {
                string uid = Guid.NewGuid().ToString();
                dynamic sourceItem = TestHelper.GetItem(uid);
                Client.GetContainer(dbName, containerName)
                    .CreateItemAsync(sourceItem, new PartitionKey(uid)).Wait();
            }
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
        /// Generates a query resolver for cosmos that looks like below
        /// {
        ///  "id": "queryName",
        ///  "isPaginated": isPaginated,
        ///  "databaseName": databaseName,
        ///  "containerName": containerName,
        ///  "parametrizedQuery": queryString
        /// }
        /// </summary>
        /// <param name="id"></param>
        /// <param name="databaseName"></param>
        /// <param name="containerName"></param>
        /// <param name="parametrizedQuery"></param>
        /// <param name="isPaginated"></param>
        /// <returns></returns>
        internal static void RegisterQueryResolver(string id,
            string databaseName,
            string containerName,
            string parametrizedQuery = "select * from c",
            bool isPaginated = false)
        {
            string queryResolver = JObject.FromObject(new
            {
                id,
                databaseName,
                containerName,
                isPaginated,
                parametrizedQuery
            }).ToString();
            GraphQLQueryResolver graphQLQueryResolver = JsonConvert.DeserializeObject<GraphQLQueryResolver>(queryResolver);
            _metadataStoreProvider.StoreQueryResolver(graphQLQueryResolver);
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
        /// Executes the GraphQL request and returns the results
        /// </summary>
        /// <param name="queryName"> Name of the GraphQL query/mutation</param>
        /// <param name="graphQLQuery"> The GraphQL query/mutation</param>
        /// <returns></returns>
        internal static async Task<JsonElement> ExecuteGraphQLRequestAsync(string queryName, string graphQLQuery)
        {
            string queryJson = JObject.FromObject(new
            {
                query = graphQLQuery
            }).ToString();
            _controller.ControllerContext.HttpContext = GetHttpContextWithBody(queryJson);
            JsonElement graphQLResult = await _controller.PostAsync();
            return graphQLResult.GetProperty("data").GetProperty(queryName);
        }
    }
}
