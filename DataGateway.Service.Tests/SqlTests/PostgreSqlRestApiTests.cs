using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlRestApiTests : SqlTestBase
    {

        #region Test Fixture Setup
        private static RestService _restService;
        private static RestController _restController;
        private static readonly string _integrationTableName = "books";

        [ClassInitialize]
        public static async Task InitializeTestFixture(TestContext context)
        {
            await InitializeTestFixture(context, _integrationTableName, TestCategory.POSTGRESQL);

            _restService = new RestService(_queryEngine, _metadataStoreProvider, _httpContextAccessor.Object, _authorizationService.Object);
            _restController = new RestController(_restService);
        }

        #endregion

        #region Positive Tests

        /// <summary>
        /// Tests the REST Api for FindById operation without a query string.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTest()
        {
            string primaryKeyRoute = "id/2";
            string queryString = string.Empty;
            string postgresQuery = @"SELECT to_jsonb(subq) AS data
                                    FROM (
                                        SELECT *
                                        FROM " + _integrationTableName + @"
                                        WHERE id = 2
                                        ORDER BY id
                                        LIMIT 1
                                    ) AS subq";

            string expected = await GetDatabaseResultAsync(postgresQuery);

            ConfigureRestController(_restController, queryString);

            await SqlTestHelper.PerformApiTest(
                _restController.Find,
                _integrationTableName,
                primaryKeyRoute,
                GetDatabaseResultAsync(postgresQuery)
            );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async Task FindIdTestWithQueryStringFields()
        {
            string primaryKeyRoute = "id/1";
            string queryStringWithFields = "?_f=id,title";
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS data
                FROM (
                    SELECT id, title
                    FROM " + _integrationTableName + @"
                    WHERE id = 1
                    ORDER BY id
                    LIMIT 1
                ) AS subq
            ";

            string expected = await GetDatabaseResultAsync(postgresQuery);

            ConfigureRestController(_restController, queryStringWithFields);

            await SqlTestHelper.PerformApiTest(
                _restController.Find,
                _integrationTableName,
                primaryKeyRoute,
                GetDatabaseResultAsync(postgresQuery)
            );
        }

        [TestMethod]
        public async Task FindTestWithPrimaryKeyContainingForeignKey()
        {
            string primaryKeyRoute = "id/567/book_id/1";
            string queryStringWithFields = "?_f=id,content";
            string entityName = "reviews";
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS data
                FROM (
                    SELECT id, content
                    FROM " + entityName + @"
                    WHERE id = 567 AND book_id = 1
                    ORDER BY id
                    LIMIT 1
                ) AS subq
            ";

            ConfigureRestController(_restController, queryStringWithFields);

            await SqlTestHelper.PerformApiTest(
                _restController.Find,
                entityName,
                primaryKeyRoute,
                GetDatabaseResultAsync(postgresQuery)
            );
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestWithInvalidFields()
        {
            string primaryKeyRoute = "id/1";
            string queryStringWithFields = "?_f=id,null";
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS data
                FROM (
                    SELECT id, name, type
                    FROM " + _integrationTableName + @"
                ) AS subq
            ";

            ConfigureRestController(_restController, queryStringWithFields);

            await SqlTestHelper.PerformApiTest(
                _restController.Find,
                _integrationTableName,
                primaryKeyRoute,
                GetDatabaseResultAsync(postgresQuery),
                expectException: true
            );
        }

        #endregion
    }
}
