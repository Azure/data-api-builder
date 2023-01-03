using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Insert
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlInsertApiTests : InsertApiTestBase
    {
        private static Dictionary<string, string> _queryMap = new()
        {
            {
                "InsertOneTest",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } AND [title] = 'My New Book' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneInBooksViewAll",
                $"SELECT [id], [title], [publisher_id] FROM { _simple_all_books } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } AND [title] = 'My New Book' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneInStocksViewSelected",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable] " +
                $"FROM { _simple_subset_stocks } WHERE [categoryid] = 4 " +
                $"AND [pieceid] = 1 AND [categoryName] = 'SciFi' AND [piecesAvailable] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneUniqueCharactersTest",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [NoteNum] AS [┬─┬ノ( º _ ºノ)], [DetailAssessmentAndPlanning] AS [始計], " +
                $"[WagingWar] AS [作戰], [StrategicAttack] AS [謀攻] FROM { _integrationUniqueCharactersTable } " +
                $"WHERE [NoteNum] = 2 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "InsertOneWithMappingTest",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [treeId], [species] AS [Scientific Name], [region] AS " +
                $"[United State's Region], [height] FROM { _integrationMappingTable } " +
                $"WHERE [treeId] = 3 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneInCompositeNonAutoGenPKTest",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [categoryid],[pieceid],[categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 5 AND [pieceid] = 2 AND [categoryName] = 'FairyTales' " +
                $"AND [piecesAvailable] = 0 AND [piecesRequired] = 0 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneInCompositeKeyTableTest",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [id], [content], [book_id] FROM { _tableWithCompositePrimaryKey } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } AND [book_id] = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneInDefaultTestTable",
                $"SELECT [id], [book_id], [content] FROM { _tableWithCompositePrimaryKey } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS + 1} AND [book_id] = 2 AND [content] = 'Its a classic' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneWithNullFieldValue",
                $"SELECT [categoryid], [pieceid], [categoryName],[piecesAvailable]," +
                $"[piecesRequired] FROM { _Composite_NonAutoGenPK_TableName } " +
                $"WHERE [categoryid] = 3 AND [pieceid] = 1 AND [categoryName] = 'SciFi' " +
                $"AND [piecesAvailable] is NULL AND [piecesRequired] = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertSqlInjectionQuery1",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } " +
                $"AND [title] = ' UNION SELECT * FROM books/*' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertSqlInjectionQuery2",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } " +
                $"AND [title] = '; SELECT * FROM information_schema.tables/*' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertSqlInjectionQuery3",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } " +
                $"AND [title] = 'value; SELECT * FROM v$version--' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertSqlInjectionQuery4",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } " +
                $"AND [title] = 'id; DROP TABLE books;' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertSqlInjectionQuery5",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } " +
                $"AND [title] = ' '' UNION SELECT * FROM books/*' " +
                $"AND [publisher_id] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "InsertOneWithStoredProcedureTest",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT table0.[id], table0.[title], table0.[publisher_id] FROM books AS table0 " +
                $"JOIN (SELECT id FROM publishers WHERE name = 'The First Publisher') AS table1 " +
                $"ON table0.[publisher_id] = table1.[id] " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            }
        };

        #region Overriden tests
        /// <inheritdoc/>
        [TestMethod]
        public override async Task InsertOneTestViolatingForeignKeyConstraint()
        {
            string requestBody = @"
            {
                ""title"": ""My New Book"",
                ""publisher_id"": 12345
            }";

            string expectedErrorMessage = "The INSERT statement conflicted with the FOREIGN KEY constraint" +
                    $" \"book_publisher_fk\". The conflict occurred in database \"{DatabaseName}\", table \"{_defaultSchemaName}.publishers\"" +
                    ", column 'id'.";

            await SetupAndRunRestApiTest(
                primaryKeyRoute: string.Empty,
                queryString: string.Empty,
                entityNameOrPath: _integrationEntityName,
                sqlQuery: string.Empty,
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                exceptionExpected: true,
                expectedErrorMessage: expectedErrorMessage,
                expectedStatusCode: HttpStatusCode.BadRequest,
                expectedSubStatusCode: DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed.ToString()
            );
        }
        #endregion

        #region Test Fixture Setup

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture(context);
        }

        /// <summary>
        /// Runs after every test to reset the database state
        /// </summary>
        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        #endregion

        [TestMethod]
        public async Task InsertOneInViewBadRequestTest()
        {
            _ = $"View or function '{_defaultSchemaName}.{_composite_subset_bookPub}' is not updatable " +
                                          $"because the modification affects multiple base tables.";
        }

        /// <summary>
        /// Tests the InsertOne functionality with a REST POST request
        /// using stored procedure.
        /// The below request tries to insert a book for a given publisher
        /// and returns all the books under that publisher.
        /// </summary>
        [TestMethod]
        public async Task InsertOneWithStoredProcedureTest()
        {
            string requestBody = @"
            {
                ""title"": ""Happy New Year"",
                ""publisher_name"": ""The First Publisher""
            }";

            string expectedLocationHeader = _integrationProcedureInsertOneAndDisplay_EntityName;
            await SetupAndRunRestApiTest(
                primaryKeyRoute: null,
                queryString: null,
                entityNameOrPath: _integrationProcedureInsertOneAndDisplay_EntityName,
                sqlQuery: GetQuery(nameof(InsertOneWithStoredProcedureTest)),
                operationType: Config.Operation.Insert,
                requestBody: requestBody,
                expectedStatusCode: HttpStatusCode.Created,
                expectedLocationHeader: expectedLocationHeader
            );
        }

        #region RestApiTestBase Overrides

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }

        #endregion
    }
}
