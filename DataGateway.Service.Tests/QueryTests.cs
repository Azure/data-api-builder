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

        [TestMethod]
        public async Task TestPaginatedQuery()
        {
            // Add query resolver
            _metadataStoreProvider.StoreQueryResolver(TestHelper.SimplePaginatedQueryResolver());
            _metadataStoreProvider.StoreQueryResolver(TestHelper.SimpleListQueryResolver());

            // Run query
            _controller.ControllerContext.HttpContext = GetHttpContextWithBody(TestHelper.SimpleListQuery);
            JsonDocument fullQueryResponse = await _controller.PostAsync();
            int actualElements = fullQueryResponse.RootElement.GetProperty("data").GetProperty("queryAll").GetArrayLength();

            // Run paginated query
            int totalElements = 0;
            string continuationToken = "null";
            const int pagesize = 15;

            do
            {
                if (continuationToken != "null")
                {
                    continuationToken = "\\\"" +
                        continuationToken.Replace(@"""", @"\\\""") +
                        "\\\"";
                }

                string paginatedQuery = string.Format(TestHelper.SimplePaginatedQueryFormat, arg0: pagesize, arg1: continuationToken);
                _controller.ControllerContext.HttpContext = GetHttpContextWithBody(paginatedQuery);
                JsonDocument paginatedQueryResponse = await _controller.PostAsync();
                JsonElement page = paginatedQueryResponse.RootElement
                    .GetProperty("data")
                    .GetProperty("paginatedQuery");
                JsonElement continuation = page.GetProperty("endCursor");
                continuationToken = continuation.ToString();

                totalElements += page.GetProperty("nodes").GetArrayLength();
            } while (!string.IsNullOrEmpty(continuationToken));

            // Validate results
            Assert.AreEqual(actualElements, totalElements);
        }
    }
}
