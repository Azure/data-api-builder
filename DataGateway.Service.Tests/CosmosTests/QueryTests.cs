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
query ($first: Int, $after: String) {
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

        /// <summary>
        /// Executes once for the test class.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static void TestFixtureSetup(TestContext context)
        {
            Init(context);
            Client.CreateDatabaseIfNotExistsAsync(DATABASE_NAME).Wait();
            Client.GetDatabase(DATABASE_NAME).CreateContainerIfNotExistsAsync(_containerName, "/id").Wait();
            _idList = CreateItems(DATABASE_NAME, _containerName, 10);
            RegisterGraphQLType("Planet", DATABASE_NAME, _containerName);
            RegisterGraphQLType("PlanetConnection", DATABASE_NAME, _containerName, true);
        }

        [TestMethod]
        public async Task TestSimpleQuery()
        {
            // Run query
            string id = _idList[0];
            JsonElement response = await ExecuteGraphQLRequestAsync("planetById", PlanetByIdQueryFormat, new() { { "id", id } });

            // Validate results
            Assert.IsFalse(response.ToString().Contains("Error"));
        }

        /// <summary>
        /// This test runs a query to list all the items in a container. Then, gets all the items by
        /// running a paginated query that gets n items per page. We then make sure the number of documents match
        /// </summary>
        [TestMethod]
        public async Task TestPaginatedQuery()
        {
            // Run query
            JsonElement response = await ExecuteGraphQLRequestAsync("planetList", PlanetListQuery, new());
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

        /// <summary>
        /// Runs once after all tests in this class are executed
        /// </summary>
        [ClassCleanup]
        public static void TestFixtureTearDown()
        {
            Client.GetDatabase(DATABASE_NAME).GetContainer(_containerName).DeleteContainerAsync().Wait();
        }

    }
}
