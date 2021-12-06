using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.MsSql
{
    /// <summary>
    /// Test REST Apis validating expected results are obtained.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlRestApiTests : MsSqlTestBase
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
            InitializeTestFixture(context, _integrationTableName);

            // Setup REST Components
            //
            _restService = new RestService(_queryEngine, _metadataStoreProvider);
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
            string msSqlQuery = $"SELECT * FROM { _integrationTableName} " +
                $"WHERE id = 2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER";

            await PerformTest(_restController.FindById,
                _integrationTableName,
                primaryKeyRoute,
                queryString: string.Empty,
                msSqlQuery);
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

            await PerformTest(_restController.FindById,
                _integrationTableName,
                primaryKeyRoute,
                queryStringWithFields,
                msSqlQuery);
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

            await PerformTest(_restController.FindById,
                _integrationTableName,
                primaryKeyRoute,
                queryStringWithFields,
                msSqlQuery,
                expectException: true);
        }

        #endregion

        #region Test Helper Functions
        /// <summary>
        /// Performs the test by calling the given api, on the entity name,
        /// primaryKeyRoute and queryString. Uses the msSqlQuery string to get the result
        /// from database and asserts the results match.
        /// </summary>
        /// <param name="api">The REST api to be invoked.</param>
        /// <param name="entityName">The entity name.</param>
        /// <param name="primaryKeyRoute">The primary key portion of the route.</param>
        /// <param name="queryString">The queryString portion of the url.</param>
        /// <param name="msSqlQuery">The expected SQL query.</param>
        /// <param name="expectException">True if we expect exceptions.</param>
        private static async Task PerformTest(Func<string, string, Task<IActionResult>> api,
            string entityName,
            string primaryKeyRoute,
            string queryString,
            string msSqlQuery,
            bool expectException = false)
        {
            _restController.ControllerContext.HttpContext = GetHttpContextWithQueryString(queryString);

            try
            {
                IActionResult actionResult = await api(entityName, primaryKeyRoute);
                OkObjectResult okResult = (OkObjectResult)actionResult;
                JsonDocument actualJson = okResult.Value as JsonDocument;
                Assert.IsFalse(expectException);
                string expected = await GetDatabaseResultAsync(msSqlQuery);
                Assert.AreEqual(expected, ToJsonString(actualJson));
            }
            catch (Exception)
            {
                if (expectException)
                {
                    Assert.IsTrue(expectException);
                }
                else
                {
                    throw;
                }
            }

        }

        /// <summary>
        /// Converts the JsonDocument to a string.
        /// </summary>
        /// <param name="jdoc">The Json document.</param>
        private static string ToJsonString(JsonDocument jdoc)
        {
            using MemoryStream stream = new();
            using Utf8JsonWriter writer = new(stream, new JsonWriterOptions { Indented = false });
            jdoc.WriteTo(writer);
            writer.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        #endregion
    }
}
