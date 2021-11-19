using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

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

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public new static void InitializeTestFixture(TestContext context)
        {
            MsSqlTestBase.InitializeTestFixture(context);

            // Setup REST Components
            //
            _restService = new RestService(_queryEngine);
            _restController = new RestController(_restService);
        }

        /// <summary>
        /// Cleans up querying table used for Tests in this class. Only to be run once at
        /// conclusion of test run, as defined by MSTest decorator.
        /// </summary>
        [ClassCleanup]
        public new static void CleanupTestFixture()
        {
            MsSqlTestBase.CleanupTestFixture();
        }

        #endregion

        #region Tests
        /// <summary>
        /// Tests the REST Api for FindById operation.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTest()
        {
            string primaryKeyRoute = "id/2";
            string msSqlQuery = $"SELECT * FROM { IntegrationTableName} " +
                $"WHERE id = 2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER";

            await PerformTest(_restController.FindById,
                IntegrationTableName,
                primaryKeyRoute,
                queryString: string.Empty,
                msSqlQuery);

            primaryKeyRoute = "id/1";
            string queryStringWithFields = "?_f=id,name,type";
            msSqlQuery = $"SELECT [id], [name], [type] FROM { IntegrationTableName } " +
                $"WHERE id = 1 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER";

            await PerformTest(_restController.FindById,
                IntegrationTableName,
                primaryKeyRoute,
                queryStringWithFields,
                msSqlQuery);
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
        public static async Task PerformTest(Func<string, string, Task<JsonDocument>> api,
            string entityName,
            string primaryKeyRoute,
            string queryString,
            string msSqlQuery)
        {
            _restController.ControllerContext.HttpContext = GetHttpContextWithQueryString(queryString);
            JsonDocument actualJson = await api(entityName, primaryKeyRoute);
            string expected = await GetDatabaseResultAsync(msSqlQuery);
            Assert.AreEqual(expected, ToJsonString(actualJson));
        }

        /// <summary>
        /// Converts the JsonDocument to a string.
        /// </summary>
        /// <param name="jdoc">The Json document.</param>
        private static string ToJsonString(JsonDocument jdoc)
        {
            MemoryStream stream = new();
            Utf8JsonWriter writer = new (stream, new JsonWriterOptions { Indented = false });
            jdoc.WriteTo(writer);
            writer.Flush();
            return Encoding.UTF8.GetString(stream.ToArray());
        }

        #endregion
    }
}
