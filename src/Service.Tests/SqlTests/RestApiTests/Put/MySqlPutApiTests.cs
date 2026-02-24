// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.RestApiTests.Put
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlPutApiTests : PutApiTestBase
    {
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "PutOne_Insert_KeylessWithAutoGenPK_Test",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
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
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
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
                  SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id = 1 and title = 'The Return of the King'
                      ORDER BY id asc
                      LIMIT 1
                  ) AS subq"
            },
            {
                "PutOne_Update_Default_Test",
                @"
                    SELECT JSON_OBJECT('id', id, 'content', content, 'book_id', book_id) AS data
                    FROM (
                        SELECT id, content, book_id
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE id = 568 AND book_id = 1 AND content = 'Good book to read'
                    ) AS subq
                "
            },
            {
                "PutOne_Update_CompositeNonAutoGenPK_Test",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 2 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable = 10
                        AND piecesRequired = 5
                    ) AS subq
                "
            },
            {
                "PutOne_Update_NullOutMissingField_Test",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 1 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable is NULL
                        AND piecesRequired = 5
                    ) AS subq
                "
            },
            {
                "PutOne_Update_Empty_Test",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 2 AND pieceid = 1 AND categoryName ='' AND piecesAvailable = 2
                        AND piecesRequired = 3
                    ) AS subq
                "
            },
            {
                "PutOne_Update_Nulled_Test",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 2 AND pieceid = 1 AND categoryName ='Tales' AND piecesAvailable is NULL
                        AND piecesRequired = 4
                    ) AS subq
                "
            },
            {
                "PutOneUpdateWithComputedFieldMissingFromRequestBody",
                @"
                    SELECT JSON_OBJECT('id', id, 'book_name', book_name, 'copies_sold', copies_sold,
                                        'last_sold_on',last_sold_on) AS data
                    FROM (
                        SELECT id, book_name, copies_sold, DATE_FORMAT(last_sold_on, '%Y-%m-%d %H:%i:%s') AS last_sold_on
                        FROM " + _tableWithReadOnlyFields + @"
                        WHERE id = 1 AND book_name = 'New book' AND copies_sold = 101 AND last_sold_on = '2023-09-12 05:30:30' AND last_sold_on_date = '2023-09-12 05:30:30'
                    ) AS subq
                "
            },
            {
                "PutOneInsertWithComputedFieldMissingFromRequestBody",
                @"
                    SELECT JSON_OBJECT('id', id, 'book_name', book_name, 'copies_sold', copies_sold,
                                        'last_sold_on',last_sold_on) AS data
                    FROM (
                        SELECT id, book_name, copies_sold, DATE_FORMAT(last_sold_on, '%Y-%m-%dT%H:%i:%s') AS last_sold_on
                        FROM " + _tableWithReadOnlyFields + @"
                        WHERE id = 2 AND book_name = 'New book' AND copies_sold = 101
                    ) AS subq
                "
            },
            {
                "PutOne_Update_With_Mapping_Test",
                @"
                  SELECT JSON_ARRAYAGG(JSON_OBJECT('treeId', treeId, 'Scientific Name', species,
                    'United State\'s Region', region, 'height', height)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationMappingTable + @"
                      WHERE treeId = 1
                    ) as subq
                "
            },
            {
                "PutOne_Insert_Test",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title, 'issue_number', issue_number ) AS data
                    FROM (
                        SELECT id, title, issue_number
                        FROM " + _integration_NonAutoGenPK_TableName + @"
                        WHERE id > 5000 AND title = 'Batman Returns'
                            AND issue_number = 1234
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_Nullable_Test",
                @"SELECT JSON_OBJECT('id', id, 'title', title, 'issue_number', issue_number ) AS data
                    FROM (
                        SELECT id, title, issue_number
                        FROM " + _integration_NonAutoGenPK_TableName + @"
                        WHERE id = " + $"{STARTING_ID_FOR_TEST_INSERTS + 1}" + @" AND title = 'Times'
                        AND issue_number is NULL
                    ) as subq
                "
            },
            {
                "PutOne_Insert_AutoGenNonPK_Test",
                @"SELECT JSON_OBJECT('id', id, 'title', title, 'volume', volume, 'categoryName', categoryName,
                    'series_id', series_id) AS data
                    FROM (
                        SELECT id, title, volume, categoryName, series_id
                        FROM " + _integration_AutoGenNonPK_TableName + @"
                        WHERE id = " + $"{STARTING_ID_FOR_TEST_INSERTS}" + @" AND title = 'Star Trek'
                        AND volume IS NOT NULL
                    ) as subq
                "
            },
            {
                "PutOne_Insert_CompositeNonAutoGenPK_Test",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 3 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable = 2
                        AND piecesRequired = 1
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_Default_Test",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 8 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable = 0
                        AND piecesRequired = 0
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_Empty_Test",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 4 AND pieceid = 1 AND categoryName ='' AND piecesAvailable = 2
                        AND piecesRequired = 3
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_Nulled_Test",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 4 AND pieceid = 1 AND categoryName ='SciFi' AND piecesAvailable is NULL
                        AND piecesRequired = 4
                    ) AS subq
                "
            },
            {
                "UpdateSqlInjectionQuery1",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
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
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
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
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
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
                    SELECT JSON_OBJECT('id', id, 'title', title, 'publisher_id', publisher_id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = 7 AND title = 'value; DROP TABLE authors;'
                        AND publisher_id = 1234
                    ) AS subq
                "
            },
            {
                "PutOne_Update_WithExcludeFields_Test",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title) AS data
                    FROM (
                        SELECT id, title
                        FROM " + _integrationTableName + @"
                        WHERE id = 7 AND title = 'The Hobbit Returns to The Shire'
                        AND publisher_id = 1234
                    ) AS subq
                "
            },
            {
                "PutOne_Update_WithNoReadAction_Test",
                @"
                    SELECT JSON_OBJECT('id', id, 'title', title) AS data
                    FROM (
                        SELECT id, title
                        FROM " + _integrationTableName + @"
                        WHERE 0 = 1
                    ) AS subq
                "
            },
            {
                "PutInsert_NoReadTest",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'categoryName', categoryName,
                                        'piecesAvailable',piecesAvailable,'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, categoryName,piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE 0 = 1
                    ) AS subq
                "
            },
            {
                "Put_Insert_WithExcludeFieldsTest",
                @"
                    SELECT JSON_OBJECT('categoryid', categoryid, 'pieceid', pieceid, 'piecesAvailable',piecesAvailable,
                                        'piecesRequired',piecesRequired) AS data
                    FROM (
                        SELECT categoryid, pieceid, piecesAvailable,piecesRequired
                        FROM " + _Composite_NonAutoGenPK_TableName + @"
                        WHERE categoryid = 0 AND pieceid = 7 AND categoryName ='SciFi' AND piecesAvailable = 4
                        AND piecesRequired = 4
                    ) AS subq
                "
            }
        };

        #region overridden tests

        [TestMethod]
        [Ignore]
        public override Task PutOneUpdateWithDatabasePolicy()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PutOneInsertWithDatabasePolicy()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PutOneInsertInViewTest()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PutOneUpdateViewTest()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public void PutOneInViewBadRequest(string expectedErrorMessage)
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public void PutOneUpdateNonNullableDefaultFieldMissingFromJsonBodyTest()
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
            await InitializeTestFixture();
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

        /// <summary>
        /// We have 1 test, which is named
        /// PutOneUpdateNonNullableDefaultFieldMissingFromJsonBodyTest
        /// that will have Db specific error messages.
        /// We return the mysql specific message here.
        /// </summary>
        /// <returns></returns>
        public override string GetUniqueDbErrorMessage()
        {
            return "Column 'piecesRequired' cannot be null";
        }
    }
}
