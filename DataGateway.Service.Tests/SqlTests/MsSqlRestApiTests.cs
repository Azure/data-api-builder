using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlRestApiTests : RestApiTestBase
    {
        private static Dictionary<string, string> _queryMap = new()
        {
            {
                "FindByIdTest",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id = 2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "FindByIdTestWithQueryStringFields",
                $"SELECT[id], [title] FROM { _integrationTableName } " +
                $"WHERE id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "FindTestWithQueryStringOneField",
                $"SELECT [id] FROM { _integrationTableName } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithQueryStringMultipleFields",
                $"SELECT [id], [title] FROM { _integrationTableName } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithQueryStringAllFields",
                $"SELECT * FROM { _integrationTableName } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryStringOneEqFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryStringValueFirstOneEqFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id = 2 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneGtFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id > 3 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneGeFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id >= 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneLtFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id < 5 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneLeFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id <= 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneNeFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id != 3 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneNotFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE NOT (id < 2) " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneRightNullEqFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE NOT (title IS NULL) " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneLeftNullNeFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE title IS NOT NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneLeftNullRightNullGtFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE NULL > NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryStringSingleAndFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id < 3 AND id > 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryStringSingleOrFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id < 3 OR id > 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryStringMultipleAndFilters",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id < 4 AND id > 1 AND title != 'Awesome book' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryStringMultipleOrFilters",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id = 1 OR id = 2 OR id = 3 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryStringMultipleAndOrFilters",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE (id > 2 AND id < 4) OR title = 'Awesome book' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryStringMultipleNotAndOrFilters",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE (NOT (id < 3) OR id < 4) OR NOT (title = 'Awesome book') " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithPrimaryKeyContainingForeignKey",
                $"SELECT [id], [content] FROM reviews " +
                $"WHERE id = 567 AND book_id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "FindTestWithFirstSingleKeyPagination",
                $"SELECT TOP 1 * FROM { _integrationTableName } " +
                $"ORDER BY id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstMultiKeyPagination",
                $"SELECT TOP 1 * FROM REVIEWS " +
                $"WHERE 1=1 " +
                $"ORDER BY book_id, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithAfterSingleKeyPagination",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id > 7 " +
                $"ORDER BY id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithAfterMultiKeyPagination",
                $"SELECT * FROM REVIEWS " +
                "WHERE book_id > 1 OR (book_id = 1 AND id > 567) " +
                $"ORDER BY book_id, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
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
                "InsertOneInCompositeKeyTableTest",
                // This query is the query for the result we get back from the database
                // after the insert operation. Not the query that we generate to perform
                // the insertion.
                $"SELECT [id], [content], [book_id] FROM { _tableWithCompositePrimaryKey } " +
                $"WHERE [id] = { STARTING_ID_FOR_TEST_INSERTS } AND [book_id] = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "DeleteOneTest",
                // This query is used to confirm that the item no longer exists, not the
                // actual delete query.
                $"SELECT [id] FROM { _integrationTableName } " +
                $"WHERE id = 5 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Update_Test",
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE id = 7 AND [title] = 'The Hobbit Returns to The Shire' " +
                $"AND [publisher_id] = 1234" +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Insert_Test",
                $"SELECT [id], [title], [issueNumber] FROM { _integration_NonAutoGenPK_TableName } " +
                $"WHERE id = { STARTING_ID_FOR_TEST_INSERTS } AND [title] = 'Batman Returns' " +
                $"AND [issueNumber] = 1234" +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Insert_Nullable_Test",
                $"SELECT [id], [title], [issueNumber] FROM { _integration_NonAutoGenPK_TableName } " +
                $"WHERE id = { STARTING_ID_FOR_TEST_INSERTS + 1 } AND [title] = 'Times' " +
                $"AND [issueNumber] IS NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PutOne_Insert_PKAutoGen_Test",
                $"INSERT INTO { _integrationTableName } " +
                $"(id, title, publisher_id)" +
                $"VALUES (1000,'The Hobbit Returns to The Shire',1234)"
            },
            {
                "PutOne_Insert_AutoGenNonPK_Test",
                $"SELECT [id], [title], [volume] FROM { _integration_AutoGenNonPK_TableName } " +
                $"WHERE id = { STARTING_ID_FOR_TEST_INSERTS } AND [title] = 'Star Trek' " +
                $"AND [volume] IS NOT NULL " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_NonAutoGenPK_Test",
                $"SELECT [id], [title], [issueNumber] FROM { _integration_NonAutoGenPK_TableName } " +
                $"WHERE id = 2 AND [title] = 'Batman Begins' " +
                $"AND [issueNumber] = 1234 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Update_Test",
                $"SELECT [id], [title], [publisher_id] FROM { _integrationTableName } " +
                $"WHERE id = 8 AND [title] = 'Heart of Darkness' " +
                $"AND [publisher_id] = 2324 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "PatchOne_Insert_PKAutoGen_Test",
                $"INSERT INTO { _integrationTableName } " +
                $"(id, title, publisher_id)" +
                $"VALUES (1000,'The Hobbit Returns to The Shire',1234)"
            }
        };

        #region Test Fixture Setup

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task InitializeTestFixture(TestContext context)
        {
            await InitializeTestFixture(context, TestCategory.MSSQL);

            // Setup REST Components
            _restService = new RestService(_queryEngine,
                _mutationEngine,
                _graphQLMetadataProvider,
                _httpContextAccessor.Object,
                _authorizationService.Object);
            _restController = new RestController(_restService);
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

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }
    }
}
