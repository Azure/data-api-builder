using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.GraphQLBuilder.Queries;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.CosmosTests
{
    [TestClass, TestCategory(TestCategory.COSMOS)]
    public class QueryTests : TestBase
    {
        private static readonly string _containerName = Guid.NewGuid().ToString();

        public static readonly string PlanetByPKQuery = @"
query ($id: ID) {
    planet_by_pk (id: $id) {
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
            Init(context);
            Client.CreateDatabaseIfNotExistsAsync(DATABASE_NAME).Wait();
            Client.GetDatabase(DATABASE_NAME).CreateContainerIfNotExistsAsync(_containerName, "/id").Wait();
            _idList = CreateItems(DATABASE_NAME, _containerName, TOTAL_ITEM_COUNT);
            OverrideEntityContainer("Planet", _containerName);
        }

        [TestMethod]
        public async Task GetByPrimaryKeyWithVariables()
        {
            // Run query
            string id = _idList[0];
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", PlanetByPKQuery, new() { { "id", id } });

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
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
    planet_by_pk (id: ""{id}"") {{
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
    planet_by_pk (id: ""{id}"") {{
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
            Client.GetDatabase(DATABASE_NAME).GetContainer(_containerName).DeleteContainerAsync().Wait();
        }
    }
}
