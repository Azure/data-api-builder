using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Patch
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlPatchApiTests : PatchApiTestBase
    {
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "PatchOne_Insert_NonAutoGenPK_Test",
                @"SELECT JSON_OBJECT('id', id, 'title', title, 'issue_number', issue_number ) AS data
                    FROM (
                        SELECT id, title, issue_number
                        FROM " + _integration_NonAutoGenPK_TableName + @"
                        WHERE id = 2 AND title = 'Batman Begins'
                        AND issue_number = 1234
                    ) as subq
                "
            },
            {
                "PatchOne_Insert_UniqueCharacters_Test",
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
                "PatchOne_Insert_Mapping_Test",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId, 'Scientific Name', species,
                    'United State\'s Region', region, 'height', height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      WHERE treeId = 4
                    ) as subq
                "
            },
            {
                "PatchOne_Insert_CompositeNonAutoGenPK_Test",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 4 AND pieceid = 1 AND categoryName ='FairyTales' AND piecesAvailable = 5
                        AND piecesRequired = 4
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_Empty_Test",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 5 AND pieceid = 1 AND categoryName ='' AND piecesAvailable = 5
                        AND piecesRequired = 4
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_Default_Test",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 7 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable = 0
                        AND piecesRequired = 0
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_Nulled_Test",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 3 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable is NULL
                        AND piecesRequired = 4
                    ) AS subq
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
                "PatchOne_Update_IfMatchHeaders_Test",
                @"
                  SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id = 1 and title = 'The Hobbit Returns to The Shire' and publisher_id = 1234
                      ORDER BY id asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "PatchOne_Update_Default_Test",
                @"
                    SELECT JSON_OBJECT('id', id, 'content', content, 'book_id', book_id) AS data
                    FROM (
                        SELECT id, content, book_id
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = 567 AND book_id = 1 AND content = 'That''s a great book'
                    ) AS subq
                "
            },
            {
                "PatchOne_Update_CompositeNonAutoGenPK_Test",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable = 10
                        AND piecesRequired = 0
                    ) AS subq
                "
            },
            {
                "PatchOne_Update_Empty_Test",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND categoryName ='' AND piecesAvailable = 10
                        AND piecesRequired = 0
                    ) AS subq
                "
            },
            {
                "PatchOne_Update_Nulled_Test",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable is NULL
                        AND piecesRequired = 0
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

        #region Overriden tests
        [TestMethod]
        [Ignore]
        public override Task PatchOneInsertInViewTest()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PatchOneUpdateViewTest()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PatchOneViewBadRequestTest(string expectedErrorMessage)
        {
            throw new NotImplementedException();
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
