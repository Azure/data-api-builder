using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Text.Json;
using System.Threading.Tasks;

namespace Azure.DataGateway.Service.Tests.MsSql
{
    /// <summary>
    /// Test GraphQL Queries validating proper resolver/engine operation.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGraphQLQueryTests : MsSqlTestBase
    {
        #region Test Fixture Setup
        private static GraphQLService _graphQLService;
        private static GraphQLController _graphQLController;

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public new static void InitializeTestFixture(TestContext context)
        {
            MsSqlTestBase.InitializeTestFixture(context);

            // Setup GraphQL Components
            //
            _graphQLService = new GraphQLService(_queryEngine, mutationEngine: null, _metadataStoreProvider);
            _graphQLController = new GraphQLController(_graphQLService);
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
        /// Get result of quering singular object
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task SingleResultQuery()
        {
            string graphQLQueryName = "characterById";
            string graphQLQuery = "{\"query\":\"{\\n characterById(id:2){\\n name\\n primaryFunction\\n}\\n}\\n\"}";
            string msSqlQuery = $"SELECT name, primaryFunction FROM { IntegrationTableName} WHERE id = 2 FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            Assert.AreEqual(actual, expected);
        }

        /// <summary>
        /// Gets array of results for querying more than one item.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task MultipleResultQuery()
        {
            string graphQLQueryName = "characterList";
            string graphQLQuery = "{\"query\":\"{\\n  characterList {\\n    name\\n    primaryFunction\\n  }\\n}\\n\"}";
            string msSqlQuery = $"SELECT name, primaryFunction FROM character FOR JSON PATH, INCLUDE_NULL_VALUES";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

            Assert.AreEqual(actual, expected);
        }

        #endregion

        #region Query Test Helper Functions
        /// <summary>
        /// Sends graphQL query through graphQL service, consisting of gql engine processing (resolvers, object serialization)
        /// returning JSON formatted result from 'data' property. 
        /// </summary>
        /// <param name="graphQLQuery"></param>
        /// <param name="graphQLQueryName"></param>
        /// <returns>string in JSON format</returns>
        public static async Task<string> GetGraphQLResultAsync(string graphQLQuery, string graphQLQueryName)
        {
            _graphQLController.ControllerContext.HttpContext = MsSqlTestBase.GetHttpContextWithBody(graphQLQuery);
            JsonDocument graphQLResult = await _graphQLController.PostAsync();
            JsonElement graphQLResultData = graphQLResult.RootElement.GetProperty("data").GetProperty(graphQLQueryName);
            return graphQLResultData.ToString();
        }

        #endregion
    }
}
