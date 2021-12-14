using Azure.DataGateway.Service.Tests.CosmosTests;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests
{
    [TestClass]
    public class MetadataProviderTests
    {
        IMetadataStoreProvider _fileProvider;

        public MetadataProviderTests()
        {
            _fileProvider = new FileMetadataStoreProvider(TestHelper.DataGatewayConfig);
        }

        [TestMethod]
        public void TestGetSchema()
        {
            Assert.IsNotNull(_fileProvider.GetGraphQLSchema());
        }

        [TestMethod]
        [Ignore] // TODO: moderakh we will re-enable, once we can run all components tests in the CI
        public void TestGetResolver()
        {
            Assert.IsNotNull(_fileProvider.GetQueryResolver("authorById"));
        }
    }
}
