using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Models;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MySqlConnector;
using Newtonsoft.Json.Linq;
using Npgsql;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    /// <summary>
    /// Base class providing common test fixture for both REST and GraphQL tests.
    /// </summary>
    [TestClass]
    public abstract class SqlTestBase
    {
        private static string _testCategory;
        protected static IQueryExecutor _queryExecutor;
        protected static IQueryBuilder _queryBuilder;
        protected static IQueryEngine _queryEngine;
        protected static IMutationEngine _mutationEngine;
        protected static IMetadataStoreProvider _metadataStoreProvider;
        protected static Mock<IAuthorizationService> _authorizationService;
        protected static Mock<IHttpContextAccessor> _httpContextAccessor;

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run.
        /// This is a helper that is called from the non abstract versions of
        /// this class.
        /// </summary>
        /// <param name="context"></param>
        protected static async Task InitializeTestFixture(TestContext context, string tableName, string testCategory)
        {
            _testCategory = testCategory;

            IOptions<DataGatewayConfig> config = SqlTestHelper.LoadConfig($"{_testCategory}IntegrationTest");

            switch (_testCategory)
            {
                case TestCategory.POSTGRESQL:
                    _queryExecutor = new QueryExecutor<NpgsqlConnection>(config);
                    _queryBuilder = new PostgresQueryBuilder();
                    break;
                case TestCategory.MSSQL:
                    _queryExecutor = new QueryExecutor<SqlConnection>(config);
                    _queryBuilder = new MsSqlQueryBuilder();
                    break;
                case TestCategory.MYSQL:
                    _queryExecutor = new QueryExecutor<MySqlConnection>(config);
                    _queryBuilder = new MySqlQueryBuilder();
                    break;
            }

            // Setup AuthorizationService to always return Authorized.
            _authorizationService = new Mock<IAuthorizationService>();
            _authorizationService.Setup(x => x.AuthorizeAsync(
                It.IsAny<ClaimsPrincipal>(),
                It.IsAny<object>(),
                It.IsAny<IEnumerable<IAuthorizationRequirement>>()
                ).Result).Returns(AuthorizationResult.Success);

            // Setup Mock HttpContextAccess to return user as required when calling AuthorizationService.AuthorizeAsync
            _httpContextAccessor = new Mock<IHttpContextAccessor>();
            _httpContextAccessor.Setup(x => x.HttpContext.User).Returns(new ClaimsPrincipal());

            _metadataStoreProvider = new FileMetadataStoreProvider("sql-config.json");
            _queryEngine = new SqlQueryEngine(_metadataStoreProvider, _queryExecutor, _queryBuilder);
            _mutationEngine = new SqlMutationEngine(_queryEngine, _metadataStoreProvider, _queryExecutor, _queryBuilder);

            await ResetDbStateAsync();
        }

        protected static async Task ResetDbStateAsync()
        {
            using DbDataReader _ = await _queryExecutor.ExecuteQueryAsync(File.ReadAllText($"{_testCategory}Books.sql"), parameters: null);
        }

        /// <summary>
        /// Constructs an http context with request consisting of the given query string and/or body data.
        /// </summary>
        /// <param name="queryStringUrl">query</param>
        /// <param name="bodyData">The data to be put in the request body e.g. GraphQLQuery</param>
        /// <returns>The http context with request consisting of the given query string (if any)
        /// and request body (if any) as a stream of utf-8 bytes.</returns>
        protected static DefaultHttpContext GetRequestHttpContext(
            string queryStringUrl = null,
            string bodyData = null)
        {
            DefaultHttpContext httpContext = new();

            if (!string.IsNullOrEmpty(queryStringUrl))
            {
                httpContext.Request.QueryString = new(queryStringUrl);
            }

            if (!string.IsNullOrEmpty(bodyData))
            {
                MemoryStream stream = new(Encoding.UTF8.GetBytes(bodyData));
                httpContext.Request.Body = stream;
                httpContext.Request.ContentLength = stream.Length;
            }

            return httpContext;
        }

        /// <summary>
        /// Sends raw SQL query to database engine to retrieve expected result in JSON format.
        /// </summary>
        /// <param name="queryText">raw database query, typically a SELECT</param>
        /// <returns>string in JSON format</returns>
        protected static async Task<string> GetDatabaseResultAsync(
            string queryText,
            Operation operationType = Operation.Find)
        {
            string result;

            using DbDataReader reader = await _queryExecutor.ExecuteQueryAsync(queryText, parameters: null);

            // An empty result will cause an error with the json parser
            if (!reader.HasRows)
            {
                // Find and Delete queries have empty result sets.
                // Delete operation will return number of records affected.
                result = null;
            }
            else
            {
                using JsonDocument sqlResult = JsonDocument.Parse(await SqlQueryEngine.GetJsonStringFromDbReader(reader));
                result = sqlResult.RootElement.ToString();
            }

            return result;
        }

        /// <summary>
        /// Does the setup required to perform a test of the REST Api for both
        /// MsSql and Postgress. Shared setup logic eliminates some code duplication
        /// between MsSql and Postgress.
        /// </summary>
        /// <param name="primaryKeyRoute">string represents the primary key route</param>
        /// <param name="queryString">string represents the query string provided in URL</param>
        /// <param name="entity">string represents the name of the entity</param>
        /// <param name="sqlQuery">string represents the query to be executed</param>
        /// <param name="controller">string represents the rest controller</param>
        /// <param name="operationType">The operation type to be tested.</param>
        /// <param name="requestBody">string represents JSON data used in mutation operations</param>
        /// <param name="exception">bool represents if we expect an exception</param>
        /// <param name="expectedErrorMessage">string represents the error message in the JsonResponse</param>
        /// <param name="expectedStatusCode">int represents the returned http status code</param>
        /// <param name="expectedSubStatusCode">enum represents the returned sub status code</param>
        /// <param name="expectedLocationHeader">The expected location header in the response (if any)</param>
        /// <returns></returns>
        protected static async Task SetupAndRunRestApiTest(
            string primaryKeyRoute,
            string queryString,
            string entity,
            string sqlQuery,
            RestController controller,
            Operation operationType = Operation.Find,
            string requestBody = null,
            bool exception = false,
            string expectedErrorMessage = "",
            HttpStatusCode expectedStatusCode = HttpStatusCode.OK,
            string expectedSubStatusCode = "BadRequest",
            string expectedLocationHeader = null)
        {
            ConfigureRestController(
                controller,
                queryString,
                requestBody);

            if (expectedLocationHeader != null)
            {
                expectedLocationHeader =
                    UriHelper.GetEncodedUrl(controller.HttpContext.Request)
                    + @"/" + expectedLocationHeader;
            }

            IActionResult actionResult = await SqlTestHelper.PerformApiTest(
                        controller,
                        entity,
                        primaryKeyRoute,
                        operationType);

            // if an exception is expected we generate the correct error
            // The expected result should be a Query that confirms the result state
            // of the Operation performed for the test. However:
            // Initial DELETE request results in 204 no content, no exception thrown.
            // Subsequent DELETE requests result in 404, which result in an exception.
            string expected;
            if ((operationType == Operation.Delete || operationType == Operation.Upsert)
                && actionResult is NoContentResult)
            {
                expected = null;
            }
            else
            {
                expected = exception ?
                    RestController.ErrorResponse(
                        expectedSubStatusCode.ToString(),
                        expectedErrorMessage, expectedStatusCode).Value.ToString() :
                    $"{{ value = {await GetDatabaseResultAsync(sqlQuery)} }}";
            }

            SqlTestHelper.VerifyResult(
                actionResult,
                expected,
                expectedStatusCode,
                expectedLocationHeader,
                !exception);
        }

        /// <summary>
        /// Add HttpContext with query and body(if any) to the RestController
        /// </summary>
        /// <param name="restController">The controller to configure.</param>
        /// <param name="queryString">The query string in the url.</param>
        /// <param name="requestBody">The data to be put in the request body.</param>
        protected static void ConfigureRestController(
            RestController restController,
            string queryString,
            string requestBody = null)
        {
            restController.ControllerContext.HttpContext =
                GetRequestHttpContext(
                    queryString,
                    bodyData: requestBody);

            // Set the mock context accessor's request same as the controller's request.
            _httpContextAccessor.Setup(x => x.HttpContext.Request).Returns(restController.ControllerContext.HttpContext.Request);
        }

        /// <summary>
        /// Read the data property of the GraphQLController result
        /// </summary>
        /// <param name="graphQLQuery"></param>
        /// <param name="graphQLQueryName"></param>
        /// <param name="graphQLController"></param>
        /// <returns>string in JSON format</returns>
        protected static async Task<string> GetGraphQLResultAsync(string graphQLQuery, string graphQLQueryName, GraphQLController graphQLController)
        {
            JsonElement graphQLResult = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, graphQLController);
            Console.WriteLine(graphQLResult.ToString());
            JsonElement graphQLResultData = graphQLResult.GetProperty("data").GetProperty(graphQLQueryName);

            // JsonElement.ToString() prints null values as empty strings instead of "null"
            return graphQLResultData.GetRawText();
        }

        /// <summary>
        /// Sends graphQL query through graphQL service, consisting of gql engine processing (resolvers, object serialization)
        /// returning the result as a JsonDocument
        /// </summary>
        /// <param name="graphQLQuery"></param>
        /// <param name="graphQLQueryName"></param>
        /// <param name="graphQLController"></param>
        /// <returns>JsonDocument</returns>
        protected static async Task<JsonElement> GetGraphQLControllerResultAsync(string graphQLQuery, string graphQLQueryName, GraphQLController graphQLController)
        {
            string graphqlQueryJson = JObject.FromObject(new
            {
                query = graphQLQuery
            }).ToString();

            Console.WriteLine(graphqlQueryJson);

            graphQLController.ControllerContext.HttpContext =
                GetRequestHttpContext(
                    queryStringUrl: null,
                    bodyData: graphqlQueryJson);

            return await graphQLController.PostAsync();
        }
    }
}
