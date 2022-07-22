using System.Net;
using System.Threading.Tasks;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Services;
using Azure.DataGateway.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.UnitTests
{
    /// <summary>
    /// Units testing for our connection string parser
    /// to retreive schema.
    /// </summary>
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class SqlMetadataProviderUnitTests : SqlTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static void Setup(TestContext context)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
        }

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
            PostgreSqlMetadataProvider.TryGetSchemaFromConnectionString(out string actual, connectionString);
            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// <code>Do: </code> Fills the table definition with information of the foreign keys
        /// for all the tables based on the entities relationship.
        /// <code>Check: </code> Making sure no exception is thrown if there are no Foriegn Keys.
        /// </summary>
        [TestMethod]
        public async Task CheckNoExceptionForNoForeignKey()
        {
            SqlTestHelper.SetupRuntimeConfig(DatabaseEngine, out _runtimeConfig);
            SqlTestHelper.RemoveAllRelationshipBetweenEntities(_runtimeConfig);
            _runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(_runtimeConfig);
            SetUpSQLMetadataProvider();
            await ResetDbStateAsync();
            await _sqlMetadataProvider.InitializeAsync();
        }

        /// <summary>
        /// <code>Do: </code> Load runtimeConfig and set connection string and db type
        /// according to data row.
        /// <code>Check: </code>  Verify malformed connection string throws correct exception.
        /// </summary>
        [DataTestMethod]
        [DataRow("DO NOT EDIT, look at CONTRIBUTING.md on how to run tests",
            "The Connection String should be provided.", DatabaseType.mssql)]
        [DataRow("DO NOT EDIT, look at CONTRIBUTING.md on how to run tests",
            "The Connection String should be provided.", DatabaseType.postgresql)]
        [DataRow("DO NOT EDIT, look at CONTRIBUTING.md on how to run tests",
            "The Connection String should be provided.", DatabaseType.mysql)]
        [DataRow("", "Cannot obtain Schema for entity", DatabaseType.mssql)]
        [DataRow("", "Cannot obtain Schema for entity", DatabaseType.postgresql)]
        [DataRow("", "Cannot obtain Schema for entity", DatabaseType.mysql)]
        public async Task CheckExceptionForBadConnectionString(string connectionString, string message, DatabaseType db)
        {
            SqlTestHelper.SetupRuntimeConfig(DatabaseEngine, out _runtimeConfig);
            _runtimeConfig.ConnectionString = connectionString;
            _runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(_runtimeConfig);
            switch (db)
            {
                case DatabaseType.mssql:
                    _sqlMetadataProvider =
                       new MsSqlMetadataProvider(_runtimeConfigProvider,
                           _queryExecutor,
                           _queryBuilder,
                           _sqlMetadataLogger);
                    break;
                case DatabaseType.mysql:
                    _sqlMetadataProvider =
                       new MySqlMetadataProvider(_runtimeConfigProvider,
                           _queryExecutor,
                           _queryBuilder,
                           _sqlMetadataLogger);
                    break;
                case DatabaseType.postgresql:
                    _sqlMetadataProvider =
                       new PostgreSqlMetadataProvider(_runtimeConfigProvider,
                           _queryExecutor,
                           _queryBuilder,
                           _sqlMetadataLogger);
                    break;
            }

            try
            {
                await _sqlMetadataProvider.InitializeAsync();
            }
            catch (DataGatewayException ex)
            {
                // use contains to correctly cover db/user unique error messaging
                Assert.IsTrue(ex.Message.Contains(message));
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataGatewayException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
            }
        }
    }
}
