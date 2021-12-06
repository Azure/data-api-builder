using System.IO;
using System.Text;
using System.Text.Json;
using System.Data.Common;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Threading.Tasks;
using Azure.DataGateway.Services;
using Azure.DataGateway.Service.Tests.MsSql;
using Azure.DataGateway.Service.Resolvers;
using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using Npgsql;



namespace Azure.DataGateway.Service.Tests.PostgreSql {
    /// <summary>
    /// Base class providing common test fixture for GraphQL tests.
    /// </summary>
    [TestClass]
    public abstract class PostgreSqlTestBase {
        protected static IQueryExecutor _queryExecutor;
        protected static IQueryBuilder _queryBuilder;
        protected static IQueryEngine _queryEngine;
        protected static IMetadataStoreProvider _metaDataStoreProvider;
        protected static DatabaseInteractor _databaseInteractor;

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        protected static void IntializeTestFixture(TestContext context, string tableName){
            _metaDataStoreProvider = new FileMetadataStoreProvider("sql-config.json");

            _queryExecutor = new QueryExecutor<NpgsqlConnection>(PostgreSqlTestHelper.DataGatewayConfig);
            _queryBuilder = new PostgresQueryBuilder();
            _queryEngine = new SqlQueryEngine(_metaDataStoreProvider, _queryExecutor, _queryBuilder);
        
            _databaseInteractor = new DatabaseInteractor(_queryExecutor);
            using DbDataReader _ = _databaseInteractor.QueryExecutor.ExecuteQueryAsync(File.ReadAllText("books.sql"), parameters: null).Result;
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
        protected static async Task<string> GetDatabaseResultAsync(string queryText){
            JsonDocument sqlResult = JsonDocument.Parse( "{ }");
            using DbDataReader reader = _databaseInteractor.QueryExecutor.ExecuteQueryAsync(queryText, parameters: null).Result;

            if(await reader.ReadAsync()){
                sqlResult = JsonDocument.Parse(reader.GetString(0));
            }

            JsonElement sqlResultData = sqlResult.RootElement;

            return sqlResultData.ToString();
        }

        /// <summary>
        /// Converts strings to JSON objects and does a deep compare
        /// </summary>
        /// <param name="jsonString1"></param>
        /// <param name="jsonString2"></param>
        /// <returns>True if JSON objects are the same</returns>
        protected static bool JsonStringsDeepEqual(string jsonString1, string jsonString2) {
            return JToken.DeepEquals(JToken.Parse(jsonString1), JToken.Parse(jsonString2));
        }

    }
}