using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Find
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlFindApiTests : FindApiTestBase
    {
        protected static string DEFAULT_SCHEMA = "dbo";
        private static Dictionary<string, string> _queryMap = new()
        {
            {
                "FindByIdTest",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE id = 2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "FindEmptyTable",
                $"SELECT * FROM { _emptyTableTableName } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindManyStoredProcedureTest",
                $"EXECUTE {_integrationProcedureFindMany_ProcName}"
            },
            {
                "FindOneStoredProcedureTestUsingParameter",
                $"EXECUTE {_integrationProcedureFindOne_ProcName} @id = 1"
            },
            {
                "FindEmptyResultSetWithQueryFilter",
                $"SELECT * FROM { _integrationTableName } " +
                $"WHERE 1 != 1 FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindOnTableWithUniqueCharacters",
                $"SELECT [NoteNum] AS [┬─┬ノ( º _ ºノ)], [DetailAssessmentAndPlanning] AS [始計], " +
                $"[WagingWar] AS [作戰], [StrategicAttack] AS [謀攻] FROM { _integrationUniqueCharactersTable } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindViewAll",
                $"SELECT * FROM { _simple_all_books } " +
                $"WHERE id = 2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "FindViewWithKeyAndMapping",
                $"SELECT id as book_id FROM { _book_view_with_key_and_mapping } FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindViewSelected",
                $"SELECT categoryid, pieceid, categoryName, piecesAvailable FROM {_simple_subset_stocks} " +
                $"WHERE categoryid = 2 AND pieceid = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "FindViewComposite",
                $"SELECT name ,id, publisher_id FROM {_composite_subset_bookPub} " +
                $"WHERE id=2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "FindTestWithFilterQueryOneGeFilterOnView",
                $"SELECT * FROM { _simple_all_books } " +
                $"WHERE id >= 4 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindByIdTestWithQueryStringFieldsOnView",
                $"SELECT[id], [title] FROM { _simple_all_books } " +
                $"WHERE id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER"
            },
            {
                "FindTestWithFilterQueryStringOneEqFilterOnView",
                $"SELECT [categoryid],[pieceid],[categoryName],[piecesAvailable] " +
                $"FROM { _simple_subset_stocks } " +
                $"WHERE [pieceid] = 1 " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneNotFilterOnView",
                $"SELECT [categoryid],[pieceid],[categoryName],[piecesAvailable] " +
                $"FROM { _simple_subset_stocks } " +
                $"WHERE NOT([categoryid] > 1)" +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFilterQueryOneLtFilterOnView",
                $"SELECT[id], [name],[publisher_id] FROM { _composite_subset_bookPub } " +
                $"WHERE id < 5 FOR JSON PATH, INCLUDE_NULL_VALUES"
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
                "FindTestWithPaginationVerifSinglePrimaryKeyInAfter",
                $"SELECT TOP 1 * FROM { _integrationTableName } " +
                $"ORDER BY id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithPaginationVerifMultiplePrimaryKeysInAfter",
                $"SELECT TOP 1 * FROM REVIEWS " +
                $"ORDER BY book_id, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithQueryStringAllFieldsOrderByAsc",
                $"SELECT * FROM { _integrationTableName } " +
                $"ORDER BY title, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithQueryStringAllFieldsMappedEntityOrderByAsc",
                $"SELECT [treeId], [species] AS [fancyName], [region], [height] FROM { _integrationMappingTable } " +
                $"ORDER BY species " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithQueryStringSpaceInNamesOrderByAsc",
                $"SELECT * FROM { _integrationTableHasColumnWithSpace } " +
                $"ORDER BY [ID Number] " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstAndSpacedColumnOrderBy",
                $"SELECT TOP 1 * FROM { _integrationTableHasColumnWithSpace } " +
                $"ORDER BY [Last Name] " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithQueryStringAllFieldsOrderByDesc",
                $"SELECT * FROM { _integrationTableName } " +
                $"ORDER BY publisher_id desc, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstSingleKeyPaginationAndOrderBy",
                $"SELECT TOP 1 * FROM { _integrationTableName } " +
                $"ORDER BY title, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestVerifyMaintainColumnOrderForOrderBy",
                $"SELECT * FROM { _integrationTableName } " +
                $"ORDER BY id desc, publisher_id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestVerifyMaintainColumnOrderForOrderByInReverse",
                $"SELECT * FROM { _integrationTableName } " +
                $"ORDER BY publisher_id, id desc " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstSingleKeyIncludedInOrderByAndPagination",
                $"SELECT TOP 1 * FROM { _integrationTableName } " +
                $"ORDER BY id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },

            {
                "FindTestWithFirstTwoOrderByAndPagination",
                $"SELECT TOP 2 * FROM { _integrationTableName } " +
                $"ORDER BY id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstTwoVerifyAfterFormedCorrectlyWithOrderBy",
                $"SELECT TOP 2 * FROM { _integrationTieBreakTable } " +
                $"ORDER BY birthdate, name, id desc " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstTwoVerifyAfterBreaksTieCorrectlyWithOrderBy",
                $"SELECT TOP 2 * FROM { _integrationTieBreakTable } " +
                $"WHERE ((birthdate > '2001-01-01') OR (birthdate = '2001-01-01' AND name > 'Aniruddh') " +
                $"OR (birthdate = '2001-01-01' AND name = 'Aniruddh' AND id > 125)) " +
                $"ORDER BY birthdate, name, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstMultiKeyIncludeAllInOrderByAndPagination",
                $"SELECT TOP 1 * FROM { _tableWithCompositePrimaryKey } " +
                $"ORDER BY id desc, book_id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstMultiKeyIncludeOneInOrderByAndPagination",
                $"SELECT TOP 1 * FROM { _tableWithCompositePrimaryKey } " +
                $"ORDER BY book_id, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstAndMultiColumnOrderBy",
                $"SELECT TOP 1 * FROM { _integrationTableName } " +
                $"ORDER BY publisher_id desc, title desc " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstAndTiedColumnOrderBy",
                $"SELECT TOP 1 * FROM { _integrationTableName } " +
                $"ORDER BY publisher_id desc, id asc " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithFirstMultiKeyPaginationAndOrderBy",
                $"SELECT TOP 1 * FROM { _tableWithCompositePrimaryKey } " +
                $"WHERE 1=1 " +
                $"ORDER BY content desc, book_id, id " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithMappedFieldsToBeReturned",
                $"SELECT [treeId], [species] AS [Scientific Name], [region] AS [United State's Region], [height] FROM { _integrationMappingTable } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithSingleMappedFieldsToBeReturned",
                $"SELECT [species] AS [Scientific Name] FROM { _integrationMappingTable } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithUnMappedFieldsToBeReturned",
                $"SELECT [treeId] FROM { _integrationMappingTable } " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithDifferentMappedFieldsAndFilter",
                $"SELECT [treeId], [species] AS [fancyName], [region], [height] FROM { _integrationMappingTable } " +
                $"WHERE [species] = 'Tsuga terophylla' " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithDifferentMappedFieldsAndOrderBy",
                $"SELECT [treeId], [species] AS [fancyName], [region], [height] FROM { _integrationMappingTable } " +
                $"ORDER BY [trees].[species] " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithDifferentMappingFirstSingleKeyPaginationAndOrderBy",
                $"SELECT TOP 1 [treeId], [species] AS [fancyName], [region], [height] FROM { _integrationMappingTable } " +
                $"ORDER BY [trees].[species] " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
            },
            {
                "FindTestWithDifferentMappingAfterSingleKeyPaginationAndOrderBy",
                $"SELECT TOP 101 [treeId], [species] AS [fancyName], [region], [height] FROM { _integrationMappingTable } " +
                $"WHERE [trees].[treeId] < 2 " +
                $"ORDER BY [trees].[species], [trees].[treeId] " +
                $"FOR JSON PATH, INCLUDE_NULL_VALUES"
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

        public override string GetDefaultSchema()
        {
            return DEFAULT_SCHEMA;
        }

        /// <summary>
        /// We include a '.' for the Edm Model
        /// schema to allow both MsSql/PostgreSql
        /// and MySql to share code. MySql does not
        /// include a '.' but MsSql does so
        /// we must include here.
        /// </summary>
        /// <returns></returns>
        public override string GetDefaultSchemaForEdmModel()
        {
            return $"{DEFAULT_SCHEMA}.";
        }

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }

        #endregion
    }
}
