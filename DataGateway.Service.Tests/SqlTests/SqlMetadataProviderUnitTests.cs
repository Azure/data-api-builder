using System;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Services;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    /// <summary>
    /// Units testing for our connection string parser
    /// to retreive schema.
    /// </summary>
    [TestClass]
    public class SqlMetadataProviderUnitTests : SqlTestBase
    {
        /// <summary>
        /// Verify we parse the connection string for the
        /// schema correctly when it is of various relevant
        /// formats.
        /// </summary>
        [DataTestMethod]
        [DataRow("", "Host=localhost;Database=graphql;SearchPath=\"\"")]
        [DataRow("", "Host=localhost;Database=graphql;SearchPath=")]
        [DataRow("foobar", "Host=localhost;Database=graphql;SearchPath=foobar")]
        [DataRow("foobar", "Host=localhost;Database=graphql;SearchPath=\"foobar\"")]
        [DataRow("baz", "SearchPath=\"baz\";Host=localhost;Database=graphql")]
        [DataRow("baz", "SearchPath=baz;Host=localhost;Database=graphql")]
        [DataRow("", "Host=localhost;Database=graphql")]
        [DataRow("", "SearchPath=;Host=localhost;Database=graphql")]
        [DataRow("", "SearchPath=\"\";Host=localhost;Database=graphql")]
        public void CheckConnectionStringParsingTest(string expected, string connectionString)
        {
            MsSqlMetadataProvider.TryGetSchemaFromConnectionString(out string actual, connectionString);
            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// <code>Do: </code> Fills the table definition with information of the foreign keys
        /// for all the tables based on the entities in runtimeConfig file.
        /// <code>Check: </code> Making sure no exception is thrown if there are no Foriegn Keys.
        /// <code>Note: </code> This test is independent of DB, so any DB(POSTGRES,MSSQL,MYSQL) can be used.
        /// </summary>
        [DataTestMethod]
        [TestCategory(TestCategory.POSTGRESQL), DataRow("PostgreSql")]
        [TestCategory(TestCategory.MSSQL), DataRow("MsSql")]
        [TestCategory(TestCategory.MYSQL), DataRow("MySql")]
        public async Task CheckNoExceptionForNoForiegnKey(string testCategory)
        {
            IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath = SqlTestHelper.LoadConfig(testCategory);
            SqlTestHelper.RemoveAllRelationshipBetweenEntities(runtimeConfigPath);
            SetUpSQLMetadataProvider(runtimeConfigPath, testCategory);

            switch (testCategory)
            {
                case TestCategory.POSTGRESQL:
                    _sqlMetadataProvider = new PostgreSqlMetadataProvider(runtimeConfigPath, _queryExecutor, _queryBuilder);
                    break;

                case TestCategory.MSSQL:
                    _sqlMetadataProvider = new MsSqlMetadataProvider(runtimeConfigPath, _queryExecutor, _queryBuilder);
                    break;

                case TestCategory.MYSQL:
                    _sqlMetadataProvider = new MySqlMetadataProvider(runtimeConfigPath, _queryExecutor, _queryBuilder);
                    break;
                default:
                    throw new Exception($"{testCategory} not supported.");
            }

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
