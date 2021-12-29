using System.Collections.Generic;
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
                "PostgresFindByIdTest",
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
                "PostgresFindByIdTestWithQueryStringFields",
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
                "PostgresFindTestWithPrimaryKeyContainingForeignKey",
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
                "PostgresFindByIdTestWithInvalidFields",
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
        public static async void InitializeTestFixture(TestContext context)
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
        public override void FindByIdTest()
        {
            base.FindByIdTest();
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public override void FindByIdTestWithQueryStringFields()
        {
            base.FindByIdTestWithQueryStringFields();
        }

        [TestMethod]
        public override void FindTestWithPrimaryKeyContainingForeignKey()
        {
            base.FindTestWithPrimaryKeyContainingForeignKey();
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public override void FindByIdTestWithInvalidFields()
        {
            base.FindByIdTestWithInvalidFields();
        }

        #endregion
    }
}
