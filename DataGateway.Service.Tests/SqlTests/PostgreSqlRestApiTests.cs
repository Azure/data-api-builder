using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlRestApiTests : RestApiTestBase
    {
        protected static Dictionary<string, string> _queryMap = new()
        {
            {
                "FindByIdTest",
                @"
                  SELECT to_jsonb(subq) AS data
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
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT id
                      FROM " + _integrationTableName + @"
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringAllFields",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindByIdTestWithQueryStringFields",
                @"
                    SELECT to_jsonb(subq) AS data
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
                    SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT id, title
                        FROM " + _integrationTableName + @"
                        ORDER BY id
                    ) AS subq
                "
            },
            {
                "FindTestWithFilterQueryStringOneEqFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id = 1
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryStringValueFirstOneEqFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id = 2
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneGtFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id > 3
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneGeFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id >= 4
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneLtFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id < 5
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneLeFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id <= 4
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneNeFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id != 3
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneNotFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE not (id < 2)
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneRightNullEqFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE NOT (title IS NULL)
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneLeftNullNeFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE title IS NOT NULL
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryOneLeftNullRightNullGtFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE NULL > NULL
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryStringSingleAndFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id < 3 AND id > 1
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryStringSingleOrFilter",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id < 3 OR id > 4
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryStringMultipleAndFilters",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id < 4 AND id > 1 AND title != 'Awesome book'
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryStringMultipleOrFilters",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE id = 1 OR id = 2 OR id = 3
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryStringMultipleAndOrFilters",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE (id > 2 AND id < 4) OR (title = 'Awesome book')
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithFilterQueryStringMultipleNotAndOrFilters",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
                      WHERE (NOT (id < 3) OR id < 4) OR NOT (title = 'Awesome book')
                      ORDER BY id
                  ) AS subq"
            },
            {
                "FindTestWithPrimaryKeyContainingForeignKey",
                @"
                    SELECT to_jsonb(subq) AS data
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
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        ORDER BY id
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithFirstMultiKeyPagination",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _tableWithCompositePrimaryKey + @"
                        ORDER BY book_id, id
                        LIMIT 1
                    ) AS subq
                "
            },
            {
                "FindTestWithAfterSingleKeyPagination",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT *
                        FROM " + _integrationTableName + @"
                        WHERE id > 7
                        ORDER BY id
                        LIMIT 100
                    ) AS subq
                "
            },
            {
                "FindTestWithAfterMultiKeyPagination",
                @"
                  SELECT json_agg(to_jsonb(subq)) AS data
                    FROM (
                        SELECT *
                        FROM " + _tableWithCompositePrimaryKey + @"
                        WHERE book_id > 1 OR (book_id = 1 AND id > 567)
                        ORDER BY book_id, id
                        LIMIT 100
                    ) AS subq
                "
            },
            {
                "InsertOneTest",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title, publisher_id
                        FROM " + _integrationTableName + @"
                        WHERE id = " + STARTING_ID_FOR_TEST_INSERTS + @"
                    ) AS subq
                "
            },
            {
                "InsertOneInCompositeKeyTableTest",
                @"
                    SELECT to_jsonb(subq) AS data
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
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id
                        FROM " + _integrationTableName + @"
                        WHERE id = 5
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
            await InitializeTestFixture(context, TestCategory.POSTGRESQL);

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
        public override Task InsertOneInCompositeKeyTableTest()
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

        [TestMethod]
        [Ignore]
        public override Task PutOne_Insert_BadReq_NonNullable_Test()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PutOne_Insert_PKAutoGen_Test()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PutOne_Insert_CompositePKAutoGen_Test()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PutOne_Insert_BadReq_AutoGen_NonNullable_Test()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PatchOne_Insert_NonAutoGenPK_Test()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PatchOne_Update_Test()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PutOneWithNonNullableFieldMissingInJsonBodyTest()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PatchOne_Insert_PKAutoGen_Test()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PatchOne_Insert_WithoutNonNullableField_Test()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task InsertOneWithNullFieldValue()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task InsertOneWithNonNullableFieldAsNull()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PutOneWithNonNullableFieldAsNull()
        {
            throw new NotImplementedException();
        }

        [TestMethod]
        [Ignore]
        public override Task PatchOneWithNonNullableFieldAsNull()
        {
            throw new NotImplementedException();
        }
    }
}
