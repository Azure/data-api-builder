using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Services;
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
                  SELECT to_jsonb(subq) AS data
                  FROM (
                      SELECT id
                      FROM " + _integrationTableName + @"
                  ) AS subq"
            },
            {
                "FindTestWithQueryStringAllFields",
                @"
                  SELECT to_jsonb(subq) AS data
                  FROM (
                      SELECT *
                      FROM " + _integrationTableName + @"
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
                "FindByIdTestWithQueryStringMultipleFields",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, title
                        FROM " + _integrationTableName + @"
                    ) AS subq
                "
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
                "FindByIdTestWithInvalidFields",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, name, type
                        FROM " + _integrationTableName + @"
                    ) AS subq
                "
            },
            {
                "FindTestWithInvalidFields",
                @"
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id, name, type
                        FROM " + _integrationTableName + @"
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
            await InitializeTestFixture(context, RestApiTestBase._integrationTableName, TestCategory.POSTGRESQL);

            _restService = new RestService(_queryEngine, _metadataStoreProvider);
            _restController = new RestController(_restService);
        }

        #endregion

        public override string GetQuery(string key)
        {
            return _queryMap[key];
        }

        #region Positive Tests

        /// <summary>
        /// Tests the REST Api for FindById operation without a query string.
        /// </summary>
        [TestMethod]
        public override Task FindByIdTest()
        {
            return base.FindByIdTest();
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string with 1 field
        /// including the field names.
        /// </summary>
        [TestMethod]
        public override Task FindTestWithQueryStringOneField()
        {
            return base.FindTestWithQueryStringOneField();
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string with multiple fields
        /// including the field names. Only returns fields designated in the query string.
        /// </summary>
        [TestMethod]
        public override Task FindTestWithQueryStringMultipleFields()
        {
            return base.FindTestWithQueryStringMultipleFields();
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public override Task FindByIdTestWithQueryStringFields()
        {
            return base.FindByIdTestWithQueryStringFields();
        }

        /// <summary>
        /// Tests the REST Api for Find operation with an empty query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public override Task FindTestWithQueryStringAllFields()
        {
            return base.FindTestWithQueryStringAllFields();
        }

        [TestMethod]
        public override Task FindTestWithPrimaryKeyContainingForeignKey()
        {
            return base.FindTestWithPrimaryKeyContainingForeignKey();
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public override Task FindByIdTestWithInvalidFields()
        {
            return base.FindByIdTestWithInvalidFields();
        }

        /// <summary>
        /// Tests the REST Api for Find operation with a query string that has an invalid field
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public override Task FindTestWithInvalidFields()
        {
            return base.FindTestWithInvalidFields();
        }

        #endregion
    }
}
