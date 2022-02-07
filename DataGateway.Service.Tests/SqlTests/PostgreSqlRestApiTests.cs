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
                "DeleteOneTest",
                @"
                    SELECT to_jsonb(subq) AS data
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
                    SELECT to_jsonb(subq) AS data
                    FROM (
                        SELECT id
                        FROM " + _integrationTableName + @"
                        WHERE id = 7
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
    }
}
