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
            _metadataStoreProvider = new FileMetadataStoreProvider("sql-config.json");

            // Setup Database Components
            //
            _queryExecutor = new QueryExecutor<SqlConnection>(MsSqlTestHelper.DataGatewayConfig);
            _queryBuilder = new MsSqlQueryBuilder();
            _queryEngine = new SqlQueryEngine(_metadataStoreProvider, _queryExecutor, _queryBuilder);

            // Setup Integration DB Components
            //
            _databaseInteractor = new DatabaseInteractor(_queryExecutor);
            using DbDataReader _ = _databaseInteractor.QueryExecutor.ExecuteQueryAsync(File.ReadAllText("books.sql"), null).Result;
        }

        #region Helper Functions
        /// <summary>
        /// returns httpcontext with body consisting of the given data.
        /// </summary>
        /// <param name="data">The data to be put in the request body e.g. GraphQLQuery</param>
        /// <returns>The http context with given data as stream of utf-8 bytes.</returns>
        protected static DefaultHttpContext GetHttpContextWithBody(string data)
        {
            MemoryStream stream = new(Encoding.UTF8.GetBytes(data));
            DefaultHttpContext httpContext = new()
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
            DefaultHttpContext httpContext = new()
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
            _ = JsonDocument.Parse("{ }");
            using DbDataReader reader = await _databaseInteractor.QueryExecutor.ExecuteQueryAsync(queryText, parameters: null);

            JsonDocument sqlResult = JsonDocument.Parse(await SqlQueryEngine.GetJsonStringFromDbReader(reader));

            JsonElement sqlResultData = sqlResult.RootElement;

            return sqlResultData.ToString();
        }

        #endregion
    }
}
