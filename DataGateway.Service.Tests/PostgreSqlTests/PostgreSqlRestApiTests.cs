using System;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.PostgreSql {
    
    [TestClass, TestCategory(TestCategory.POSTGRESSQL)]
    public class PostgreSqlRestApiTests : PostgreSqlTestBase {

        #region Test Fixture Setup
        private static RestService _restService;
        private static RestController _restController;
        private static readonly string _integrationTableName = "books";

        [ClassInitialize]
        public static void InitializeTestFixture(TestContext context){
            IntializeTestFixture(context, _integrationTableName);

            _restService = new RestService(_queryEngine);
            _restController = new RestController(_restService);
        }

        #endregion

        #region Positive Tests

        /// <summary>
        /// Tests the REST Api for FindById operation without a query string.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTest(){
            string primaryKeyRoute = "id/2";
            string postgresQuery = @"SELECT to_jsonb(subq) AS data
                                    FROM (
                                        SELECT * 
                                        FROM " + _integrationTableName + @"
                                        WHERE id = 2
                                        ORDER BY id
                                        LIMIT 1
                                    ) AS subq";
            
            await PerformTest( 
                _restController.FindById,
                _integrationTableName,
                primaryKeyRoute,
                queryString: string.Empty,
                postgresQuery
            );
        }

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// including the field names.
        /// </summary>
        [TestMethod]
        public async Task FindIdTestWithQueryStringFields(){
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

            await PerformTest(
                _restController.FindById,
                _integrationTableName,
                primaryKeyRoute,
                queryStringWithFields,
                postgresQuery
            );
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// Tests the REST Api for FindById operation with a query string
        /// having invalid field names.
        /// </summary>
        [TestMethod]
        public async Task FindByIdTestWithInvalidFields(){
            string primaryKeyRoute = "id/1";
            string queryStringWithFields = "?_f=id,null";
            string postgresQuery = @"
                SELECT to_jsonb(subq) AS data
                FROM (
                    SELECT id, name, type
                    FROM " + _integrationTableName + @"
                ) AS subq
            "; 

            await PerformTest(
                _restController.FindById,
                _integrationTableName,
                primaryKeyRoute,
                queryStringWithFields,
                postgresQuery,
                expectException: true
            );
        }

        #endregion 

        #region Test Helper Functions

        /// <summary>
        /// Performs the test by calling the given api, on the entity name,
        /// primaryKeyRoute and queryString. Uses the Postgres string to get the result
        /// from database and asserts the results match.
        /// </summary>
        /// <param name="api">The REST api to be invoked.</param>
        /// <param name="entityName">The entity name.</param>
        /// <param name="primaryKeyRoute">The primary key portion of the route.</param>
        /// <param name="queryString">The queryString portion of the url.</param>
        /// <param name="postgresQuery">The expected Postgres query.</param>
        /// <param name="expectException">True if we expect exceptions.</param>
        private static async Task PerformTest(
            Func<string, string, Task<JsonDocument>> api,
            string entityName,
            string primaryKeyRoute,
            string queryString,
            string postgresQuery,
            bool expectException = false ) {

            _restController.ControllerContext.HttpContext = GetHttpContextWithQueryString(queryString);

            try{
                JsonDocument actualJson = await api(entityName, primaryKeyRoute);
                
                Assert.IsFalse(expectException);

                string actual = actualJson.RootElement.ToString();
                string expected = await GetDatabaseResultAsync(postgresQuery);
                
                Assert.IsTrue(JsonStringsDeepEqual(expected, actual), 
                        $"\nExpected:<{expected}>\nActual:\n");
            
            } catch (Exception e) {
                // Consider scenarios:
                // no exception + expectException: true -> test fails
                // exception + expectException: true    -> test passes
                // no exception + expectException: false-> test passes
                // exception + expectException: false   -> test fails
                if(expectException && !(e is AssertFailedException))
                    Assert.IsTrue(expectException);
                else
                    throw;
            }
        }

        #endregion
    }
}