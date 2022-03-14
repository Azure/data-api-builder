using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlMetadataProviderTests : SqlMetadataProviderTests
    {
        [ClassInitialize]
        public static async Task InitializeTestFixture(TestContext context)
        {
            await InitializeTestFixture(context, TestCategory.MYSQL);
        }

        [TestMethod]
        [Ignore]
        public override async Task TestDerivedDatabaseSchemaIsValid()
        {
            await base.TestDerivedDatabaseSchemaIsValid();
        }
    }
}
