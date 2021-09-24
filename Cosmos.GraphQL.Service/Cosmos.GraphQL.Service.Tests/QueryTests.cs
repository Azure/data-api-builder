using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using System.Threading.Tasks;

namespace Cosmos.GraphQL.Service.Tests
{
    [TestClass, TestCategory(TestCategory.Cosmos)]
    public class QueryTests : TestBase
    {
        [TestMethod]
        public async Task TestSimpleQuery()
        {

            // Add query resolver
            this.metadataStoreProvider.StoreQueryResolver(TestHelper.SampleQueryResolver());

            // Run query
            controller.ControllerContext.HttpContext = GetHttpContextWithBody(TestHelper.SampleQuery);
            JsonDocument response = await controller.Post();

            // Validate results
            Assert.IsFalse(response.ToString().Contains("Error"));
        }
    }
}
