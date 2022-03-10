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
query {{
    planet_by_pk (id: {0}) {{
        id
        name
    }}
}}";
        public static readonly string PlanetListQuery = @"{planetList{ id, name}}";
        public static readonly string PlanetConnectionQueryStringFormat = @"
query {{
    planets (first: {0}, continuation: {1}) {{
        items {{
            id
            name
        }}
        continuation
        hasNextPage
    }}
}}";

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
        public async Task GetItemByIdQuery()
        {
            // Run query
            string id = _idList[0];
            string query = string.Format(PlanetByIdQueryFormat,"\"" + id + "\"");
            JsonElement response = await ExecuteGraphQLRequestAsync("planet_by_pk", query);

            // Validate results
            Assert.AreEqual(id, response.GetProperty("id").GetString());
        }

        [TestMethod]
        public async Task GetUnfilteredPaginatedItems()
        {
            const int pagesize = TOTAL_ITEM_COUNT / 2;
            string continuationToken = "null";
            // Run query
            JsonElement response =
                await ExecuteGraphQLRequestAsync("planets", string.Format(PlanetConnectionQueryStringFormat, pagesize, continuationToken));
            continuationToken = response.GetProperty("continuation").GetString();
            int totalElementsFromPaginatedQuery = response.GetProperty("items").GetArrayLength();

            do
            {
                if (continuationToken != "null")
                {
                    // We need to append an escape quote to continuation token because of the way we are using string.format
                    // for generating the graphql paginated query stringformat for this test.
                    continuationToken = "\"" + continuationToken + "\"";
                }

                string paginatedQuery = string.Format(PlanetConnectionQueryStringFormat,pagesize, continuationToken);
                JsonElement page = await ExecuteGraphQLRequestAsync("planets", paginatedQuery);
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
