using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Tests.CosmosTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit testing for the CosmosMetadataProvider
    /// </summary>
    [TestClass, TestCategory(TestCategory.COSMOS)]
    public class CosmosSqlMetadataProviderUnitTests : TestBase
    {
        RuntimeConfigProvider _runtimeConfigProvider;

        /// <summary>
        /// Make sure lower case container name would not result in not finding the entity.
        /// </summary>
        [DataTestMethod]
        public void TestGetDatabaseConfig()
        {
            _runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(CosmosTestHelper.ConfigPath);
            CosmosSqlMetadataProvider cosmosSqlMetadataProvider = new(_runtimeConfigProvider, null);

            string name = cosmosSqlMetadataProvider.GetDatabaseObjectName("planet");
            Assert.AreEqual("planet", name);

        }

    }
}
