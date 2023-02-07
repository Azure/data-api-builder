using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Resolvers;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class QueryTests : TestBase
    {
        private static readonly string _containerName = Guid.NewGuid().ToString();

        public static readonly string PlanetByPKQuery = @"
query ($id: ID, $partitionKeyValue: String) {
    planet_by_pk (id: $id, _partitionKeyValue: $partitionKeyValue) {
        id
        name
    }
}";
        public static readonly string PlanetsQuery = @"
query ($first: Int!, $after: String) {
    planets (first: $first, after: $after) {
        items {
            id
            name
        }
        endCursor
        hasNextPage
    }
}";
        public static readonly string PlanetsWithOrderBy = @"
query{
    planets (first: 10, after: null, orderBy: {id: ASC, name: null }) {
        items {
            id
            name
        },
        endCursor,
        hasNextPage
    }
}
";
        private static List<string> _idList;
        private const int TOTAL_ITEM_COUNT = 10;

        [ClassInitialize]
        public static void TestFixtureSetup(TestContext context)
        {
            CosmosClient cosmosClient = _application.Services.GetService<CosmosClientProvider>().Client;
            cosmosClient.CreateDatabaseIfNotExistsAsync(TEMP_DATABASE_NAME).Wait();
            cosmosClient.GetDatabase(TEMP_DATABASE_NAME).CreateContainerIfNotExistsAsync(_containerName, "/id").Wait();
            _idList = CreateItems(TEMP_DATABASE_NAME, _containerName, TOTAL_ITEM_COUNT);
            OverrideEntityContainer("Planet", _containerName);
            OverrideEntityContainer("StarAlias", _containerName);
        }

        [TestMethod]
        public async Task GetByPrimaryKeyWithVariables()
        {
            

            // Run query
            string id = _idList[0];
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", PlanetByPKQuery, new() { { "id", id }, { "partitionKeyValue", id } });

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        [TestMethod]
        public async Task GetListOfString()
        {
            

            string id = _idList[0];
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", @"
query ($id: ID, $partitionKeyValue: String) {
    planet_by_pk (id: $id, _partitionKeyValue: $partitionKeyValue) {
        tags
    }
}", new() { { "id", id }, { "partitionKeyValue", id } });

            string[] tags = response.GetProperty("tags").Deserialize<string[]>();
            Assert.AreEqual(2, tags.Length);
            CollectionAssert.AreEqual(new[] { "tag1", "tag2" }, tags);
        }

        [TestMethod]
        public async Task GetPaginatedWithVariables()
        {
            // Run paginated query
            const int pagesize = TOTAL_ITEM_COUNT / 2;
            int totalElementsFromPaginatedQuery = 0;
            string afterToken = null;

            do
            {
                JsonElement page = await ExecuteGraphQLRequestAsync("planets",
                    PlanetsQuery, new() { { "first", pagesize }, { "after", afterToken } });
                JsonElement after = page.GetProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME);
                afterToken = after.ToString();
                totalElementsFromPaginatedQuery += page.GetProperty(QueryBuilder.PAGINATION_FIELD_NAME).GetArrayLength();
            } while (!string.IsNullOrEmpty(afterToken));

            // Validate results
            Assert.AreEqual(TOTAL_ITEM_COUNT, totalElementsFromPaginatedQuery);
        }

        [TestMethod]
        public async Task GetByPrimaryKeyWithoutVariables()
        {
            

            // Run query
            string id = _idList[0];
            string query = @$"
query {{
    planet_by_pk (id: ""{id}"", _partitionKeyValue: ""{id}"") {{
        id
        name
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", query);

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        [TestMethod]
        public async Task GetPaginatedWithoutVariables()
        {
            

            // Run paginated query
            const int pagesize = TOTAL_ITEM_COUNT / 2;
            int totalElementsFromPaginatedQuery = 0;
            string afterToken = null;
            List<string> pagedResponse = new();

            do
            {
                string planetConnectionQueryStringFormat = @$"
query {{
    planets (first: {pagesize}, after: {(afterToken == null ? "null" : "\"" + afterToken + "\"")}) {{
        items {{
            id
            name
        }}
        endCursor
        hasNextPage
    }}
}}";
                JsonElement page = await ExecuteGraphQLRequestAsync("planets", planetConnectionQueryStringFormat, variables: new());
                JsonElement after = page.GetProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME);
                afterToken = after.ToString();
                totalElementsFromPaginatedQuery += page.GetProperty(QueryBuilder.PAGINATION_FIELD_NAME).GetArrayLength();
                ConvertJsonElementToStringList(page.GetProperty(QueryBuilder.PAGINATION_FIELD_NAME), pagedResponse);
            } while (!string.IsNullOrEmpty(afterToken));

            Assert.AreEqual(TOTAL_ITEM_COUNT, totalElementsFromPaginatedQuery);
            
        }

        [TestMethod]
        public async Task GetPaginatedWithSinglePartition()
        {
            

            // Run paginated query
            const int pagesize = TOTAL_ITEM_COUNT / 2;
            int totalElementsFromPaginatedQuery = 0;
            string afterToken = null;
            List<string> pagedResponse = new();
            string id = _idList[0];

            do
            {
                string planetConnectionQueryStringFormat = @$"
query {{
    planets (first: {pagesize}, after: {(afterToken == null ? "null" : "\"" + afterToken + "\"")},
    {QueryBuilder.FILTER_FIELD_NAME}: {{ id: {{eq: ""{id}""}} }}) {{
        items {{
            id
            name
        }}
        endCursor
        hasNextPage
    }}
}}";
                JsonElement page = await ExecuteGraphQLRequestAsync("planets", planetConnectionQueryStringFormat, variables: new());
                JsonElement after = page.GetProperty(QueryBuilder.PAGINATION_TOKEN_FIELD_NAME);
                afterToken = after.ToString();
                totalElementsFromPaginatedQuery += page.GetProperty(QueryBuilder.PAGINATION_FIELD_NAME).GetArrayLength();
                ConvertJsonElementToStringList(page.GetProperty(QueryBuilder.PAGINATION_FIELD_NAME), pagedResponse);
            } while (!string.IsNullOrEmpty(afterToken));

            Assert.AreEqual(1, totalElementsFromPaginatedQuery);
            
        }

        /// <summary>
        /// Query result with nested object
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task GetByPrimaryKeyWithInnerObject()
        {
            
            // Run query
            string id = _idList[0];
            string query = @$"
query {{
    planet_by_pk (id: ""{id}"", _partitionKeyValue: ""{id}"") {{
        id
        name
        character {{
            id
            name
        }}
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", query);

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
            
        }

        [TestMethod]
        public async Task GetWithOrderBy()
        {
            
            JsonElement response = await ExecuteGraphQLRequestAsync("planets", PlanetsWithOrderBy);

            int i = 0;
            // Check order matches
            foreach (string id in _idList.OrderBy(x => x))
            {
                Assert.AreEqual(id, response.GetProperty("items")[i++].GetProperty("id").GetString());
            }

            
        }

        /// <summary>
        /// This is to exercise the scenario when the GraphQL type and top-level entity name in the runtime config do not match.
        /// "Star" is a GraphQL type, in the runtime config, the top level entity name is "StarAlias"
        /// A match is attempted using the runtime config entity singular type name when there is no match found with the GraphQL type name.
        /// </summary>
        [TestMethod]
        public async Task GetByPrimaryKeyWhenEntityNameDoesntMatchGraphQLType()
        {
            
            // Run query
            // _idList is the mock data that's generated for testing purpose, arbitrarilys pick the first id here to query.
            string id = _idList[0];
            string query = @$"
query {{
    star_by_pk (id: ""{id}"", _partitionKeyValue: ""{id}"") {{
        id
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("star_by_pk", query);

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
            
        }

        private static void ConvertJsonElementToStringList(JsonElement ele, List<string> strList)
        {
            if (ele.ValueKind == JsonValueKind.Array)
            {
                JsonElement.ArrayEnumerator enumerator = ele.EnumerateArray();

                while (enumerator.MoveNext())
                {
                    JsonElement prop = enumerator.Current;
                    strList.Add(prop.ToString());
                }
            }
        }

        [ClassCleanup]
        public static void TestFixtureTearDown()
        {
            CosmosClient cosmosClient = _application.Services.GetService<CosmosClientProvider>().Client;
            cosmosClient.GetDatabase(TEMP_DATABASE_NAME).GetContainer(_containerName).DeleteContainerAsync().Wait();
        }
    }
}
