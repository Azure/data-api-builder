using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Services;
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
            }
        };
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
            // Setup REST Components
            _restService = new RestService(_queryEngine,
                _mutationEngine,
                _sqlMetadataProvider,
                _httpContextAccessor.Object,
                _authorizationService.Object,
                _authorizationResolver,
                _runtimeConfigProvider);
            _restController = new RestController(_restService,
                                                 _restControllerLogger);
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

        #region RestApiTestBase Overrides

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }

        #endregion
    }
}
