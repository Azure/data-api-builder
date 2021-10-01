using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using System.Text;
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
            _metadataStoreProvider.StoreQueryResolver(TestHelper.SampleQueryResolver());

            // Write JSON request to the body.
            RequestDataMock.Object.Body.Write(Encoding.UTF8.GetBytes(TestHelper.SampleQuery));
            // Reset the stream to the beginning.
            RequestDataMock.Object.Body.Seek(0, SeekOrigin.Begin);

            // Run the query.
            HttpResponseData responseData = await _controller.Run(RequestDataMock.Object, FunctionContext);
            responseData.Body.Seek(0, SeekOrigin.Begin);
            JsonDocument responseJson = JsonDocument.Parse(responseData.Body);

            Assert.IsFalse(responseJson.ToString().Contains("Error"));
        }
    }
}
