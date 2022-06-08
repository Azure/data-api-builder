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
        [DataRow("PostgreSql")]
        [DataRow("MsSql")]
        [DataRow("MySql")]
        public async Task CheckNoExceptionForNoForiegnKey(string testCategory)
        {
            IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath = SqlTestHelper.LoadConfig(testCategory);
            SqlTestHelper.RemoveAllRelationshipBetweenEntities(runtimeConfigPath);
            SetUpSQLMetadataProvider(runtimeConfigPath, testCategory);

            try
            {
                switch (testCategory)
                {
                    case TestCategory.POSTGRESQL:
                        PostgreSqlMetadataProvider postgreSqlMetadataProvider = new(runtimeConfigPath, _queryExecutor, _queryBuilder);
                        await postgreSqlMetadataProvider.InitializeAsync();
                        break;

                    case TestCategory.MSSQL:
                        MsSqlMetadataProvider msSqlMetadataProvider = new(runtimeConfigPath, _queryExecutor, _queryBuilder);
                        await msSqlMetadataProvider.InitializeAsync();
                        break;

                    case TestCategory.MYSQL:
                        MySqlMetadataProvider mySqlMetadataProvider = new(runtimeConfigPath, _queryExecutor, _queryBuilder);
                        await mySqlMetadataProvider.InitializeAsync();
                        break;
                    default:
                        throw new Exception($"{testCategory} not supported.");
                }
            }
            catch (Exception ex)
            {
                Assert.Fail("Expected no exception, but got: " + ex.Message);
            }
        }
    }
}
