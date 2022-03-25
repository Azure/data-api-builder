using Azure.DataGateway.Service.Services;
using Azure.DataGateway.Service.Tests.CosmosTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests
{
    [TestClass]
    public class MetadataProviderTests
    {
        IGraphQLMetadataProvider _fileProvider;

        public MetadataProviderTests()
        {
            _fileProvider = new GraphQLFileMetadataProvider(TestHelper.DataGatewayConfig);
        }

        [TestMethod]
        public void TestGetSchema()
        {
            Assert.IsNotNull(_fileProvider.GetGraphQLSchema());
        }
    }
}
