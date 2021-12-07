using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlRestApiTests : SqlTestBase
    {
        #region Test Fixture Setup
        private static RestService _restService;
        private static RestController _restController;
        private static readonly string _integrationTableName = "books";

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static void InitializeTestFixture(TestContext context)
        {
            IntializeTestFixture(context, _integrationTableName, TestCategory.MSSQL);

            // Setup REST Components
            //
            _restService = new RestService(_queryEngine);
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
            string queryString = "";
            string msSqlQuery = $"SELECT * FROM { _integrationTableName} " +
                $"WHERE id = 2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER";

            ConfigureRestController(_restController, queryString);

            await SqlTestHelper.PerformApiTest(
                _restController.FindById,
                _integrationTableName,
                primaryKeyRoute,
                GetDatabaseResultAsync(msSqlQuery)
            );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestWithQueryStringFields()
        {
            string primaryKeyRoute = "id/1";
            string queryStringWithFields = "?_f=id,title";
            string msSqlQuery = $"SELECT [id], [title] FROM { _integrationTableName } " +
                $"WHERE id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER";

            ConfigureRestController(_restController, queryStringWithFields);

            await SqlTestHelper.PerformApiTest(
                _restController.FindById,
                _integrationTableName,
                primaryKeyRoute,
                GetDatabaseResultAsync(msSqlQuery)
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
            string msSqlQuery = $"SELECT [id], [name], [type] FROM { _integrationTableName } " +
                $"WHERE id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER";

            ConfigureRestController(_restController, queryStringWithFields);

            await SqlTestHelper.PerformApiTest(
                _restController.FindById,
                _integrationTableName,
                primaryKeyRoute,
                GetDatabaseResultAsync(msSqlQuery),
                expectException: true
            );
        }

        #endregion
    }
}
