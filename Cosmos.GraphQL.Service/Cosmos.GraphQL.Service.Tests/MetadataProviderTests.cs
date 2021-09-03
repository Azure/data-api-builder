using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cosmos.GraphQL.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Cosmos.GraphQL.Service.Tests
{
    [TestClass]
    public class MetadataProviderTests
    {
        IMetadataStoreProvider _fileProvider;

        public MetadataProviderTests()
        {
            _fileProvider = new FileMetadataStoreProvider();
        }

        [TestMethod]
        public void TestGetSchema()
        {
            Assert.IsNotNull(_fileProvider.GetGraphQLSchema());
        }

        [TestMethod]
        public void TestGetResolver()
        {
            Assert.IsNotNull(_fileProvider.GetQueryResolver("authorById"));
        }
    }
}
