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
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MySqlConnector;
using Npgsql;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests
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
        protected static ILogger<SqlMutationEngine> _mutationEngineLogger;
        protected static ILogger<SqlQueryEngine> _queryEngineLogger;
        protected static ILogger<RestController> _restControllerLogger;
        protected const string MSSQL_DEFAULT_DB_NAME = "master";

        protected static string DatabaseName { get; set; }
        protected static string DatabaseEngine { get; set; }
        protected static HttpClient HttpClient { get; private set; }

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run.
        /// This is a helper that is called from the non abstract versions of
        /// this class.
        /// </summary>
        /// <param name="context"></param>
        /// <param name="customQueries">Test specific queries to be executed on database.</param>
        /// <param name="customEntities">Test specific entities to be added to database.</param>
        /// <returns></returns>
        protected static async Task InitializeTestFixture(TestContext context, List<string> customQueries = null,
            List<string[]> customEntities = null)
        {
            _queryEngineLogger = new Mock<ILogger<SqlQueryEngine>>().Object;
            _mutationEngineLogger = new Mock<ILogger<SqlMutationEngine>>().Object;
            _restControllerLogger = new Mock<ILogger<RestController>>().Object;

            RuntimeConfigPath configPath = TestHelper.GetRuntimeConfigPath($"{DatabaseEngine}");
            Mock<ILogger<RuntimeConfigProvider>> configProviderLogger = new();
            RuntimeConfigProvider.ConfigProviderLogger = configProviderLogger.Object;
            RuntimeConfigProvider.LoadRuntimeConfigValue(configPath, out _runtimeConfig);

            // Add magazines entity to the 
            if (TestCategory.MYSQL.Equals(DatabaseEngine))
            {
                TestHelper.AddMissingEntitiesToConfig(_runtimeConfig, "magazine", "magazines");
            }
            else
            {
                TestHelper.AddMissingEntitiesToConfig(_runtimeConfig, "magazine", "foo.magazines");
            }

            // Add custom entities for the test, if any.
            AddCustomEntities(customEntities);

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

            await ResetDbStateAsync();

            // Execute additional queries, if any.
            await ExecuteQueriesOnDbAsync(customQueries);

            await _sqlMetadataProvider.InitializeAsync();

            // sets the database name using the connection string
            SetDatabaseNameFromConnectionString(_runtimeConfig.ConnectionString);

            //Initialize the authorization resolver object
            _authorizationResolver = new AuthorizationResolver(_runtimeConfigProvider, _sqlMetadataProvider);

            _queryEngine = new SqlQueryEngine(
                _queryExecutor,
                _queryBuilder,
                _sqlMetadataProvider,
                _httpContextAccessor.Object,
                _authorizationResolver,
                _queryEngineLogger);
            _mutationEngine =
                new SqlMutationEngine(
                _queryEngine,
                _queryExecutor,
                _queryBuilder,
                _sqlMetadataProvider,
                _authorizationResolver,
                _httpContextAccessor.Object,
                _mutationEngineLogger);

            _application = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureTestServices(services =>
                    {
                        services.AddHttpContextAccessor();
                        services.AddSingleton(_runtimeConfigProvider);
                        services.AddSingleton<IQueryEngine>(implementationFactory: (serviceProvider) =>
                        {
                            return new SqlQueryEngine(
                                _queryExecutor,
                                _queryBuilder,
                                _sqlMetadataProvider,
                                ActivatorUtilities.GetServiceOrCreateInstance<IHttpContextAccessor>(serviceProvider),
                                _authorizationResolver,
                                _queryEngineLogger
                                );
                        });
                        services.AddSingleton<IMutationEngine>(implementationFactory: (serviceProvider) =>
                        {
                            return new SqlMutationEngine(
                                    _queryEngine,
                                    _queryExecutor,
                                    _queryBuilder,
                                    _sqlMetadataProvider,
                                    _authorizationResolver,
                                    ActivatorUtilities.GetServiceOrCreateInstance<IHttpContextAccessor>(serviceProvider),
                                    _mutationEngineLogger);
                        });
                        services.AddSingleton(_sqlMetadataProvider);
                        services.AddSingleton(_authorizationResolver);
                    });
                });

            HttpClient = _application.CreateClient();
        }

        /// <summary>
        /// Helper method to add test specific entities to the entity mapping.
        /// </summary>
        /// <param name="customEntities">List of test specific entities.</param>
        private static void AddCustomEntities(List<string[]> customEntities)
        {
            if (customEntities is not null)
            {
                foreach (string[] customEntity in customEntities)
                {
                    string objectKey = customEntity[0];
                    string objectName = customEntity[1];
                    TestHelper.AddMissingEntitiesToConfig(_runtimeConfig, objectKey, objectName);
                }
            }
        }

        /// <summary>
        /// Helper method to execute all the additional queries for a test on the database.
        /// </summary>
        /// <param name="customQueries"></param>
        /// <returns></returns>
        private static async Task ExecuteQueriesOnDbAsync(List<string> customQueries)
        {
            if (customQueries is not null)
            {
                foreach (string query in customQueries)
                {
                    await _queryExecutor.ExecuteQueryAsync(query, parameters: null);
                }
            }
        }

        /// <summary>
        /// Sets the database name based on the provided connection string.
        /// If connection string has no database set, we set the default based on the db type.
        /// </summary>
        /// <param name="connectionString">connection string containing the database name.</param>
        private static void SetDatabaseNameFromConnectionString(string connectionString)
        {
            switch (DatabaseEngine)
            {
                case TestCategory.MSSQL:
                    // use master as default name for MsSql
                    string sqlDbName = new SqlConnectionStringBuilder(connectionString).InitialCatalog;
                    DatabaseName = !string.IsNullOrEmpty(sqlDbName) ? sqlDbName : MSSQL_DEFAULT_DB_NAME;
                    break;
                case TestCategory.POSTGRESQL:
                    // use username as default name for PostgreSql, if no username use empty string
                    NpgsqlConnectionStringBuilder npgBuilder = new(connectionString);
                    DatabaseName = !string.IsNullOrEmpty(npgBuilder.Database) ? npgBuilder.Database :
                        !string.IsNullOrEmpty(npgBuilder.Username) ? npgBuilder.Username : string.Empty;
                    break;
                case TestCategory.MYSQL:
                    // no default name needed for MySql, if db name doesn't exist use empty string
                    string mySqlDbName = new MySqlConnectionStringBuilder(connectionString).Database;
                    DatabaseName = !string.IsNullOrEmpty(mySqlDbName) ? mySqlDbName : string.Empty;
                    break;
            }
        }

        protected static void SetUpSQLMetadataProvider()
        {
            _sqlMetadataLogger = new Mock<ILogger<ISqlMetadataProvider>>().Object;

            switch (DatabaseEngine)
            {
                case TestCategory.POSTGRESQL:
                    Mock<ILogger<QueryExecutor<NpgsqlConnection>>> pgQueryExecutorLogger = new();
                    _queryBuilder = new PostgresQueryBuilder();
                    _defaultSchemaName = "public";
                    _dbExceptionParser = new DbExceptionParser(_runtimeConfigProvider);
                    _queryExecutor = new QueryExecutor<NpgsqlConnection>(
                        _runtimeConfigProvider,
                        _dbExceptionParser,
                        pgQueryExecutorLogger.Object);
                    _sqlMetadataProvider =
                        new PostgreSqlMetadataProvider(
                            _runtimeConfigProvider,
                            _queryExecutor,
                            _queryBuilder,
                            _sqlMetadataLogger);
                    break;
                case TestCategory.MSSQL:
                    Mock<ILogger<QueryExecutor<SqlConnection>>> msSqlQueryExecutorLogger = new();
                    _queryBuilder = new MsSqlQueryBuilder();
                    _defaultSchemaName = "dbo";
                    _dbExceptionParser = new DbExceptionParser(_runtimeConfigProvider);
                    _queryExecutor = new QueryExecutor<SqlConnection>(
                        _runtimeConfigProvider,
                        _dbExceptionParser,
                        msSqlQueryExecutorLogger.Object);
                    _sqlMetadataProvider =
                        new MsSqlMetadataProvider(
                            _runtimeConfigProvider,
                            _queryExecutor, _queryBuilder,
                            _sqlMetadataLogger);
                    break;
                case TestCategory.MYSQL:
                    Mock<ILogger<QueryExecutor<MySqlConnection>>> mySqlQueryExecutorLogger = new();
                    _queryBuilder = new MySqlQueryBuilder();
                    _defaultSchemaName = "mysql";
                    _dbExceptionParser = new DbExceptionParser(_runtimeConfigProvider);
                    _queryExecutor = new QueryExecutor<MySqlConnection>(
                        _runtimeConfigProvider,
                        _dbExceptionParser,
                        mySqlQueryExecutorLogger.Object);
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
            Operation operation = Operation.Read)
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
            Operation operationType = Operation.Read)
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
            Operation operationType = Operation.Read,
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
            string restEndPoint = path + "/" + entity;

            if (!string.IsNullOrEmpty(primaryKeyRoute))
            {
                restEndPoint = restEndPoint + "/" + primaryKeyRoute;
            }

            if (!string.IsNullOrEmpty(queryString))
            {
                restEndPoint = restEndPoint + queryString;
            }

            JsonSerializerOptions options = new()
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            HttpMethod httpMethod = GetHttpMethodFromOperation(operationType);
            HttpRequestMessage request = new(httpMethod, restEndPoint)
            {
                Content = JsonContent.Create(requestBody, options: options)
            };

            if (headers is not null)
            {
                foreach ((string key, StringValues value) in headers)
                {
                    request.Headers.Add(key, value.ToString());
                }
            }

            HttpResponseMessage response = await HttpClient.SendAsync(request);

            string responseBody = await response.Content.ReadAsStringAsync();

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
                 && response.StatusCode.Equals(HttpStatusCode.NoContent)
                )
            {
                expected = string.Empty;
            }
            else
            {
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
                    string baseUrl = HttpClient.BaseAddress.ToString() + path + "/" + entity;
                    if (!string.IsNullOrEmpty(queryString))
                    {
                        baseUrl = baseUrl + "?" + HttpUtility.ParseQueryString(queryString).ToString();
                    }

                    string dbResult = await GetDatabaseResultAsync(sqlQuery);
                    // For FIND requests, null result signifies an empty result set
                    dbResult = (operationType is Operation.Read && dbResult is null) ? "[]" : dbResult;
                    expected = $"{{\"value\":{FormatExpectedValue(dbResult)}{ExpectedNextLinkIfAny(paginated, baseUrl, $"{expectedAfterQueryString}")}}}";
                }
            }

            if (!exception)
            {
                Assert.IsTrue(SqlTestHelper.JsonStringsDeepEqual(expected, responseBody));
                Assert.AreEqual(expectedStatusCode, response.StatusCode);
                if (operationType == Operation.Insert)
                {
                    string responseUri = (response.Headers.Location.OriginalString);
                    string requestUri = request.RequestUri.OriginalString;
                    string actualLocation = responseUri.Substring(
                        responseUri.IndexOf(requestUri + "/") + requestUri.Length + 1);
                    Assert.AreEqual(expectedLocationHeader, actualLocation);
                }
            }
            else
            {
                responseBody = Regex.Replace(responseBody, @"\\u0022", @"\\""");
                responseBody = Regex.Unescape(responseBody);
                Assert.AreEqual(expected, responseBody, ignoreCase: true);
            }
        }

        private static HttpMethod GetHttpMethodFromOperation(Operation operationType)
        {
            switch (operationType)
            {
                case Operation.Read:
                    return HttpMethod.Get;
                case Operation.Insert:
                    return HttpMethod.Post;
                case Operation.Delete:
                    return HttpMethod.Delete;
                case Operation.Upsert:
                    return HttpMethod.Put;
                case Operation.UpsertIncremental:
                    return HttpMethod.Patch;
                default:
                    throw new DataApiBuilderException(
                        message: "Operation not supported for the request.",
                        statusCode: HttpStatusCode.NotImplemented,
                        subStatusCode: DataApiBuilderException.SubStatusCodes.NotSupported);
            }
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
        /// <param name="query"></param>
        /// <param name="queryName"></param>
        /// <param name="httpClient"></param>
        /// <param name="variables">Variables to be included in the GraphQL request. If null, no variables property is included in the request, to pass an empty object provide an empty dictionary</param>
        /// <returns>string in JSON format</returns>
        protected virtual async Task<JsonElement> ExecuteGraphQLRequestAsync(
            string query,
            string queryName,
            bool isAuthenticated,
            Dictionary<string, object> variables = null,
            string clientRoleHeader = null)
        {
            RuntimeConfigProvider configProvider = _application.Services.GetService<RuntimeConfigProvider>();
            return await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                HttpClient,
                configProvider,
                queryName,
                query,
                variables,
                isAuthenticated ? AuthTestHelper.CreateStaticWebAppsEasyAuthToken(specificRole: clientRoleHeader) : null,
                clientRoleHeader: clientRoleHeader
            );
        }
    }
}
