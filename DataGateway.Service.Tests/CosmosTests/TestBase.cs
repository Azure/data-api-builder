using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Auth;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Tests.GraphQLBuilder.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Cosmos.Fluent;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;

namespace Azure.DataGateway.Service.Tests.CosmosTests
{
    public class TestBase
    {
        internal const string DATABASE_NAME = "graphqldb";
        private const string GRAPHQL_SCHEMA = @"
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

        private static string[] _planets = { "Earth", "Mars", "Jupiter", "Tatooine", "Endor", "Dagobah", "Hoth", "Bespin", "Spec%ial"};

        internal CosmosClient CosmosClient { get; private set; }

        private HttpClient _client;
        private WebApplicationFactory<Program> _application;

        [ClassInitialize]
        public void Init(TestContext context)
        {
            MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>()
            {
                { @"./schema.gql", new MockFileData(GRAPHQL_SCHEMA) }
            });

            //create mock authorization resolver where mock entityPermissionsMap is created for Planet and Character.
            Mock<IAuthorizationResolver> authorizationResolverCosmos = new();
            _ = authorizationResolverCosmos.Setup(x => x.EntityPermissionsMap).Returns(GetEntityPermissionsMap(new string[] { "Character", "Planet" }));

            _application = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    _ = builder.ConfigureServices(services =>
                    {
                        services.RemoveAll<IFileSystem>();
                        services.AddSingleton<IFileSystem>(fileSystem);

                        services.RemoveAll<RuntimeConfigProvider>();
                        services.AddSingleton(TestHelper.ConfigProvider);

                        services.RemoveAll<IAuthorizationResolver>();
                        services.AddSingleton(authorizationResolverCosmos.Object);
                    });
                });

            CosmosClient = new CosmosClientBuilder(TestHelper.ConfigProvider.GetRuntimeConfiguration().ConnectionString).Build();

            _client = _application.CreateClient();
        }

        /// <summary>
        /// Creates items on the specified container
        /// </summary>
        /// <param name="dbName">the database name</param>
        /// <param name="containerName">the container name</param>
        /// <param name="numItems">number of items to be created</param>
        internal List<string> CreateItems(string dbName, string containerName, int numItems)
        {
            List<string> idList = new();
            for (int i = 0; i < numItems; i++)
            {
                string uid = Guid.NewGuid().ToString();
                idList.Add(uid);
                dynamic sourceItem = TestHelper.GetItem(uid, _planets[i % (_planets.Length)], i);
                CosmosClient.GetContainer(dbName, containerName)
                    .CreateItemAsync(sourceItem, new PartitionKey(uid)).Wait();
            }

            return idList;
        }

        private static DefaultHttpContext GetHttpContextWithBody(string data)
        {
            HttpRequestMessage request = new();
            MemoryStream stream = new(Encoding.UTF8.GetBytes(data));
            request.Method = HttpMethod.Post;

            //Add identity object to the Mock context object.
            ClaimsIdentity identity = new(authenticationType: "Bearer");
            identity.AddClaim(new Claim(ClaimTypes.Role, "anonymous"));
            identity.AddClaim(new Claim(ClaimTypes.Role, "authenticated"));

            ClaimsPrincipal user = new(identity);
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
        internal async Task<JsonElement> ExecuteGraphQLRequestAsync(string queryName, string query, Dictionary<string, object> variables = null)
        {
            string queryJson = variables == null ?
                JObject.FromObject(new { query }).ToString() :
                JObject.FromObject(new
                {
                    query,
                    variables
                }).ToString();

            string graphQLEndpoint = TestHelper.ConfigProvider.GetRuntimeConfiguration().GraphQLGlobalSettings.Path;

            // todo: set the stuff that use to be on HttpContext
            HttpResponseMessage responseMessage = await _client.PostAsync(graphQLEndpoint, new StringContent(queryJson));
            string body = await responseMessage.Content.ReadAsStringAsync();

            JsonElement graphQLResult = JsonSerializer.Deserialize<JsonElement>(body);

            if (graphQLResult.TryGetProperty("errors", out JsonElement errors))
            {
                // to validate expected errors and error message
                return errors;
            }

            return graphQLResult.GetProperty("data").GetProperty(queryName);
        }

        internal async Task<JsonDocument> ExecuteCosmosRequestAsync(string query, int pagesize, string continuationToken, string containerName)
        {
            QueryRequestOptions options = new()
            {
                MaxItemCount = pagesize,
            };
            Container c = CosmosClient.GetContainer(DATABASE_NAME, containerName);
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

        private static Dictionary<string, EntityMetadata> GetEntityPermissionsMap(string[] entities)
        {
            return GraphQLTestHelpers.CreateStubEntityPermissionsMap(
                    entityNames: entities,
                    actionNames: new string[] { ActionType.CREATE, ActionType.READ, ActionType.UPDATE, ActionType.DELETE },
                    roles: new string[] { "anonymous", "authenticated" }
                );
        }

    }
}
