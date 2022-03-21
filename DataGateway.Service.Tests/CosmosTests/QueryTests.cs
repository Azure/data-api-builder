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

        public static readonly string PlanetByIdQueryFormat = @"
query ($id: ID) {
    planetById (id: $id) {
        id
        name
    }
}";
        public static readonly string PlanetListQuery = @"{planetList{ id, name}}";
        public static readonly string PlanetConnectionQueryStringFormat = @"
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
            JsonElement response = await ExecuteGraphQLRequestAsync("planetById", PlanetByIdQueryFormat, new() { { "id", id } });

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        [TestMethod]
        public async Task GetPaginatedWithVariables()
        {
            // Run query
            JsonElement response = await ExecuteGraphQLRequestAsync("planetList", PlanetListQuery);
            int actualElements = response.GetArrayLength();
            // Run paginated query
            int totalElementsFromPaginatedQuery = 0;
            string continuationToken = null;
            const int pagesize = 5;

            do
            {
                JsonElement page = await ExecuteGraphQLRequestAsync("planets", PlanetConnectionQueryStringFormat, new() { { "first", pagesize }, { "after", continuationToken } });
                JsonElement continuation = page.GetProperty("endCursor");
                continuationToken = continuation.ToString();
                totalElementsFromPaginatedQuery += page.GetProperty("items").GetArrayLength();
            } while (!string.IsNullOrEmpty(continuationToken));

            // Validate results
            Assert.AreEqual(actualElements, totalElementsFromPaginatedQuery);
        }

        [TestMethod]
        public async Task GetByPrimaryKeyWithoutVariables()
        {
            // Run query
            string id = _idList[0];
            string query = @$"
query {{
    planetById (id: ""{id}"") {{
        id
        name
    }}
}}";
            JsonElement response = await ExecuteGraphQLRequestAsync("planetById", query);

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        [TestMethod]
        public async Task GetPaginatedWithoutVariables()
        {
            // Run query
            JsonElement response = await ExecuteGraphQLRequestAsync("planetList", PlanetListQuery);
            int actualElements = response.GetArrayLength();
            // Run paginated query
            int totalElementsFromPaginatedQuery = 0;
            string continuationToken = null;
            const int pagesize = 5;

            do
            {
                string planetConnectionQueryStringFormat = @$"
query {{
    planets (first: {pagesize}, after: {(continuationToken == null ? "null" : "\"" + continuationToken + "\"")}) {{
        items {{
            id
            name
        }}
        endCursor
        hasNextPage
    }}
}}";

                JsonElement page = await ExecuteGraphQLRequestAsync("planets", planetConnectionQueryStringFormat, new());
                JsonElement continuation = page.GetProperty("endCursor");
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
