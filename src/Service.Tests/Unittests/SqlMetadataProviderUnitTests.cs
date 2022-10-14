using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Services;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Units testing for our connection string parser
    /// to retreive schema.
    /// </summary>
    [TestClass]
    public class SqlMetadataProviderUnitTests : SqlTestBase
    {
        /// <summary>
        /// Only for PostgreSql connection strings.
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
            PostgreSqlMetadataProvider.TryGetSchemaFromConnectionString(connectionString, out string actual);
            Assert.AreEqual(expected, actual);
        }

        /// <summary>
        /// <code>Do: </code> Fills the table definition with information of the foreign keys
        /// for all the tables based on the entities relationship.
        /// <code>Check: </code> Making sure no exception is thrown if there are no Foreign Keys.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.POSTGRESQL)]
        public async Task CheckNoExceptionForNoForeignKey()
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            _runtimeConfig = SqlTestHelper.SetupRuntimeConfig(DatabaseEngine);
            _runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(_runtimeConfig);
            SqlTestHelper.RemoveAllRelationshipBetweenEntities(_runtimeConfig);
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
        [DataRow(";;;;;fooBarBAZ", DatabaseType.mssql)]
        [DataRow(";;;;;fooBarBAZ", DatabaseType.mysql)]
        [DataRow(";;;;;fooBarBAZ", DatabaseType.postgresql)]
        [DataRow("!&^%*&$$%#$%@$%#@()", DatabaseType.mssql)]
        [DataRow("!&^%*&$$%#$%@$%#@()", DatabaseType.mysql)]
        [DataRow("!&^%*&$$%#$%@$%#@()", DatabaseType.postgresql)]
        [DataRow("Server=<>;Databases=<>;Persist Security Info=False;Integrated Security=True;MultipleActiveResultSets=False;Connection Timeout=5;", DatabaseType.mssql)]
        [DataRow("Server=<>;Databases=<>;Persist Security Info=False;Integrated Security=True;MultipleActiveResultSets=False;Connection Timeout=5;", DatabaseType.mysql)]
        [DataRow("Server=<>;Databases=<>;Persist Security Info=False;Integrated Security=True;MultipleActiveResultSets=False;Connection Timeout=5;", DatabaseType.postgresql)]
        [DataRow("Servers=<>;Database=<>;Persist Security Info=False;Integrated Security=True;MultipleActiveResultSets=False;Connection Timeout=5;", DatabaseType.mssql)]
        [DataRow("Servers=<>;Database=<>;Persist Security Info=False;Integrated Security=True;MultipleActiveResultSets=False;Connection Timeout=5;", DatabaseType.mysql)]
        [DataRow("Servers=<>;Database=<>;Persist Security Info=False;Integrated Security=True;MultipleActiveResultSets=False;Connection Timeout=5;", DatabaseType.postgresql)]
        [DataRow("DO NOT EDIT, look at CONTRIBUTING.md on how to run tests", DatabaseType.mssql)]
        [DataRow("DO NOT EDIT, look at CONTRIBUTING.md on how to run tests", DatabaseType.postgresql)]
        [DataRow("DO NOT EDIT, look at CONTRIBUTING.md on how to run tests", DatabaseType.mysql)]
        [DataRow("", DatabaseType.mssql)]
        [DataRow("", DatabaseType.postgresql)]
        [DataRow("", DatabaseType.mysql)]
        public async Task CheckExceptionForBadConnectionString(string connectionString, DatabaseType db)
        {
            _runtimeConfig = SqlTestHelper.SetupRuntimeConfig(db.ToString());
            _runtimeConfig.ConnectionString = connectionString;
            _sqlMetadataLogger = new Mock<ILogger<ISqlMetadataProvider>>().Object;
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
            catch (DataApiBuilderException ex)
            {
                // use contains to correctly cover db/user unique error messaging
                Assert.IsTrue(ex.Message.Contains(DataApiBuilderException.CONNECTION_STRING_ERROR_MESSAGE));
                Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
                Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, ex.SubStatusCode);
            }
        }

        /// <summary>
        /// <code>Do: </code> Load runtimeConfig and set up the source fields for the entities.
        /// <code>Check: </code>  Verifies that source object is correctly parsed.
        /// </summary>
        [TestMethod, TestCategory(TestCategory.MSSQL)]
        public async Task CheckCorrectParsingForStoredProcedure()
        {
            DatabaseEngine = TestCategory.MSSQL;
            _runtimeConfig = SqlTestHelper.SetupRuntimeConfig(DatabaseEngine);
            _runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(_runtimeConfig);
            SetUpSQLMetadataProvider();

            await _sqlMetadataProvider.InitializeAsync();

            Entity entity = _runtimeConfig.Entities["GetBooks"];
            Assert.AreEqual("get_books", entity.SourceName);
            Assert.AreEqual(SourceType.StoredProcedure, entity.ObjectType);
        }

        /// <summary>
        /// Test to verify that all the base tables which have a column in select clause
        /// for the view are present in the base table definitions for the view's
        /// definition.
        /// </summary>
        /// <param name="entityName">Name of the view.</param>
        /// <param name="expectedBaseTableCount">Expected number of base tables.</param>
        /// <param name="baseTableNames">Names of the base tables.</param>
        /// <returns></returns>
        [DataTestMethod]
        [DataRow("books_view_all", 1, new string[] { "dbo.books" })]
        [DataRow("stocks_view_selected", 1, new string[] { "dbo.stocks" })]
        [DataRow("books_publishers_view_composite", 2, new string[] { "dbo.books", "dbo.publishers" })]
        public async Task CheckPopulatedBaseTableDefinitionsForViewAsync(
            string entityName,
            int expectedBaseTableCount,
            string[] baseTableNames)
        {
            DatabaseEngine = TestCategory.MSSQL;
            _runtimeConfig = SqlTestHelper.SetupRuntimeConfig(DatabaseEngine);
            _runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(_runtimeConfig);
            SetUpSQLMetadataProvider();

            await _sqlMetadataProvider.InitializeAsync();
            ViewDefinition viewDefinition = (ViewDefinition)_sqlMetadataProvider.GetSourceDefinition(entityName);

            // Assert that there are expected number of base tables in view's definition.
            Assert.AreEqual(expectedBaseTableCount, viewDefinition.BaseTableDefinitions.Count);
            foreach (string baseTableName in baseTableNames)
            {
                // Assert that the base table's in the BaseTableDefinitions are the ones
                // that are expected.
                Assert.IsTrue(viewDefinition.BaseTableDefinitions.ContainsKey(baseTableName));
            }
        }

        /// <summary>
        /// Test to verify that the mappings generated for columns in view to corresponding
        /// source column and table are correct.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task CheckPopulatedColToBaseTableDetailsForViewAsync()
        {
            DatabaseEngine = TestCategory.MSSQL;
            _runtimeConfig = SqlTestHelper.SetupRuntimeConfig(DatabaseEngine);
            _runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(_runtimeConfig);
            SetUpSQLMetadataProvider();

            await _sqlMetadataProvider.InitializeAsync();
            ViewDefinition viewDefinition = (ViewDefinition)_sqlMetadataProvider.GetSourceDefinition("books_publishers_view_composite");

            // Create the expected column mapping for view columns to source column and table.
            Dictionary<string, Tuple<string, string>> expectedColToBaseTableDetails = new();
            expectedColToBaseTableDetails.Add("publisher_id", new Tuple<string, string>("id", "dbo.publishers"));
            expectedColToBaseTableDetails.Add("name", new Tuple<string, string>("name", "dbo.publishers"));
            expectedColToBaseTableDetails.Add("id", new Tuple<string, string>("id", "dbo.books"));

            foreach ((string colName, Tuple<string, string> expectedBaseTableDetail) in expectedColToBaseTableDetails)
            {
                // Assert that there is a mapping for every column in view.
                Assert.IsTrue(viewDefinition.ColToBaseTableDetails.
                   TryGetValue(colName, out Tuple<string, string> baseTableDetails));

                // Assert that the source column name is as expected.
                Assert.IsTrue(expectedBaseTableDetail.Item1.Equals(baseTableDetails.Item1));

                // Assert that the source table name is as expected.
                Assert.IsTrue(expectedBaseTableDetail.Item2.Equals(baseTableDetails.Item2));
            }
        }
    }
}
