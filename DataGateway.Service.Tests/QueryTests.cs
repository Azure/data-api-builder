using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests
{
    [TestClass, TestCategory(TestCategory.COSMOS)]
    public class QueryTests : TestBase
    {
        [TestMethod]
        public async Task TestSimpleQuery()
        {
            // Add query resolver
            _metadataStoreProvider.StoreQueryResolver(TestHelper.SampleQueryResolver());

            // Run query
            _controller.ControllerContext.HttpContext = GetHttpContextWithBody(TestHelper.SampleQuery);
            JsonDocument response = await _controller.PostAsync();

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
            // Add query resolver
            _metadataStoreProvider.StoreQueryResolver(TestHelper.SimplePaginatedQueryResolver());
            _metadataStoreProvider.StoreQueryResolver(TestHelper.SimpleListQueryResolver());

            // Run query
            int actualElements = 0;
            _controller.ControllerContext.HttpContext = GetHttpContextWithBody(TestHelper.SimpleListQuery);
            using (JsonDocument fullQueryResponse = await _controller.PostAsync())
            {
                actualElements = fullQueryResponse.RootElement.GetProperty("data").GetProperty("queryAll").GetArrayLength();
            }
            // Run paginated query
            int totalElementsFromPaginatedQuery = 0;
            string continuationToken = "null";
            const int pagesize = 15;

            do
            {
                if (continuationToken != "null")
                {
                    // We need to append an escape quote to continuation token because of the way we are using string.format
                    // for generating the graphql paginated query stringformat for this test.
                    continuationToken = "\\\"" + continuationToken + "\\\"";
                }

                string paginatedQuery = string.Format(TestHelper.SimplePaginatedQueryFormat, arg0: pagesize, arg1: continuationToken);
                _controller.ControllerContext.HttpContext = GetHttpContextWithBody(paginatedQuery);
                using JsonDocument paginatedQueryResponse = await _controller.PostAsync();
                JsonElement page = paginatedQueryResponse.RootElement
                    .GetProperty("data")
                    .GetProperty("paginatedQuery");
                JsonElement continuation = page.GetProperty("endCursor");
                continuationToken = continuation.ToString();
                totalElementsFromPaginatedQuery += page.GetProperty("nodes").GetArrayLength();
            } while (!string.IsNullOrEmpty(continuationToken));

            // Validate results
            Assert.AreEqual(actualElements, totalElementsFromPaginatedQuery);
        }
    }
}
