using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Services;
using Azure.DataGateway.Service.Tests.CosmosTests;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests
{
    [TestClass]
    public class MetadataProviderTests
    {
        protected IMetadataStoreProvider FileProvider { get; set; }

        public MetadataProviderTests()
        {
            FileProvider = new FileMetadataStoreProvider(TestHelper.DataGatewayConfig);
        }

        public MetadataProviderTests(IOptions<DataGatewayConfig> dataGatewayConfig)
        {
            FileProvider = new FileMetadataStoreProvider(dataGatewayConfig);
        }

        [TestMethod]
        public void TestGetSchema()
        {
            Assert.IsNotNull(FileProvider.GetGraphQLSchema());
        }
    }
}
