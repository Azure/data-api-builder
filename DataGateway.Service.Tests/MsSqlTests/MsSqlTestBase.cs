using System.Data.Common;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.MsSql
{
    /// <summary>
    /// Base class providing common test fixture for both REST and GraphQL tests.
    /// </summary>
    [TestClass]
    public abstract class MsSqlTestBase
    {
        protected static IQueryExecutor _queryExecutor;
        protected static IQueryBuilder _queryBuilder;
        protected static IQueryEngine _queryEngine;
        protected static IMetadataStoreProvider _metadataStoreProvider;
        protected static DatabaseInteractor _databaseInteractor;

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator. 
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        protected static void InitializeTestFixture(TestContext context, string tableName)
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
            CreateTable(tableName);
            InsertData(tableName);
        }

        /// <summary>
        /// Cleans up querying table used for Tests in this class. Only to be run once at
        /// conclusion of test run, as defined by MSTest decorator.
        /// </summary>
        [ClassCleanup]
        protected static void CleanupTestFixture(string tableName)
        {
            _databaseInteractor.DropTable(tableName);
        }

        #region Helper Functions
        /// <summary>
        /// Creates the given table.
        /// </summary>
        /// <param name="tableName">The table name.</param>
        private static void CreateTable(string tableName)
        {
            _databaseInteractor.CreateTable(tableName, "id int, name varchar(20), type varchar(20), homePlanet int, primaryFunction varchar(20)");
        }

        /// <summary>
        /// Inserts some default data into the table.
        /// </summary>
        private static void InsertData(string tableName)
        {
            _databaseInteractor.InsertData(tableName, "'1', 'Mace', 'Jedi','1','Master'");
            _databaseInteractor.InsertData(tableName, "'2', 'Plo Koon', 'Jedi','2','Master'");
            _databaseInteractor.InsertData(tableName, "'3', 'Yoda', 'Jedi','3','Master'");
        }

        /// <summary>
        /// returns httpcontext with body consisting of the given data.
        /// </summary>
        /// <param name="data">The data to be put in the request body e.g. GraphQLQuery</param>
        /// <returns>The http context with given data as stream of utf-8 bytes.</returns>
        protected static DefaultHttpContext GetHttpContextWithBody(string data)
        {
            var stream = new MemoryStream(Encoding.UTF8.GetBytes(data));
            var httpContext = new DefaultHttpContext()
            {
                Request = { Body = stream, ContentLength = stream.Length }
            };
            return httpContext;
        }

        /// <summary>
        /// Constructs an http context with request consisting of the given query string.
        /// </summary>
        /// <param name="queryStringUrl">query</param>
        /// <returns>The http context with request consisting of the given query string.</returns>
        protected static DefaultHttpContext GetHttpContextWithQueryString(string queryStringUrl)
        {
            var httpContext = new DefaultHttpContext()
            {
                Request = { QueryString = new(queryStringUrl) }
            };

            return httpContext;
        }

        /// <summary>
        /// Sends raw SQL query to database engine to retrieve expected result in JSON format.
        /// </summary>
        /// <param name="queryText">raw database query</param>
        /// <returns>string in JSON format</returns>
        public static async Task<string> GetDatabaseResultAsync(string queryText)
        {
            var sqlResult = JsonDocument.Parse("{ }");
            using DbDataReader reader = _databaseInteractor.QueryExecutor.ExecuteQueryAsync(queryText, parameters: null).Result;

            if (await reader.ReadAsync())
            {
                sqlResult = JsonDocument.Parse(reader.GetString(0));
            }

            JsonElement sqlResultData = sqlResult.RootElement;

            return sqlResultData.ToString();
        }

        #endregion
    }
}
