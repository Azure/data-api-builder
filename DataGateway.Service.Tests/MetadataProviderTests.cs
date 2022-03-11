using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Services;
using Azure.DataGateway.Service.Tests.CosmosTests;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests
{
    [TestClass]
    public class MetadataProviderTests
    {
        IMetadataStoreProvider _fileProvider;

        public MetadataProviderTests()
        {
            _fileProvider = new FileMetadataStoreProvider(TestHelper.DataGatewayConfigMonitor);
        }

        [TestMethod]
        public void TestGetSchema()
        {
            Assert.IsNotNull(_fileProvider.GetGraphQLSchema());
        }
    }
}
