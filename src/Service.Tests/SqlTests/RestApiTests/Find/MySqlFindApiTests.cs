using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Find
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlFindApiTests : FindApiTestBase
    {
        protected static string DEFAULT_SCHEMA = string.Empty;
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "FindByIdTest",
                @"
                  SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id = 2
                      ORDER BY id
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindEmptyTable",
                @"
                    SELECT JSON_OBJECT('id', id) AS data
                    FROM (
                        SELECT *
                        FROM " + _emptyTableTableName + @"
                    ) AS subq"
            },
            {
                "FindEmptyResultSetWithQueryFilter",
                @"
                    SELECT JSON_OBJECT('id', id) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE 1 != 1
                    ) AS subq"
            },
            {
                "FindOnTableWithUniqueCharacters",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('┬─┬ノ( º _ ºノ)', NoteNum,
                  '始計', DetailAssessmentAndPlanning, '作戰', WagingWar,
                  '謀攻', StrategicAttack)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationUniqueCharactersTable + @"
                  ) AS subq"
            },
            {
                "FindViewAll",
                @"
                  SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
                  FROM (
                      SELECT *
                      FROM " + _simple_all_books + @"
                      WHERE id = 2
                      ORDER BY id
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindViewSelected",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable
                        FROM " + _simple_subset_stocks + @"
                        WHERE categoryid = 2 AND pieceid = 1
                    ) AS subq
                "
            },
            {
                "FindViewComposite",
                @"
                  SELECT JSON_OBJECT('id', id, 'title', title, 'name', name, 'pub_id', pub_id) AS data
                  FROM (
                      SELECT *
                      FROM " + _composite_subset_bookPub + @"
                      WHERE id = 2 AND title = 'Also Awesome book' AND 
                      pub_id = 1234 AND name = 'Big Company' 
                      ORDER BY id
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneGeFilterOnView",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _simple_all_books + @"
                      WHERE id >= 4
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindByIdTestWithQueryStringFieldsOnView",
                @"
                  SELECT JSON_OBJECT('id', id, 'title', title) AS data
                  FROM (
                      SELECT *
                      FROM " + _simple_all_books + @"
                      WHERE id = 1
                      ORDER BY id
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryStringOneEqFilterOnView",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid,
                                        'categoryName', categoryName, 'piecesAvailable',piecesAvailable)) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable
                        FROM " + _simple_subset_stocks + @"
                        WHERE pieceid = 1
                    ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneNotFilterOnView",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid,
                                        'categoryName', categoryName, 'piecesAvailable',piecesAvailable)) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable
                        FROM " + _simple_subset_stocks + @"
                        WHERE NOT(categoryid > 1)
                    ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneLtFilterOnView",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'pub_id', pub_id, 'name', name)) AS data
                  FROM (
                      SELECT *
                      FROM " + _composite_subset_bookPub + @"
                      WHERE id < 5
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringOneField",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`)) AS `data`
                    FROM (
                        SELECT `table0`.`id` AS `id`
                        FROM `" + _integrationTableName + @"` AS `table0`
                        ORDER BY `table0`.`id`
                        LIMIT 100
                        ) AS `subq1`"
            },
            {
                "FindTestWithQueryStringAllFields",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY id
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindByIdTestWithQueryStringFields",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title) AS data
                    FROM (
                        SELECT id, title
                        FROM " + _integrationTableName + @"
                        WHERE id = 1
                        ORDER BY id
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringOneEqFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id = 1
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringValueFirstOneEqFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id = 2
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneGtFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id > 3
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneGeFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id >= 4
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneLtFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id < 5
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneLeFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id <= 4
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneNeFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id != 3
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneNotFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE not (id < 2)
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneRightNullEqFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE NOT (title IS NULL)
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryOneLeftNullNeFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE title IS NOT NULL
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringSingleAndFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id < 3 AND id > 1
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringSingleOrFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id < 3 OR id > 4
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringMultipleAndFilters",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id < 4 AND id > 1 AND title != 'Awesome book'
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringMultipleOrFilters",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id = 1 OR id = 2 OR id = 3
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringMultipleAndOrFilters",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE (id > 2 AND id < 4) OR (title = 'Awesome book')
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringMultipleNotAndOrFilters",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE (NOT (id < 3) OR (id < 4) or NOT (title = 'Awesome book'))
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringBoolResultFilter",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id = (publisher_id > 1)
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithQueryStringMultipleFields",
                @"
                    SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title)) AS data
                    FROM (
                        SELECT id, title
                        FROM " + _integrationTableName + @"
                        ORDER BY id
                        LIMIT 100
                    ) AS subq
                "
            },
            {
                "FindTestWithPrimaryKeyContainingForeignKey",
                @"
                    SELECT JSON_OBJECT('id', id, 'content', content) AS data
                    FROM (
                        SELECT id, content
                        FROM reviews" + @"
                        WHERE id = 567 AND book_id = 1
                        ORDER BY id
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithFirstSingleKeyPagination",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY id
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithFirstMultiKeyPagination",
                @"
                  SELECT JSON_OBJECT('id', id, 'content', content, 'book_id', book_id) AS data
                  FROM (
                      SELECT *
                      FROM " + _tableWithCompositePrimaryKey + @"
                      ORDER BY book_id, id
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithAfterSingleKeyPagination",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id > 7
                      ORDER BY id
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithAfterMultiKeyPagination",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'content', content, 'book_id', book_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _tableWithCompositePrimaryKey + @"
                      WHERE book_id > 1 OR (book_id = 1 AND id > 567)
                      ORDER BY book_id, id
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithPaginationVerifSinglePrimaryKeyInAfter",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY id
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithPaginationVerifMultiplePrimaryKeysInAfter",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'content', content, 'book_id', book_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _tableWithCompositePrimaryKey + @"
                      ORDER BY book_id, id
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringAllFieldsOrderByAsc",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY title, id
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringAllFieldsMappedEntityOrderByAsc",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId, 'fancyName', species, 'region', region, 'height', height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      ORDER BY species
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringSpaceInNamesOrderByAsc",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('ID Number', `ID Number`, 'First Name', `First Name`, 'Last Name', `Last Name`)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableHasColumnWithSpace + @"
                      ORDER BY `ID Number`
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithFirstAndSpacedColumnOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('ID Number', `ID Number`, 'First Name', `First Name`, 'Last Name', `Last Name`)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableHasColumnWithSpace + @"
                      ORDER BY `Last Name`
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringAllFieldsOrderByDesc",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY publisher_id desc, id
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithFirstSingleKeyPaginationAndOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY title, id
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestVerifyMaintainColumnOrderForOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT id, title, publisher_id
                      FROM " + _integrationTableName + @"
                      ORDER BY id desc, publisher_id
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestVerifyMaintainColumnOrderForOrderByInReverse",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT id, title, publisher_id
                      FROM " + _integrationTableName + @"
                      ORDER BY publisher_id, id desc
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithFirstSingleKeyIncludedInOrderByAndPagination",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY id
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithFirstTwoOrderByAndPagination",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY id
                      LIMIT 2
                  ) AS subq"
            },
            {
                "FindTestWithFirstTwoVerifyAfterFormedCorrectlyWithOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'name', name, 'birthdate', birthdate)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTieBreakTable + @"
                      ORDER BY birthdate, name, id desc
                      LIMIT 2
                  ) AS subq"
            },
            {
                "FindTestWithFirstTwoVerifyAfterBreaksTieCorrectlyWithOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'name', name, 'birthdate', birthdate)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTieBreakTable + @"
                      WHERE ((birthdate > '2001-01-01') OR(birthdate = '2001-01-01' AND name > 'Aniruddh') OR
                      (birthdate = '2001-01-01' AND name = 'Aniruddh' AND id > 125))
                      ORDER BY birthdate, name, id
                      LIMIT 2
                  ) AS subq"
            },
            {
                "FindTestWithFirstMultiKeyIncludeAllInOrderByAndPagination",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'content', content, 'book_id', book_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _tableWithCompositePrimaryKey + @"
                      ORDER BY id desc, book_id
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithFirstMultiKeyIncludeOneInOrderByAndPagination",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'content', content, 'book_id', book_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _tableWithCompositePrimaryKey + @"
                      ORDER BY book_id, id
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithFirstAndMultiColumnOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY publisher_id desc, title desc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithFirstAndTiedColumnOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY publisher_id desc, id asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithFirstMultiKeyPaginationAndOrderBy",
                @"
                  SELECT JSON_OBJECT('id', id, 'content', content, 'book_id', book_id) AS data
                  FROM (
                      SELECT *
                      FROM " + _tableWithCompositePrimaryKey + @"
                      ORDER BY content desc, book_id, id
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithMappedFieldsToBeReturned",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId, 'Scientific Name', species, 'United State\'s Region', region, 'height', height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                  ) AS subq"
            },
            {
                "FindTestWithSingleMappedFieldsToBeReturned",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('Scientific Name', species)) AS data
                  FROM (
                      SELECT species
                      FROM " + _integrationMappingTable + @"
                  ) AS subq"
            },
            {
                "FindTestWithUnMappedFieldsToBeReturned",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId)) AS data
                  FROM (
                      SELECT treeId
                      FROM " + _integrationMappingTable + @"
                  ) AS subq"
            },
            {
                "FindTestWithDifferentMappedFieldsAndFilter",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId, 'fancyName', species, 'region', region, 'height', height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      WHERE species = 'Tsuga terophylla'
                  ) AS subq"
            },
            {
                "FindTestWithDifferentMappedFieldsAndOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId, 'fancyName', species, 'region', region, 'height', height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      ORDER BY species ASC
                      LIMIT 100
                  ) AS subq"
            },
            {
                "FindTestWithDifferentMappingFirstSingleKeyPaginationAndOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId, 'fancyName', species, 'region', region, 'height', height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      ORDER BY species
                      LIMIT 1
                  ) AS subq"
            },
            {
                "FindTestWithDifferentMappingAfterSingleKeyPaginationAndOrderBy",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId, 'fancyName', species, 'region', region, 'height', height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      WHERE treeId < 2
                      ORDER BY species, treeId
                      LIMIT 101
                  ) AS subq"
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
            DatabaseEngine = TestCategory.MYSQL;
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

        public override string GetDefaultSchema()
        {
            return string.Empty;
        }

        /// <summary>
        /// MySql does not a schema so it lacks
        /// the '.' between schema and table, we
        /// return empty string here for this reason.
        /// </summary>
        /// <returns></returns>
        public override string GetDefaultSchemaForEdmModel()
        {
            return string.Empty;
        }

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }

        // Pending Stored Procedure Support
        [TestMethod]
        [Ignore]
        public override Task FindManyStoredProcedureTest()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task FindOneStoredProcedureTestUsingParameter()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task FindStoredProcedureWithNonEmptyPrimaryKeyRoute()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task FindStoredProcedureWithMissingParameter()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task FindStoredProcedureWithNonexistentParameter()
        {
            throw new NotImplementedException();
        }

    }
}
