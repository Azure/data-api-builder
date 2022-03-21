using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
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
query ($first: Int!, $continuation: String) {
    planets (first: $first, continuation: $continuation) {
        items {
            id
            name
        }
        continuation
        hasNextPage
    }
}";

        private static List<string> _idList;
        private const int TOTAL_ITEM_COUNT = 10;

        [ClassInitialize]
        public static void TestFixtureSetup(TestContext context)
        {
            Init(context);
            Client.CreateDatabaseIfNotExistsAsync(DATABASE_NAME).Wait();
            Client.GetDatabase(DATABASE_NAME).CreateContainerIfNotExistsAsync(_containerName, "/id").Wait();
            _idList = CreateItems(DATABASE_NAME, _containerName, TOTAL_ITEM_COUNT);
            RegisterGraphQLType("Planet", DATABASE_NAME, _containerName);
            RegisterGraphQLType("PlanetConnection", DATABASE_NAME, _containerName, true);
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
            const int pagesize = TOTAL_ITEM_COUNT / 2;
            string continuationToken = null;
            int totalElementsFromPaginatedQuery = 0;

            do
            {
                JsonElement page = await ExecuteGraphQLRequestAsync("planets", PlanetsQuery, new() { { "first", pagesize }, { "continuation", continuationToken } });
                JsonElement continuation = page.GetProperty("continuation");
                continuationToken = continuation.ToString();
                totalElementsFromPaginatedQuery += page.GetProperty("items").GetArrayLength();
            } while (!string.IsNullOrEmpty(continuationToken));

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
            const int pagesize = TOTAL_ITEM_COUNT / 2;
            int totalElementsFromPaginatedQuery = 0;
            string continuationToken = null;

            do
            {
                string planetConnectionQueryStringFormat = @$"
query {{
    planets (first: {pagesize}, continuation: {(continuationToken == null ? "null" : "\"" + continuationToken + "\"")}) {{
        items {{
            id
            name
        }}
        continuation
        hasNextPage
    }}
}}";

                JsonElement page = await ExecuteGraphQLRequestAsync("planets", planetConnectionQueryStringFormat, new());
                JsonElement continuation = page.GetProperty("continuation");
                continuationToken = continuation.ToString();
                totalElementsFromPaginatedQuery += page.GetProperty("items").GetArrayLength();
            } while (!string.IsNullOrEmpty(continuationToken));

            // Validate results
            Assert.AreEqual(TOTAL_ITEM_COUNT, totalElementsFromPaginatedQuery);
        }

        [ClassCleanup]
        public static void TestFixtureTearDown()
        {
            Client.GetDatabase(DATABASE_NAME).GetContainer(_containerName).DeleteContainerAsync().Wait();
        }
    }
}
