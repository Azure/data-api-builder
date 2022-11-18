using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.DataApiBuilder.Service.Tests.GraphQLBuilder.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{
    public class TestBase
    {
        internal const string DATABASE_NAME = "graphqldb";
        private const string GRAPHQL_SCHEMA = @"
type Character @model(name:""Character"") {
    id : ID,
    name : String,
    type: String,
    homePlanet: Int,
    primaryFunction: String,
    star: Star
}

type Planet @model(name:""Planet"") {
    id : ID,
    name : String,
    character: Character,
    age : Int,
    dimension : String,
    stars: [Star]
}

type Star @model(name:""StarAlias"") {
    id : ID,
    name : String
}";

        private static string[] _planets = { "Earth", "Mars", "Jupiter", "Tatooine", "Endor", "Dagobah", "Hoth", "Bespin", "Spec%ial" };

        private static HttpClient _client;
        internal static WebApplicationFactory<Startup> _application;

        [ClassInitialize(InheritanceBehavior.BeforeEachDerivedClass)]
        public static void Init(TestContext context)
        {
            MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>()
            {
                { @"./schema.gql", new MockFileData(GRAPHQL_SCHEMA) }
            });

            //create mock authorization resolver where mock entityPermissionsMap is created for Planet and Character.
            Mock<IAuthorizationResolver> authorizationResolverCosmos = new();
            authorizationResolverCosmos.Setup(x => x.EntityPermissionsMap).Returns(GetEntityPermissionsMap(new string[] { "Character", "Planet", "StarAlias" }));

            _application = new WebApplicationFactory<Startup>()
                .WithWebHostBuilder(builder =>
                {
                    _ = builder.ConfigureTestServices(services =>
                    {
                        services.AddSingleton<IFileSystem>(fileSystem);
                        services.AddSingleton(TestHelper.GetRuntimeConfigProvider(CosmosTestHelper.ConfigPath));
                        services.AddSingleton(authorizationResolverCosmos.Object);
                    });
                });

            RuntimeConfigProvider configProvider = _application.Services.GetService<RuntimeConfigProvider>();

            _client = _application.CreateClient();
        }

        /// <summary>
        /// Creates items on the specified container
        /// </summary>
        /// <param name="dbName">the database name</param>
        /// <param name="containerName">the container name</param>
        /// <param name="numItems">number of items to be created</param>
        internal static List<string> CreateItems(string dbName, string containerName, int numItems)
        {
            List<string> idList = new();
            CosmosClient cosmosClient = _application.Services.GetService<CosmosClientProvider>().Client;
            for (int i = 0; i < numItems; i++)
            {
                string uid = Guid.NewGuid().ToString();
                idList.Add(uid);
                dynamic sourceItem = CosmosTestHelper.GetItem(uid, _planets[i % (_planets.Length)], i);
                cosmosClient.GetContainer(dbName, containerName)
                    .CreateItemAsync(sourceItem, new PartitionKey(uid)).Wait();
            }

            return idList;
        }

        /// <summary>
        /// Overrides the container than an entity will be saved to
        /// </summary>
        /// <param name="entityName">name of the mutation</param>
        /// <param name="containerName">the container name</param>
        internal static void OverrideEntityContainer(string entityName, string containerName)
        {
            RuntimeConfigProvider configProvider = _application.Services.GetService<RuntimeConfigProvider>();
            RuntimeConfig config = configProvider.GetRuntimeConfiguration();
            Entity entity = config.Entities[entityName];

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
        internal static Task<JsonElement> ExecuteGraphQLRequestAsync(string queryName, string query, Dictionary<string, object> variables = null)
        {
            RuntimeConfigProvider configProvider = _application.Services.GetService<RuntimeConfigProvider>();
            return GraphQLRequestExecutor.PostGraphQLRequestAsync(_client, configProvider, queryName, query, variables);
        }

        internal static async Task<JsonDocument> ExecuteCosmosRequestAsync(string query, int pagesize, string continuationToken, string containerName)
        {
            QueryRequestOptions options = new()
            {
                MaxItemCount = pagesize,
            };
            CosmosClient cosmosClient = _application.Services.GetService<CosmosClientProvider>().Client;
            Container c = cosmosClient.GetContainer(DATABASE_NAME, containerName);
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
                    operations: new Operation[] { Operation.Create, Operation.Read, Operation.Update, Operation.Delete },
                    roles: new string[] { "anonymous", "authenticated" }
                );
        }

    }
}
