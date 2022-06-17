using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Azure.DataGateway.Service.Services.MetadataProviders;
using Azure.DataGateway.Service.Tests.Authorization;
using HotChocolate.Language;
using Microsoft.AspNetCore.Http;
using Microsoft.Azure.Cosmos;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Newtonsoft.Json.Linq;

namespace Azure.DataGateway.Service.Tests.CosmosTests
{
    public class TestBase
    {
        internal const string DATABASE_NAME = "graphqldb";
        internal static GraphQLService _graphQLService;
        internal static CosmosClientProvider _clientProvider;
        internal static CosmosQueryEngine _queryEngine;
        internal static CosmosMutationEngine _mutationEngine;
        internal static GraphQLController _controller;
        internal static CosmosClient Client { get; private set; }

        [ClassInitialize]
        public static void Init(TestContext context)
        {
            _clientProvider = new CosmosClientProvider(TestHelper.ConfigProvider);
            string jsonString = @"
type Character @model {
    id : ID,
    name : String,
    type: String,
    homePlanet: Int,
    primaryFunction: String
}

type Planet @model {
    id : ID,
    name : String,
    character: Character,
    age : Int,
    dimension : String
}";
            MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>()
            {
                { @"./schema.gql", new MockFileData(jsonString) }
            });

            CosmosSqlMetadataProvider _metadataStoreProvider = new(TestHelper.ConfigProvider, fileSystem);
            RuntimeConfig runtimeConfig = AuthorizationHelpers.InitRuntimeConfig();
            AuthorizationResolver authZResolver = AuthorizationHelpers.InitAuthorizationResolver(runtimeConfig);

            _queryEngine = new CosmosQueryEngine(_clientProvider, _metadataStoreProvider);
            _mutationEngine = new CosmosMutationEngine(_clientProvider, _metadataStoreProvider);
            _graphQLService = new GraphQLService(
                TestHelper.ConfigProvider,
                _queryEngine,
                _mutationEngine,
                new DocumentCache(),
                new Sha256DocumentHashProvider(),
                _metadataStoreProvider,
                authZResolver);
            _controller = new GraphQLController(_graphQLService);
            Client = _clientProvider.Client;
        }

        private static string[] _planets = { "Earth", "Mars", "Jupiter",
            "Tatooine", "Endor", "Dagobah", "Hoth", "Bespin", "Spec%ial"};

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
                dynamic sourceItem = TestHelper.GetItem(uid, _planets[i % (_planets.Length)], i);
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
            ClaimsPrincipal user = new(new ClaimsIdentity(authenticationType: "Bearer"));
            DefaultHttpContext httpContext = new()
            {
                Request = { Body = stream, ContentLength = stream.Length },
                User = user
            };
            return httpContext;
        }

        /// <summary>
        /// Overrides the container than an entity will be saved to
        /// </summary>
        /// <param name="entityName">name of the mutation</param>
        /// <param name="containerName">the container name</param>
        internal static void OverrideEntityContainer(string entityName, string containerName)
        {
            Entity entity = TestHelper.Config.Entities[entityName];

            System.Reflection.PropertyInfo prop = entity.GetType().GetProperty("Source");
            // Use reflection to set the entity Source (since `entity` is a record type and technically immutable)
            // But it has to be a JsonElement, which we can only make by parsing JSON, so we do that then grab the property
            prop.SetValue(entity, JsonDocument.Parse(@$"{{ ""value"": ""{containerName}"" }}").RootElement.GetProperty("value"));
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
                // to validate expected errors and error message
                return errors;
            }

            return graphQLResult.GetProperty("data").GetProperty(queryName);
        }

        internal static async Task<JsonDocument> ExecuteCosmosRequestAsync(string query, int pagesize, string continuationToken, string containerName)
        {
            QueryRequestOptions options = new()
            {
                MaxItemCount = pagesize,
            };
            Container c = Client.GetContainer(DATABASE_NAME, containerName);
            QueryDefinition queryDef = new(query);
            FeedIterator<JObject> resultSetIterator = c.GetItemQueryIterator<JObject>(queryDef, continuationToken, options);
            FeedResponse<JObject> firstPage = await resultSetIterator.ReadNextAsync();
            JArray jarray = new();
            IEnumerator<JObject> enumerator = firstPage.GetEnumerator();
            while (enumerator.MoveNext())
            {
                JObject item = enumerator.Current;
                jarray.Add(item);
            }

            return JsonDocument.Parse(jarray.ToString().Trim());

        }

    }
}
