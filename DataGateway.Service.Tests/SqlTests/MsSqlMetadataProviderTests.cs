using System.Threading.Tasks;
using Azure.DataGateway.Service.Tests.UnitTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlMetadataProviderTests : SqlMetadataProviderUnitTests
    {
        /// <summary>
        /// <code>Do: </code> Fills the table definition with information of the foreign keys
        /// for all the tables based on the entities in runtimeConfig file.
        /// <code>Check: </code> Making sure no exception is thrown if there are no Foriegn Keys.
        /// </summary>
        [TestMethod]
        public async Task CheckNoExceptionForNoForiegnKeyMsSql()
        {
            await CheckNoExceptionForNoForiegnKey(TestCategory.MSSQL);
        }
    }
}
