using System;
using System.Collections.Generic;
using System.Data.Common;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Threading.Tasks;
using System.Web;
using Azure.DataGateway.Auth;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Extensions;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MySqlConnector;
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
        protected static IMutationEngine _mutationEngine;
        protected static Mock<IAuthorizationService> _authorizationService;
        protected static Mock<IHttpContextAccessor> _httpContextAccessor;
        protected static DbExceptionParser _dbExceptionParser;
        protected static ISqlMetadataProvider _sqlMetadataProvider;
        protected static string _defaultSchemaName;
        protected static string _defaultSchemaVersion;
        protected static RuntimeConfigProvider _runtimeConfigProvider;
        protected static IAuthorizationResolver _authorizationResolver;
        private static WebApplicationFactory<Program> _application;
        protected static RuntimeConfig _runtimeConfig;
        protected static ILogger<ISqlMetadataProvider> _sqlMetadataLogger;

        protected static string DatabaseEngine { get; set; }
        protected static HttpClient HttpClient { get; private set; }

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run.
        /// This is a helper that is called from the non abstract versions of
        /// this class.
        /// </summary>
        /// <param name="context"></param>
        protected static async Task InitializeTestFixture(TestContext context)
        {
            RuntimeConfigPath configPath = TestHelper.GetRuntimeConfigPath($"{DatabaseEngine}");
            Mock<ILogger<RuntimeConfigProvider>> configProviderLogger = new();
            RuntimeConfigProvider.ConfigProviderLogger = configProviderLogger.Object;
            RuntimeConfigProvider.LoadRuntimeConfigValue(configPath, out _runtimeConfig);
            TestHelper.AddMissingEntitiesToConfig(_runtimeConfig);
            _runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(_runtimeConfig);

            SetUpSQLMetadataProvider();
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
                _sqlMetadataProvider,
                _httpContextAccessor.Object);
            _mutationEngine =
                new SqlMutationEngine(
                _queryEngine,
                _queryExecutor,
                _queryBuilder,
                _sqlMetadataProvider);
            await ResetDbStateAsync();
            await _sqlMetadataProvider.InitializeAsync();

            //Initialize the authorization resolver object
            _authorizationResolver = new AuthorizationResolver(_runtimeConfigProvider, _sqlMetadataProvider);

            _application = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureTestServices(services =>
                    {
                        services.AddSingleton(_runtimeConfigProvider);
                        services.AddSingleton(_queryEngine);
                        services.AddSingleton(_mutationEngine);
                        services.AddSingleton(_sqlMetadataProvider);
                    });
                });

            HttpClient = _application.CreateClient();
        }

        protected static void SetUpSQLMetadataProvider()
        {
            _sqlMetadataLogger = new Mock<ILogger<ISqlMetadataProvider>>().Object;

            switch (DatabaseEngine)
            {
                case TestCategory.POSTGRESQL:
                    _queryBuilder = new PostgresQueryBuilder();
                    _defaultSchemaName = "public";
                    _dbExceptionParser = new DbExceptionParser(_runtimeConfigProvider);
                    _queryExecutor = new QueryExecutor<NpgsqlConnection>(_runtimeConfigProvider, _dbExceptionParser);
                    _sqlMetadataProvider =
                        new PostgreSqlMetadataProvider(
                            _runtimeConfigProvider,
                            _queryExecutor,
                            _queryBuilder,
                            _sqlMetadataLogger);
                    break;
                case TestCategory.MSSQL:
                    _queryBuilder = new MsSqlQueryBuilder();
                    _defaultSchemaName = "dbo";
                    _dbExceptionParser = new DbExceptionParser(_runtimeConfigProvider);
                    _queryExecutor = new QueryExecutor<SqlConnection>(_runtimeConfigProvider, _dbExceptionParser);
                    _sqlMetadataProvider =
                        new MsSqlMetadataProvider(
                            _runtimeConfigProvider,
                            _queryExecutor, _queryBuilder,
                            _sqlMetadataLogger);
                    break;
                case TestCategory.MYSQL:
                    _queryBuilder = new MySqlQueryBuilder();
                    _defaultSchemaName = "mysql";
                    _dbExceptionParser = new DbExceptionParser(_runtimeConfigProvider);
                    _queryExecutor = new QueryExecutor<MySqlConnection>(_runtimeConfigProvider, _dbExceptionParser);
                    _sqlMetadataProvider =
                         new MySqlMetadataProvider(
                             _runtimeConfigProvider,
                             _queryExecutor,
                             _queryBuilder,
                             _sqlMetadataLogger);
                    break;
            }
        }

        protected static async Task ResetDbStateAsync()
        {
            using DbDataReader _ = await _queryExecutor.ExecuteQueryAsync(File.ReadAllText($"{DatabaseEngine}Books.sql"), parameters: null);
        }

        /// <summary>
        /// Constructs an http context with request consisting of the given query string and/or body data.
        /// </summary>
        /// <param name="queryStringUrl">query</param>
        /// <param name="bodyData">The data to be put in the request body e.g. GraphQLQuery</param>
        /// <param name="operation">The operation used to define the HttpContext HTTP Method</param>
        /// <returns>The http context with request consisting of the given query string (if any)
        /// and request body (if any) as a stream of utf-8 bytes.</returns>
        protected static DefaultHttpContext GetRequestHttpContext(
            string queryStringUrl = null,
            IHeaderDictionary headers = null,
            string bodyData = null,
            Operation operation = Operation.Find)
        {
            DefaultHttpContext httpContext;
            IFeatureCollection features = new FeatureCollection();
            //Add response features
            features.Set<IHttpResponseFeature>(new HttpResponseFeature());
            features.Set<IHttpResponseBodyFeature>(new StreamResponseBodyFeature(new MemoryStream()));

            if (headers is not null)
            {
                features.Set<IHttpRequestFeature>(new HttpRequestFeature { Headers = headers, Method = SqlTestHelper.OperationTypeToHTTPVerb(operation) });
                httpContext = new(features);
            }
            else
            {
                features.Set<IHttpRequestFeature>(new HttpRequestFeature { Method = SqlTestHelper.OperationTypeToHTTPVerb(operation) });
                httpContext = new(features);
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

            // Add identity object to the Mock context object.
            ClaimsIdentity identity = new(authenticationType: "Bearer");
            identity.AddClaim(new Claim(ClaimTypes.Role, "anonymous"));
            identity.AddClaim(new Claim(ClaimTypes.Role, "authenticated"));

            ClaimsPrincipal user = new(identity);
            httpContext.User = user;

            // Set the user role as authenticated to allow tests to execute with all privileges.
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = "authenticated";

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
            string path = "api",
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
                operationType,
                headers,
                requestBody
                );
            string baseUrl = UriHelper.GetEncodedUrl(controller.HttpContext.Request);
            if (expectedLocationHeader != null)
            {
                expectedLocationHeader =
                    baseUrl
                    + @"/" + expectedLocationHeader;
            }

            IActionResult actionResult = await SqlTestHelper.PerformApiTest(
                        controller,
                        path,
                        entity,
                        primaryKeyRoute,
                        operationType);

            // if an exception is expected we generate the correct error
            // The expected result should be a Query that confirms the result state
            // of the Operation performed for the test. However:
            // Initial DELETE request results in 204 no content, no exception thrown.
            // Subsequent DELETE requests result in 404, which result in an exception.
            string expected;
            if ((operationType is Operation.Delete ||
                 operationType is Operation.Upsert ||
                 operationType is Operation.UpsertIncremental ||
                 operationType is Operation.Update ||
                 operationType is Operation.UpdateIncremental)
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

                if (exception)
                {
                    expected = JsonSerializer.Serialize(RestController.ErrorResponse(
                        expectedSubStatusCode.ToString(),
                        expectedErrorMessage,
                        expectedStatusCode).Value,
                        options);
                }
                else
                {
                    string dbResult = await GetDatabaseResultAsync(sqlQuery);
                    // For FIND requests, null result signifies an empty result set
                    dbResult = (operationType is Operation.Find && dbResult is null) ? "[]" : dbResult;
                    expected = $"{{\"value\":{FormatExpectedValue(dbResult)}{ExpectedNextLinkIfAny(paginated, EncodeQueryString(baseUrl), $"{expectedAfterQueryString}")}}}";
                }
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
            Operation operation,
            IHeaderDictionary headers = null,
            string requestBody = null
            )
        {
            restController.ControllerContext.HttpContext =
                GetRequestHttpContext(
                    queryString,
                    headers,
                    bodyData: requestBody,
                    operation);

            // Set the mock context accessor's request same as the controller's request.
            _httpContextAccessor.Setup(x => x.HttpContext.Request).Returns(restController.ControllerContext.HttpContext.Request);

            //Set the mock context accessor's Items same as the controller's Items
            _httpContextAccessor.Setup(x => x.HttpContext.Items).Returns(restController.ControllerContext.HttpContext.Items);
        }

        /// <summary>
        /// Read the data property of the GraphQLController result
        /// </summary>
        /// <param name="graphQLQuery"></param>
        /// <param name="graphQLQueryName"></param>
        /// <param name="httpClient"></param>
        /// <param name="variables">Variables to be included in the GraphQL request. If null, no variables property is included in the request, to pass an empty object provide an empty dictionary</param>
        /// <returns>string in JSON format</returns>
        protected virtual async Task<string> GetGraphQLResultAsync(string graphQLQuery, string graphQLQueryName, HttpClient httpClient, Dictionary<string, object> variables = null, bool failOnErrors = true)
        {
            JsonElement graphQLResult = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, httpClient, variables);
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
        /// <param name="httpClient"></param>
        /// <param name="variables">Variables to be included in the GraphQL request. If null, no variables property is included in the request, to pass an empty object provide an empty dictionary</param>
        /// <returns>JsonElement</returns>
        protected static async Task<JsonElement> GetGraphQLControllerResultAsync(string query, string graphQLQueryName, HttpClient httpClient, Dictionary<string, object> variables = null)
        {
            object payload = variables == null ?
                new { query } :
                new
                {
                    query,
                    variables
                };
            string graphQLEndpoint = _application.Services.GetService<RuntimeConfigProvider>()
                .GetRuntimeConfiguration()
                .GraphQLGlobalSettings.Path;

            // todo: set the stuff that use to be on HttpContext
            httpClient.DefaultRequestHeaders.Add("X-MS-CLIENT-PRINCIPAL", "eyJ1c2VySWQiOiIyNTllM2JjNTE5NzU3Mzk3YTE2ZjdmMDBjMTI0NjQxYSIsInVzZXJSb2xlcyI6WyJhbm9ueW1vdXMiLCJhdXRoZW50aWNhdGVkIl0sImlkZW50aXR5UHJvdmlkZXIiOiJnaXRodWIiLCJ1c2VyRGV0YWlscyI6ImFhcm9ucG93ZWxsIiwiY2xhaW1zIjpbXX0=");
            HttpResponseMessage responseMessage = await httpClient.PostAsJsonAsync(graphQLEndpoint, payload);
            string body = await responseMessage.Content.ReadAsStringAsync();

            return JsonSerializer.Deserialize<JsonElement>(body);

            //httpClient.ControllerContext.HttpContext =
            //    GetRequestHttpContext(
            //        queryStringUrl: null,
            //        bodyData: graphqlQueryJson);

            //return await httpClient.PostAsync();
        }
    }
}
