using System;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Services;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlMetadataProviderTests : SqlTestBase
    {
        /// <summary>
        /// <code>Do: </code> Fills the table definition with information of the foreign keys
        /// for all the tables based on the entities in runtimeConfig file.
        /// <code>Check: </code> Making sure no exception is thrown if there are no Foriegn Keys.
        /// </summary>
        [TestMethod]
        public async Task CheckNoExceptionForNoForiegnKey()
        {
            IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath = SqlTestHelper.LoadConfig(TestCategory.MYSQL);
            SqlTestHelper.RemoveAllRelationshipBetweenEntities(runtimeConfigPath);
            SetUpSQLMetadataProvider(runtimeConfigPath, TestCategory.MYSQL);
            _sqlMetadataProvider = new MySqlMetadataProvider(runtimeConfigPath, _queryExecutor, _queryBuilder);

            try
            {
                await _sqlMetadataProvider.InitializeAsync();
            }
            catch (Exception ex)
            {
                Assert.Fail("Expected no exception, but got: " + ex.Message);
            }
        }
    }
}
