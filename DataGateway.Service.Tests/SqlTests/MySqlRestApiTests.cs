using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlRestApiTests : RestApiTestBase
    {
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
                "InsertOneTest",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 5001
                    ) AS subq
                "
            },
            {
                "InsertOneInCompositeKeyTableTest",
                @"
                    SELECT JSON_OBJECT('id', id, 'content', content, 'book_id', book_id) AS data
                    FROM (
                        SELECT id, content, book_id
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND book_id = 1
                    ) AS subq
                "
            },
            {
                "DeleteOneTest",
                @"
                    SELECT JSON_OBJECT('id', id) AS data
                    FROM (
                        SELECT id
                        FROM " + _integrationTableName + @"
                        WHERE id = 5
                    ) AS subq
                "
            },
            {
                "PutOne_Update_Test",
                @"
                    SELECT JSON_OBJECT('id', id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 7 AND title = 'The Hobbit Returns to The Shire'
                        AND publisher_id = 1234
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_Test",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title, 'issueNumber', issueNumber ) AS data
                    FROM (
                        SELECT id, title, issueNumber
                        FROM " + _integration_NonAutoGenPK_TableName + @"
                        WHERE id > 5000 AND title = 'Batman Returns'
                            AND issueNumber = 1234
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_Nullable_Test",
                @"SELECT JSON_OBJECT('id', id, 'title', title, 'issueNumber', issueNumber ) AS data
                    FROM (
                        SELECT id, title, issueNumber
                        FROM " + _integration_NonAutoGenPK_TableName + @"
                        WHERE id = " + $"{STARTING_ID_FOR_TEST_INSERTS + 1}" + @" AND title = 'Times'
                        AND issueNumber is NULL
                    ) as subq
                "
            },
            {
                "PutOne_Insert_AutoGenNonPK_Test",
                @"SELECT JSON_OBJECT('id', id, 'title', title, 'volume', volume ) AS data
                    FROM (
                        SELECT id, title, volume
                        FROM " + _integration_AutoGenNonPK_TableName + @"
                        WHERE id = " + $"{STARTING_ID_FOR_TEST_INSERTS}" + @" AND title = 'Star Trek'
                        AND volume IS NOT NULL
                    ) as subq
                "
            },
            {
                "PatchOne_Insert_NonAutoGenPK_Test",
                @"SELECT JSON_OBJECT('id', id, 'title', title, 'issueNumber', issueNumber ) AS data
                    FROM (
                        SELECT id, title, issueNumber
                        FROM " + _integration_NonAutoGenPK_TableName + @"
                        WHERE id = 2 AND title = 'Batman Begins'
                        AND issueNumber = 1234
                    ) as subq
                "
            },
            {
                "PatchOne_Update_Test",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 8 AND title = 'Heart of Darkness'
                        AND publisher_id = 2324
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_PKAutoGen_Test",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 1000 AND title = 'The Hobbit Returns to The Shire'
                        AND publisher_id = 1234
                    ) AS subq
                "
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
            await InitializeTestFixture(context, RestApiTestBase._integrationTableName, TestCategory.MYSQL);

            _restService = new RestService(_queryEngine,
                _mutationEngine,
                _metadataStoreProvider,
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
