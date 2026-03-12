// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Put
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlPutApiTests : PutApiTestBase
    {
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "PutOne_Insert_KeylessWithAutoGenPK_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                            AND title = 'My New Book' AND publisher_id = 1234
                    ) AS subq
                "
            },
            {
                "PutOne_Update_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 7 AND title = 'The Hobbit Returns to The Shire'
                            AND publisher_id = 1234
                    ) AS subq
                "
            },
            {
                "PutOne_Update_IfMatchHeaders_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 1 AND title = 'The Return of the King'
                    ) AS subq
                "
            },
            {
                "PutOneUpdateWithDatabasePolicy",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 100 AND pieceid = 99 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" = 4 AND ""piecesRequired"" = 5 AND pieceid != 1
                    ) AS subq
                "
            },
            {
                "PutOneUpdateAccessibleRowWithDatabasePolicy",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, name
                        FROM " + _foreignKeyTableName + @"
                        WHERE id = 2345 AND name = 'New Publisher' AND id != 1234
                    ) AS subq
                "
            },
            {
                "PutOne_Update_Default_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, book_id, content
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = 568 AND book_id = 1 AND content ='Good book to read'
                    ) AS subq
                "
            },
            {
                "PutOne_Update_CompositeNonAutoGenPK_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 2 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" = 10 AND ""piecesRequired"" = 5
                    ) AS subq
                "
            },
            {
                "PutOne_Update_NullOutMissingField_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" is NULL AND ""piecesRequired"" = 5
                    ) AS subq
                "
            },
            {
                "PutOne_Update_Empty_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 2 AND pieceid = 1 AND ""categoryName"" = ''
                            AND ""piecesAvailable"" = 2 AND ""piecesRequired"" = 3
                    ) AS subq
                "
            },
            {
                "PutOne_Update_Nulled_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 2 AND pieceid = 1 AND ""categoryName"" = 'Tales'
                            AND ""piecesAvailable"" is NULL AND ""piecesRequired"" = 4
                    ) AS subq
                "
            },
            {
                "PutOneUpdateWithComputedFieldMissingFromRequestBody",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, book_name, copies_sold, last_sold_on, last_sold_on_date
                        FROM " + _tableWithReadOnlyFields + @"
                        WHERE id = 1 AND book_name = 'New book' AND copies_sold = 101 AND last_sold_on = last_sold_on_date
                    ) AS subq
                "
            },
            {
                "PutOneInsertWithComputedFieldMissingFromRequestBody",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, book_name, copies_sold, last_sold_on, last_sold_on_date
                        FROM " + _tableWithReadOnlyFields + @"
                        WHERE id = 2 AND book_name = 'New book' AND copies_sold = 101 AND last_sold_on = '9999-12-31 23:59:59.997'
                        AND last_sold_on_date = '9999-12-31 23:59:59.997'
                    ) AS subq
                "
            },
            {
                "PutOne_Update_With_Mapping_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT  ""treeId"", ""species"" AS ""Scientific Name"", ""region""
                            AS ""United State's Region"", ""height""
                        FROM " + _integrationMappingTable + @"
                        WHERE ""treeId"" = 1
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_Nullable_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, issue_number
                        FROM " + "foo." + _integration_NonAutoGenPK_TableName + @"
                        WHERE id = " + (STARTING_ID_FOR_TEST_INSERTS + 1) + @"
                            AND title = 'Times' AND issue_number is NULL
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_PKAutoGen_Test",
                $"INSERT INTO { _integrationTableName } " +
                $"(id, title, publisher_id)" +
                $"VALUES (1000,'The Hobbit Returns to The Shire',1234)"
            },
            {
                "PutOne_Insert_AutoGenNonPK_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, volume, ""categoryName"", series_id
                        FROM " + _integration_AutoGenNonPK_TableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                            AND title = 'Star Trek' AND volume IS NOT NULL
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_CompositeNonAutoGenPK_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 3 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" = 2 AND ""piecesRequired"" = 1
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_Default_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 8 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" = 0 AND ""piecesRequired"" = 0
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_Empty_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 4 AND pieceid = 1 AND ""categoryName"" = ''
                            AND ""piecesAvailable"" = 2 AND ""piecesRequired"" = 3
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_Nulled_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 4 AND pieceid = 1 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" is NULL AND ""piecesRequired"" = 4
                    ) AS subq
                "
            },
            {
                "PutOneInsertInStocksViewSelected",
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
                "PutOneUpdateStocksViewSelected",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable""
                        FROM " + _simple_subset_stocks + @"
                        WHERE categoryid = 2 AND pieceid = 1
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "UpdateSqlInjectionQuery1",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 7 AND title = ' UNION SELECT * FROM books/*'
                            AND publisher_id = 1234
                    ) AS subq
                "
            },
            {
                "UpdateSqlInjectionQuery2",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 7 AND title = '; SELECT * FROM information_schema.tables/*'
                            AND publisher_id = 1234
                    ) AS subq
                "
            },
            {
                "UpdateSqlInjectionQuery3",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 7 AND title = 'value; SELECT * FROM v$version--'
                            AND publisher_id = 1234
                    ) AS subq
                "
            },
            {
                "UpdateSqlInjectionQuery4",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 7 AND title = 'value; DROP TABLE authors;'
                            AND publisher_id = 1234
                    ) AS subq
                "
            },
            {
                "PutOne_Update_WithNoReadAction_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE 0 = 1
                    ) AS subq
                "
            },
            {
                "PutOne_Update_WithExcludeFields_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title
                        FROM " + _integrationTableName + @"
                        WHERE id = 7 AND title = 'The Hobbit Returns to The Shire'
                        AND publisher_id = 1234
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_Test",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, issue_number
                        FROM " + "foo." + _integration_NonAutoGenPK_TableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @" AND title = 'Batman Returns'
                            AND issue_number = 1234
                    ) AS subq
                "
            },
            {
                "PutInsert_NoReadTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""categoryName"", ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE 0 = 1
                    ) AS subq
                "
            },
            {
                "Put_Insert_WithExcludeFieldsTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT categoryid, pieceid, ""piecesAvailable"", ""piecesRequired""
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 0 AND pieceid = 7 AND ""categoryName"" = 'SciFi'
                            AND ""piecesAvailable"" = 4 AND ""piecesRequired"" = 4
                    ) AS subq
                "
            }

        };

        [TestMethod]
        public async Task PutOneInViewBadRequest()
        {
            string expectedErrorMessage = $"55000: cannot update view \"{_composite_subset_bookPub}\"";
            await base.PutOneInViewBadRequest(
                expectedErrorMessage,
                isExpectedErrorMsgSubstr: true);
        }

        [TestMethod]
        public async Task PutOneUpdateNonNullableDefaultFieldMissingFromJsonBodyTest()
        {
            await base.PutOneUpdateNonNullableDefaultFieldMissingFromJsonBodyTest(
                isExpectedErrorMsgSubstr: true);
        }

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
            await InitializeTestFixture();
        }

        #endregion

        #region overridden tests

        [TestMethod]
        [Ignore]
        public override Task PutOneInsertWithDatabasePolicy()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PutOneWithUnsatisfiedDatabasePolicy()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PutOneInsertInTableWithFieldsInDbPolicyNotPresentInBody()
        {
            throw new NotImplementedException();
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

        /// <summary>
        /// We have 1 test that is named
        /// PutOneUpdateNonNullableDefaultFieldMissingFromJsonBodyTest
        /// which will have Db specific error messages.
        /// We return the postgres specific message here.
        /// </summary>
        /// <returns></returns>
        public override string GetUniqueDbErrorMessage()
        {
            return "23502: null value in column \"piecesRequired\" of relation \"stocks\" violates not-null constraint";
        }
    }
}
