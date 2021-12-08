using System.Data.Common;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
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
        private static readonly string _postgresqlTestConfigFile = "appsettings.PostgreSqlIntegrationTest.json";
        private static readonly string _mssqlTestConfigFile = "appsettings.MsSqlIntegrationTest.json";

        protected static IQueryExecutor _queryExecutor;
        protected static IQueryBuilder _queryBuilder;
        protected static IQueryEngine _queryEngine;
        protected static IMetadataStoreProvider _metadataStoreProvider;

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        protected static void InitializeTestFixture(TestContext context, string tableName, string testCategory)
        {
            _metadataStoreProvider = new FileMetadataStoreProvider("sql-config.json");

            switch (testCategory)
            {
                case TestCategory.POSTGRESSQL:
                    _queryExecutor = new QueryExecutor<NpgsqlConnection>(SqlTestHelper.LoadConfig(_postgresqlTestConfigFile));
                    _queryBuilder = new PostgresQueryBuilder();
                    _queryEngine = new SqlQueryEngine(_metadataStoreProvider, _queryExecutor, _queryBuilder);
                    break;
                case TestCategory.MSSQL:
                    _queryExecutor = new QueryExecutor<SqlConnection>(SqlTestHelper.LoadConfig(_mssqlTestConfigFile));
                    _queryBuilder = new MsSqlQueryBuilder();
                    _queryEngine = new SqlQueryEngine(_metadataStoreProvider, _queryExecutor, _queryBuilder);
                    break;
            }

            using DbDataReader _ = _queryExecutor.ExecuteQueryAsync(File.ReadAllText("books.sql"), parameters: null).Result;
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
