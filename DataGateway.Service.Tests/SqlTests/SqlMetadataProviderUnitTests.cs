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
    public class SqlMetadataProviderUnitTests : GraphQLMutationTestBase
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
        [TestMethod]
        public async Task CheckNoExceptionForNoForiegnKey()
        {
            // POSTGRESQL
            string customRuntimeTestConfig = "hawaii-config.PostgreSql.json";
            IOptionsMonitor<RuntimeConfigPath> runtimeConfigPath = SqlTestHelper.LoadCustomConfig(customRuntimeTestConfig, TestCategory.POSTGRESQL);
            SqlTestHelper.RemoveAllRelationshipBetweenEntities(runtimeConfigPath);
            SetUpSQLMetadataProvider(runtimeConfigPath, TestCategory.POSTGRESQL);

            PostgreSqlMetadataProvider postgreSqlMetadataProvider = new(runtimeConfigPath, _queryExecutor, _queryBuilder);
            Console.WriteLine("Custom Config file for Postgres set successfully.");

            try
            {
                await postgreSqlMetadataProvider.InitializeAsync();
            }
            catch (Exception ex)
            {
                Assert.Fail("Expected no exception, but got: " + ex.Message);
            }

            // MSSQL
            customRuntimeTestConfig = "hawaii-config.MsSql.json";
            runtimeConfigPath = SqlTestHelper.LoadCustomConfig(customRuntimeTestConfig, TestCategory.MSSQL);
            SqlTestHelper.RemoveAllRelationshipBetweenEntities(runtimeConfigPath);
            SetUpSQLMetadataProvider(runtimeConfigPath, TestCategory.MSSQL);

            MsSqlMetadataProvider msSqlMetadataProvider = new(runtimeConfigPath, _queryExecutor, _queryBuilder);
            Console.WriteLine("Custom Config file for MsSql set successfully.");

            try
            {
                await msSqlMetadataProvider.InitializeAsync();
            }
            catch (Exception ex)
            {
                Assert.Fail("Expected no exception, but got: " + ex.Message);
            }

            // MYSQL
            customRuntimeTestConfig = "hawaii-config.MySql.json";
            runtimeConfigPath = SqlTestHelper.LoadCustomConfig(customRuntimeTestConfig, TestCategory.MYSQL);
            SqlTestHelper.RemoveAllRelationshipBetweenEntities(runtimeConfigPath);
            SetUpSQLMetadataProvider(runtimeConfigPath, TestCategory.MYSQL);

            MySqlMetadataProvider mySqlMetadataProvider = new(runtimeConfigPath, _queryExecutor, _queryBuilder);
            Console.WriteLine("Custom Config file set for MySql successfully.");

            try
            {
                await mySqlMetadataProvider.InitializeAsync();
            }
            catch (Exception ex)
            {
                Assert.Fail("Expected no exception, but got: " + ex.Message);
            }
        }
    }
}
