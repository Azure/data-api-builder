using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Services;
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
                "FindByIdTestWithInvalidFields",
                @"
                    SELECT JSON_OBJECT('id', id, 'name', name, 'type', type) AS data
                    FROM (
                        SELECT id, name, type
                        FROM " + _integrationTableName + @"
                    ) AS subq
                "
            },
            {
                "FindTestWithInvalidFields",
                @"
                    SELECT JSON_OBJECT('id', id, 'name', name, 'type', type) AS data
                    FROM (
                        SELECT id, name, type
                        FROM " + _integrationTableName + @"
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
                "DeleteNonExistentTest",
                @"
                    SELECT JSON_OBJECT('id', id) AS data
                    FROM (
                        SELECT id
                        FROM " + _integrationTableName + @"
                        WHERE id = 7
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
                    SELECT JSON_OBJECT('id', id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id > 5000 AND title = 'The Hobbit Returns to The Shire'
                        AND publisher_id = 1234
                    ) AS subq
                "
            },
            {
                "PutOne_Insert_BadReq_Test",
                /// Tests the PutOne functionality with a REST PUT request
                /// with item that does NOT exist, AND parameters incorrectly match schema, results in BadRequest.
                /// sqlQuery represents the query used to get 'expected' result of zero items.
                @"
                    SELECT JSON_OBJECT('id', id) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id > 5000 AND title = 'The Hobbit Returns to The Shire'
                        AND publisher_id = 1234
                    ) AS subq
                "
            },
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

        #endregion

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }

        [TestMethod]
        [Ignore]
        public override Task InsertOneTest()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PutOne_Update_Test()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PutOne_Insert_Test()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PutOne_Insert_BadReq_Test()
        {
            throw new NotImplementedException();
        }
    }
}
