using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.CosmosTests
{
    [TestClass, TestCategory(TestCategory.COSMOS)]
    public class QueryTests : TestBase
    {
        private static readonly string _containerName = Guid.NewGuid().ToString();

        public static readonly string PlanetByIdQuery = @"{planetById{ id, name}}";
        public static readonly string PlanetListQuery = @"{planetList{ id, name}}";
        public static readonly string PlanetConnectionQueryStringFormat = @"
            {{planets (first: {0}, after: {1}){{
                 items{{ id  name }}
                 endCursor
                 hasNextPage
                }}
            }}";

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
            CreateItems(DATABASE_NAME, _containerName, 10);
            RegisterQueryResolver("planetList", DATABASE_NAME, _containerName);
            RegisterQueryResolver("planets", DATABASE_NAME, _containerName, isPaginated: true);
            RegisterQueryResolver("planetById", DATABASE_NAME, _containerName, isPaginated: false);
        }

        [TestMethod]
        public async Task TestSimpleQuery()
        {
            // Run query
            JsonElement response = await ExecuteGraphQLRequestAsync("planetById", PlanetByIdQuery);

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
            JsonElement response = await ExecuteGraphQLRequestAsync("planetList", PlanetListQuery);
            int actualElements = response.GetArrayLength();
            // Run paginated query
            int totalElementsFromPaginatedQuery = 0;
            string continuationToken = "null";
            const int pagesize = 5;

            do
            {
                if (continuationToken != "null")
                {
                    // We need to append an escape quote to continuation token because of the way we are using string.format
                    // for generating the graphql paginated query stringformat for this test.
                    continuationToken = "\"" + continuationToken + "\"";
                }

                string paginatedQuery = string.Format(PlanetConnectionQueryStringFormat, arg0: pagesize, arg1: continuationToken);
                JsonElement page = await ExecuteGraphQLRequestAsync("planets", paginatedQuery);
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
