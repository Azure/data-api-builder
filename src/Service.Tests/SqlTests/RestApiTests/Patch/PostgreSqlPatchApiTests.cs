using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Patch
{
    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlPatchApiTests : PatchApiTestBase
    {
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "PatchOne_Insert_Mapping_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT  ""treeId"", ""species"" AS ""Scientific Name"", ""region""
                            AS ""United State's Region"", ""height""
                        FROM " + _integrationMappingTable + @"
                        WHERE ""treeId"" = 4
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_NonAutoGenPK_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, issue_number
                        FROM " + "foo." + _integration_NonAutoGenPK_TableName + @"
                        WHERE id = 2 AND title = 'Batman Begins' AND issue_number = 1234
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_UniqueCharacters_Test",
                @"
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT  ""NoteNum"" AS ""┬─┬ノ( º _ ºノ)"", ""DetailAssessmentAndPlanning""
                        AS ""始計"", ""WagingWar"" AS ""作戰"", ""StrategicAttack"" AS ""謀攻""
                        FROM " + _integrationUniqueCharactersTable + @"
                        WHERE ""NoteNum"" = 2
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_CompositeNonAutoGenPK_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 4 AND pieceid = 1 AND ""categoryName"" = 'FairyTales'
                            AND ""piecesAvailable"" = 5 AND ""piecesRequired"" = 4
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_Empty_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 5 AND pieceid = 1 AND ""categoryName"" = ''
                            AND ""piecesAvailable"" = 5 AND ""piecesRequired"" = 4
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_Default_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 7 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" = 0 AND ""piecesRequired"" = 0
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_Nulled_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 3 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" is NULL AND ""piecesRequired"" = 4
                    ) AS subq
                "
            },
            {
                "PatchOne_Update_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 8 AND title = 'Heart of Darkness' AND publisher_id = 2324
                    ) AS subq
                "
            },
            {
                "PatchOne_Update_IfMatchHeaders_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 1 AND title = 'The Hobbit Returns to The Shire'
                    ) AS subq
                "
            },
            {
                "PatchOne_Update_Default_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, book_id, content
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = 567 AND book_id = 1 AND content = 'That''s a great book'
                    ) AS subq
                "
            },
            {
                "PatchOne_Update_CompositeNonAutoGenPK_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" = 10 AND ""piecesRequired"" = 0
                    ) AS subq
                "
            },
            {
                "PatchOne_Update_Empty_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND ""categoryName"" = ''
                            AND ""piecesAvailable"" = 10 AND ""piecesRequired"" = 0
                    ) AS subq
                "
            },
            {
                "PatchOne_Update_Nulled_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" is NULL AND ""piecesRequired"" = 0
                    ) AS subq
                "
            },
            {
                "PatchOne_Insert_PKAutoGen_Test",
                $"INSERT INTO { _integrationTableName } " +
                $"(id, title, publisher_id)" +
                $"VALUES (1000,'The Hobbit Returns to The Shire',1234)"
            },
            {
                "PatchOneInsertInStocksViewSelected",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable""
                        FROM " + _simple_subset_stocks + @"
                        WHERE categoryid = 4 AND pieceid = 1
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "PatchOneUpdateStocksViewSelected",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable""
                        FROM " + _simple_subset_stocks + @"
                        WHERE categoryid = 2 AND pieceid = 1
                        LIMIT 1
                    ) AS subq
                "
            }
        };

        [TestMethod]
        public async Task PatchOneViewBadRequestTest()
        {
            string expectedErrorMessage = $"55000: cannot update view \"{_composite_subset_bookPub}\"";
            await base.PatchOneViewBadRequestTest(expectedErrorMessage);
        }

        #region Overriden tests

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
            DatabaseEngine = TestCategory.POSTGRESQL;
            await InitializeTestFixture(context);
        }

        #endregion

        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }
    }
}
