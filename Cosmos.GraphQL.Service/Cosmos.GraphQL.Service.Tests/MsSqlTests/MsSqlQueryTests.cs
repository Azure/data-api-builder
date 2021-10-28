using System.Data.Common;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Cosmos.GraphQL.Service.Controllers;
using Cosmos.GraphQL.Service.Resolvers;
using Cosmos.GraphQL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Cosmos.GraphQL.Service.Tests.MsSql
{
    /// <summary>
    /// Test GraphQL Queries validating proper resolver/engine operation.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MsSql)]
    public class MsSqlQueryTests
    {
        #region Test Fixture Setup
        private static IMetadataStoreProvider _metadataStoreProvider;
        private static IQueryExecutor _queryExecutor;
        private static IQueryBuilder _queryBuilder;
        private static IQueryEngine _queryEngine;
        private static GraphQLService _graphQLService;
        private static GraphQLController _graphQLController;
        private static DatabaseInteractor _databaseInteractor;

        public static string IntegrationTableName { get; } = "character";

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator. 
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static void InitializeTestFixure(TestContext context)
        {
            // Setup Schema and Resolvers
            //
            _metadataStoreProvider = new MetadataStoreProviderForTest();
            _metadataStoreProvider.StoreGraphQLSchema(MsSqlTestHelper.GraphQLSchema);
            _metadataStoreProvider.StoreQueryResolver(MsSqlTestHelper.GetQueryResolverJson(MsSqlTestHelper.CharacterByIdResolver));
            _metadataStoreProvider.StoreQueryResolver(MsSqlTestHelper.GetQueryResolverJson(MsSqlTestHelper.CharacterListResolver));

            // Setup Database Components
            //
            _queryExecutor = new QueryExecutor<SqlConnection>(MsSqlTestHelper.DataGatewayConfig);
            _queryBuilder = new MsSqlQueryBuilder();
            _queryEngine = new SqlQueryEngine(_metadataStoreProvider, _queryExecutor, _queryBuilder);

            // Setup Integration DB Components
            //
            _databaseInteractor = new DatabaseInteractor(_queryExecutor);
            CreateTable();
            InsertData();

            // Setup GraphQL Components
            //
            _graphQLService = new GraphQLService(_queryEngine, mutationEngine: null, _metadataStoreProvider);
            _graphQLController = new GraphQLController(logger: null, _queryEngine, mutationEngine: null, _graphQLService);
        }

        /// <summary>
        /// Cleans up querying table used for Tests in this class. Only to be run once at
        /// conclusion of test run, as defined by MSTest decorator.
        /// </summary>
        [ClassCleanup]
        public static void CleanupTestFixture()
        {
            _databaseInteractor.DropTable(IntegrationTableName);
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
            _graphQLController.ControllerContext.HttpContext = GetHttpContextWithBody(graphQLQuery);
            JsonDocument graphQLResult = await _graphQLController.PostAsync();
            JsonElement graphQLResultData = graphQLResult.RootElement.GetProperty("data").GetProperty(graphQLQueryName);
            return graphQLResultData.ToString();
        }

        /// <summary>
        /// Sends raw SQL query to database engine to retrieve expected result in JSON format
        /// </summary>
        /// <param name="queryText">raw database query</param>
        /// <returns>string in JSON format</returns>
        public static async Task<string> GetDatabaseResultAsync(string queryText)
        {
            JsonDocument sqlResult = JsonDocument.Parse("{ }");
            using DbDataReader reader = _databaseInteractor.QueryExecutor.ExecuteQueryAsync(queryText, parameters: null).Result;

            if (await reader.ReadAsync())
            {
                sqlResult = JsonDocument.Parse(reader.GetString(0));
            }

            JsonElement sqlResultData = sqlResult.RootElement;

            return sqlResultData.ToString();
        }
        #endregion
        #region Helper Functions
        /// <summary>
        /// Creates a default table
        /// </summary>
        private static void CreateTable()
        {
            _databaseInteractor.CreateTable(IntegrationTableName, "id int, name varchar(20), type varchar(20), homePlanet int, primaryFunction varchar(20)");
        }

        /// <summary>
        /// Inserts some default data into the table
        /// </summary>
        private static void InsertData()
        {
            _databaseInteractor.InsertData(IntegrationTableName, "'1', 'Mace', 'Jedi','1','Master'");
            _databaseInteractor.InsertData(IntegrationTableName, "'2', 'Plo Koon', 'Jedi','2','Master'");
            _databaseInteractor.InsertData(IntegrationTableName, "'3', 'Yoda', 'Jedi','3','Master'");
        }
        /// <summary>
        /// returns httpcontext with body consisting of GraphQLQuery 
        /// </summary>
        /// <param name="data">GraphQLQuery</param>
        /// <returns>The http context with given data as stream of utf-8 bytes.</returns>
        private static DefaultHttpContext GetHttpContextWithBody(string data)
        {
            MemoryStream stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
            DefaultHttpContext httpContext = new DefaultHttpContext()
            {
                Request = { Body = stream, ContentLength = stream.Length }
            };
            return httpContext;
        }
        #endregion
    }
}
