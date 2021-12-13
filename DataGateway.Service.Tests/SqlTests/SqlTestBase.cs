using System.Data.Common;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.configurations;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Npgsql;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    /// <summary>
    /// Base class providing common test fixture for both REST and GraphQL tests.
    /// </summary>
    [TestClass]
    public abstract class SqlTestBase
    {
        protected static IQueryExecutor _queryExecutor;
        protected static IQueryBuilder _queryBuilder;
        protected static IQueryEngine _queryEngine;
        protected static IMetadataStoreProvider _metadataStoreProvider;

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run.
        /// This is a helper that is called from the non abstract versions of
        /// this class.
        /// </summary>
        /// <param name="context"></param>
        protected static async Task InitializeTestFixture(TestContext context, string tableName, string testCategory)
        {
            IOptions<DataGatewayConfig> config = SqlTestHelper.LoadConfig($"{testCategory}IntegrationTest");

            switch (testCategory)
            {
                case TestCategory.POSTGRESQL:
                    _queryExecutor = new QueryExecutor<NpgsqlConnection>(config);
                    _queryBuilder = new PostgresQueryBuilder();
                    break;
                case TestCategory.MSSQL:
                    _queryExecutor = new QueryExecutor<SqlConnection>(config);
                    _queryBuilder = new MsSqlQueryBuilder();
                    break;
            }

            _metadataStoreProvider = new FileMetadataStoreProvider("sql-config.json");
            _queryEngine = new SqlQueryEngine(_metadataStoreProvider, _queryExecutor, _queryBuilder);

            using DbDataReader _ = await _queryExecutor.ExecuteQueryAsync(File.ReadAllText("books.sql"), parameters: null);
        }

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
        protected static async Task<string> GetDatabaseResultAsync(string queryText)
        {
            using DbDataReader reader = await _queryExecutor.ExecuteQueryAsync(queryText, parameters: null);

            // an empty result will cause an error with the json parser
            if (!reader.HasRows)
            {
                throw new System.Exception("No rows to read from database result");
            }

            using JsonDocument sqlResult = JsonDocument.Parse(await SqlQueryEngine.GetJsonStringFromDbReader(reader));

            JsonElement sqlResultData = sqlResult.RootElement;

            return sqlResultData.ToString();
        }

        ///<summary>
        /// Add HttpContext with query to the RestController
        ///</summary>
        protected static void ConfigureRestController(RestController restController, string queryString)
        {
            restController.ControllerContext.HttpContext = GetHttpContextWithQueryString(queryString);
        }
    }
}
