using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.Tests
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
    }
}
