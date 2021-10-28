using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using System.Threading.Tasks;

namespace Microsoft.GraphQL.Service.Tests
{
    [TestClass, TestCategory(TestCategory.Cosmos)]
    public class MutationTests : TestBase
    {
        [TestMethod]
        public async Task TestMutationRun()
        {
            // Add mutation resolver
            _metadataStoreProvider.StoreMutationResolver(TestHelper.SampleMutationResolver());

            // Run mutation;
            _controller.ControllerContext.HttpContext = GetHttpContextWithBody(TestHelper.SampleMutation);
            JsonDocument response = await _controller.PostAsync();

            // Validate results
            Assert.IsFalse(response.ToString().Contains("Error"));
        }
    }
}
