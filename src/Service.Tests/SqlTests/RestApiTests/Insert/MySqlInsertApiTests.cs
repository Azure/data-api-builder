using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Insert
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlInsertApiTests : InsertApiTestBase
    {
        protected static Dictionary<string, string> _queryMap = new()
        {
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
                "InsertOneUniqueCharactersTest",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('┬─┬ノ( º _ ºノ)', NoteNum,
                  '始計', DetailAssessmentAndPlanning, '作戰', WagingWar,
                  '謀攻', StrategicAttack)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationUniqueCharactersTable + @"
                      WHERE NoteNum = 2
                  ) AS subq
                "
            },
            {
                "InsertOneWithMappingTest",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId, 'Scientific Name', species,
                    'United State\'s Region', region)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      WHERE treeId = 3
                    ) AS subq
                "
            },
            {
                "InsertOneInCompositeNonAutoGenPKTest",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                      'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 5 AND pieceid = 2 AND categoryName ='FairyTales' AND piecesAvailable = 0
                        AND piecesRequired = 0
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
                "InsertOneWithNullFieldValue",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 3 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable is NULL
                        AND piecesRequired = 1
                    ) AS subq
                "
            },
            {
                "InsertOneInDefaultTestTable",
                @"
                    SELECT JSON_OBJECT('id', id, 'content', content, 'book_id', book_id) AS data
                    FROM (
                        SELECT id, content, book_id
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = " + $"{STARTING_ID_FOR_TEST_INSERTS + 1}" + @"
                        AND book_id = 2 AND content = 'Its a classic'
                    ) AS subq
                "
            },
            {
                "InsertSqlInjectionQuery1",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = ' UNION SELECT * FROM books/*'
                    ) AS subq
                "
            },
            {
                "InsertSqlInjectionQuery2",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = '; SELECT * FROM information_schema.tables/*'
                    ) AS subq
                "
            },
            {
                "InsertSqlInjectionQuery3",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = 'value; SELECT * FROM v$version--'
                    ) AS subq
                "
            },
            {
                "InsertSqlInjectionQuery4",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = 'id; DROP TABLE books;'
                    ) AS subq
                "
            },
            {
                "InsertSqlInjectionQuery5",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                        AND title = ' '' UNION SELECT * FROM books/*'
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

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }
    }
}
