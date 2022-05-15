using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;
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
        protected static Mock<IAuthorizationService> _authorizationService;
        protected static Mock<IHttpContextAccessor> _httpContextAccessor;
        protected static DbExceptionParserBase _dbExceptionParser;
        protected static ISqlMetadataProvider _sqlMetadataProvider;
        protected static string _defaultSchemaName;
        protected static string _defaultSchemaVersion;
        protected static IOptionsMonitor<RuntimeConfigPath> _runtimeConfigPath;

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run.
        /// This is a helper that is called from the non abstract versions of
        /// this class.
        /// </summary>
        /// <param name="context"></param>
        protected static async Task InitializeTestFixture(TestContext context, string testCategory)
        {
            _testCategory = testCategory;

            _runtimeConfigPath = SqlTestHelper.LoadConfig($"{_testCategory}");
            switch (_testCategory)
            {
                case TestCategory.POSTGRESQL:
                    _queryBuilder = new PostgresQueryBuilder();
                    _defaultSchemaName = "public";
                    _dbExceptionParser = new PostgresDbExceptionParser();
                    _queryExecutor = new QueryExecutor<NpgsqlConnection>(_runtimeConfigPath, _dbExceptionParser);
                    _sqlMetadataProvider =
                        new PostgreSqlMetadataProvider(
                            _runtimeConfigPath,
                            _queryExecutor,
                            _queryBuilder);
                    break;
                case TestCategory.MSSQL:
                    _queryBuilder = new MsSqlQueryBuilder();
                    _defaultSchemaName = "dbo";
                    _dbExceptionParser = new DbExceptionParserBase();
                    _queryExecutor = new QueryExecutor<SqlConnection>(_runtimeConfigPath, _dbExceptionParser);
                    _sqlMetadataProvider = new MsSqlMetadataProvider(
                        _runtimeConfigPath,
                        _queryExecutor, _queryBuilder);
                    break;
                case TestCategory.MYSQL:
                    _queryBuilder = new MySqlQueryBuilder();
                    _defaultSchemaName = "mysql";
                    _dbExceptionParser = new MySqlDbExceptionParser();
                    _queryExecutor = new QueryExecutor<MySqlConnection>(_runtimeConfigPath, _dbExceptionParser);
                    _sqlMetadataProvider =
                         new MySqlMetadataProvider(
                             _runtimeConfigPath,
                             _queryExecutor,
                             _queryBuilder);
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

            _queryEngine = new SqlQueryEngine(
                _queryExecutor,
                _queryBuilder,
                _sqlMetadataProvider);
            _mutationEngine =
                new SqlMutationEngine(
                _queryEngine,
                _queryExecutor,
                _queryBuilder,
                _sqlMetadataProvider);
            await ResetDbStateAsync();
            await _sqlMetadataProvider.InitializeAsync();
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
            IHeaderDictionary headers = null,
            string bodyData = null)
        {
            DefaultHttpContext httpContext;
            if (headers is not null)
            {
                IFeatureCollection features = new FeatureCollection();
                features.Set<IHttpRequestFeature>(new HttpRequestFeature { Headers = headers });
                features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));
                // set Response StatusCode here to avoid null reference when returning exception in test with supplied headers
                features.Set<IHttpResponseFeature>(new HttpResponseFeature { StatusCode = 200 });
                httpContext = new(features);
            }
            else
            {
                httpContext = new();
            }

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
                using JsonDocument sqlResult = JsonDocument.Parse(await SqlQueryEngine.GetJsonStringFromDbReader(reader, _queryExecutor));
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
            IHeaderDictionary headers = null,
            string requestBody = null,
            bool exception = false,
            string expectedErrorMessage = "",
            HttpStatusCode expectedStatusCode = HttpStatusCode.OK,
            string expectedSubStatusCode = "BadRequest",
            string expectedLocationHeader = null,
            string expectedAfterQueryString = "",
            bool paginated = false,
            int verifyNumRecords = -1)
        {
            ConfigureRestController(
                controller,
                queryString,
                headers,
                requestBody);
            string baseUrl = UriHelper.GetEncodedUrl(controller.HttpContext.Request);
            if (expectedLocationHeader != null)
            {
                expectedLocationHeader =
                    baseUrl
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
            if ((operationType == Operation.Delete ||
                 operationType == Operation.Upsert ||
                 operationType == Operation.UpsertIncremental ||
                 operationType == Operation.Update ||
                 operationType == Operation.UpdateIncremental)
                && actionResult is NoContentResult)
            {
                expected = null;
            }
            else
            {
                JsonSerializerOptions options = new()
                {
                    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
                };
                expected = exception ?
                    JsonSerializer.Serialize(RestController.ErrorResponse(
                        expectedSubStatusCode.ToString(),
                        expectedErrorMessage,
                        expectedStatusCode).Value,
                        options) :
                    $"{{\"value\":{FormatExpectedValue(await GetDatabaseResultAsync(sqlQuery))}{ExpectedNextLinkIfAny(paginated, EncodeQueryString(baseUrl), $"{expectedAfterQueryString}")}}}";
            }

            SqlTestHelper.VerifyResult(
                actionResult,
                expected,
                expectedStatusCode,
                expectedLocationHeader,
                !exception,
                verifyNumRecords);
        }

        /// <summary>
        /// Helper function encodes the url with query string into the correct
        /// format. We utilize the toString() of the HttpValueCollection
        /// which is used by the NameValueCollection returned from
        /// ParseQueryString to avoid writing this ourselves.
        /// </summary>
        /// <param name="fullUrl">Url to be encoded as query string.</param>
        /// <returns>query string encoded url.</returns>
        private static string EncodeQueryString(string fullUrl)
        {
            return HttpUtility.ParseQueryString(fullUrl).ToString();
        }

        /// <summary>
        /// Helper function formats the expected value to match actual response format.
        /// </summary>
        /// <param name="expected">The expected response.</param>
        /// <returns>Formatted expected response.</returns>
        private static string FormatExpectedValue(string expected)
        {
            return string.IsNullOrWhiteSpace(expected) ? string.Empty : (!Equals(expected[0], '[')) ? $"[{expected}]" : expected;
        }

        /// <summary>
        /// Helper function will return the expected NextLink if one is
        /// required, and an empty string otherwise.
        /// </summary>
        /// <param name="paginated">Bool representing if the nextLink is needed.</param>
        /// <param name="baseUrl">The base Url.</param>
        /// <param name="queryString">The query string to add to the url.</param>
        /// <returns></returns>
        private static string ExpectedNextLinkIfAny(bool paginated, string baseUrl, string queryString)
        {
            return paginated ? $",\"nextLink\":\"{baseUrl}{queryString}\"" : string.Empty;
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
            IHeaderDictionary headers = null,
            string requestBody = null)
        {
            restController.ControllerContext.HttpContext =
                GetRequestHttpContext(
                    queryString,
                    headers,
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
        /// <param name="variables">Variables to be included in the GraphQL request. If null, no variables property is included in the request, to pass an empty object provide an empty dictionary</param>
        /// <returns>string in JSON format</returns>
        protected virtual async Task<string> GetGraphQLResultAsync(string graphQLQuery, string graphQLQueryName, GraphQLController graphQLController, Dictionary<string, object> variables = null, bool failOnErrors = true)
        {
            JsonElement graphQLResult = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, graphQLController, variables);
            Console.WriteLine(graphQLResult.ToString());

            if (failOnErrors && graphQLResult.TryGetProperty("errors", out JsonElement errors))
            {
                Assert.Fail(errors.GetRawText());
            }

            JsonElement graphQLResultData = graphQLResult.GetProperty("data").GetProperty(graphQLQueryName);

            // JsonElement.ToString() prints null values as empty strings instead of "null"
            return graphQLResultData.GetRawText();
        }

        /// <summary>
        /// Sends graphQL query through graphQL service, consisting of gql engine processing (resolvers, object serialization)
        /// returning the result as a JsonElement - the root of the JsonDocument.
        /// </summary>
        /// <param name="query"></param>
        /// <param name="graphQLQueryName"></param>
        /// <param name="graphQLController"></param>
        /// <param name="variables">Variables to be included in the GraphQL request. If null, no variables property is included in the request, to pass an empty object provide an empty dictionary</param>
        /// <returns>JsonElement</returns>
        protected static async Task<JsonElement> GetGraphQLControllerResultAsync(string query, string graphQLQueryName, GraphQLController graphQLController, Dictionary<string, object> variables = null)
        {
            string graphqlQueryJson = variables == null ?
                JObject.FromObject(new { query }).ToString() :
                JObject.FromObject(new
                {
                    query,
                    variables
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
