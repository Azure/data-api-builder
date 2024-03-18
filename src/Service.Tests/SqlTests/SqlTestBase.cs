// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.Cache;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Controllers;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using MySqlConnector;
using Npgsql;
using ZiggyCreatures.Caching.Fusion;

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
        protected static Mock<IAuthorizationService> _authorizationService;
        protected static Mock<IHttpContextAccessor> _httpContextAccessor;
        protected static Mock<IAbstractQueryManagerFactory> _queryManagerFactory;
        protected static Mock<IQueryEngineFactory> _queryEngineFactory;
        protected static Mock<IMetadataProviderFactory> _metadataProviderFactory;
        protected static DbExceptionParser _dbExceptionParser;
        protected static ISqlMetadataProvider _sqlMetadataProvider;
        protected static string _defaultSchemaName;
        protected static string _defaultSchemaVersion;
        protected static IAuthorizationResolver _authorizationResolver;
        private static WebApplicationFactory<Program> _application;
        protected static ILogger<ISqlMetadataProvider> _sqlMetadataLogger;
        protected static ILogger<SqlMutationEngine> _mutationEngineLogger;
        protected static ILogger<IQueryEngine> _queryEngineLogger;
        protected static ILogger<RestController> _restControllerLogger;
        protected static GQLFilterParser _gQLFilterParser;
        protected const string MSSQL_DEFAULT_DB_NAME = "master";

        protected static string DatabaseName { get; set; }
        protected static string DatabaseEngine { get; set; }
        protected static HttpClient HttpClient { get; private set; }

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run.
        /// This is a helper that is called from the non abstract versions of
        /// this class.
        /// </summary>
        /// <param name="customQueries">Test specific queries to be executed on database.</param>
        /// <param name="customEntities">Test specific entities to be added to database.</param>
        /// <param name="isRestBodyStrict">When false, allows extraneous fields in REST request body.</param>
        /// <returns></returns>
        protected async static Task InitializeTestFixture(
            List<string> customQueries = null,
            List<string[]> customEntities = null,
            bool isRestBodyStrict = true)
        {
            TestHelper.SetupDatabaseEnvironment(DatabaseEngine);
            // Get the base config file from disk
            RuntimeConfig runtimeConfig = SqlTestHelper.SetupRuntimeConfig();

            // Setting the rest.request-body-strict flag as per the test fixtures.
            if (!isRestBodyStrict)
            {
                runtimeConfig = runtimeConfig with { Runtime = runtimeConfig.Runtime with { Rest = runtimeConfig.Runtime?.Rest with { RequestBodyStrict = isRestBodyStrict } } };
            }

            // Add magazines entity to the config
            runtimeConfig = DatabaseEngine switch
            {
                TestCategory.MYSQL => TestHelper.AddMissingEntitiesToConfig(
                    config: runtimeConfig,
                    entityKey: "magazine",
                    entityName: "magazines"),
                TestCategory.DWSQL => TestHelper.AddMissingEntitiesToConfig(
                    config: runtimeConfig,
                    entityKey: "magazine",
                    entityName: "foo.magazines",
                    keyfields: new string[] { "id" }),
                _ => TestHelper.AddMissingEntitiesToConfig(
                    config: runtimeConfig,
                    entityKey: "magazine",
                    entityName: "foo.magazines"),
            };

            // Add table name collision testing entity to the config
            runtimeConfig = DatabaseEngine switch
            {
                // MySql does not handle schema the same as other DB, so this testing entity is not needed
                TestCategory.MYSQL => runtimeConfig,
                _ => TestHelper.AddMissingEntitiesToConfig(
                    config: runtimeConfig,
                    entityKey: "bar_magazine",
                    entityName: "bar.magazines",
                    keyfields: new string[] { "upc" })
            };

            // Add custom entities for the test, if any.
            runtimeConfig = AddCustomEntities(customEntities, runtimeConfig);

            // Generate in memory runtime config provider that uses the config that we have modified
            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(runtimeConfig);

            _queryEngineLogger = new Mock<ILogger<IQueryEngine>>().Object;
            _mutationEngineLogger = new Mock<ILogger<SqlMutationEngine>>().Object;
            _restControllerLogger = new Mock<ILogger<RestController>>().Object;

            SetUpSQLMetadataProvider(runtimeConfigProvider);

            // Setup Mock HttpContextAccess to return user as required when calling AuthorizationService.AuthorizeAsync
            _httpContextAccessor = new Mock<IHttpContextAccessor>();
            _httpContextAccessor.Setup(x => x.HttpContext.User).Returns(new ClaimsPrincipal());

            await ResetDbStateAsync();

            // Execute additional queries, if any.
            await ExecuteQueriesOnDbAsync(customQueries);

            await _sqlMetadataProvider.InitializeAsync();

            // Setup Mock metadataprovider Factory
            _metadataProviderFactory = new Mock<IMetadataProviderFactory>();
            _metadataProviderFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(_sqlMetadataProvider);

            // Setup Mock engine Factory
            _queryManagerFactory = new Mock<IAbstractQueryManagerFactory>();
            _queryManagerFactory.Setup(x => x.GetQueryBuilder(It.IsAny<DatabaseType>())).Returns(_queryBuilder);
            _queryManagerFactory.Setup(x => x.GetQueryExecutor(It.IsAny<DatabaseType>())).Returns(_queryExecutor);

            _gQLFilterParser = new(runtimeConfigProvider, _metadataProviderFactory.Object);

            // sets the database name using the connection string
            SetDatabaseNameFromConnectionString(runtimeConfig.DataSource.ConnectionString);

            //Initialize the authorization resolver object
            _authorizationResolver = new AuthorizationResolver(
                runtimeConfigProvider,
                _metadataProviderFactory.Object);

            Mock<IFusionCache> cache = new();
            DabCacheService cacheService = new(cache.Object, logger: null, _httpContextAccessor.Object);

            _application = new WebApplicationFactory<Program>()
                .WithWebHostBuilder(builder =>
                {
                    builder.ConfigureTestServices(services =>
                    {
                        services.AddHttpContextAccessor();
                        services.AddSingleton(runtimeConfigProvider);
                        services.AddSingleton(_gQLFilterParser);
                        services.AddSingleton<IQueryEngine>(implementationFactory: (serviceProvider) =>
                        {
                            return new SqlQueryEngine(
                                _queryManagerFactory.Object,
                                ActivatorUtilities.GetServiceOrCreateInstance<MetadataProviderFactory>(serviceProvider),
                                ActivatorUtilities.GetServiceOrCreateInstance<IHttpContextAccessor>(serviceProvider),
                                _authorizationResolver,
                                _gQLFilterParser,
                                _queryEngineLogger,
                                runtimeConfigProvider,
                                cacheService
                                );
                        });
                        services.AddSingleton<IMutationEngine>(implementationFactory: (serviceProvider) =>
                        {
                            return new SqlMutationEngine(
                                    _queryManagerFactory.Object,
                                    ActivatorUtilities.GetServiceOrCreateInstance<MetadataProviderFactory>(serviceProvider),
                                    _queryEngineFactory.Object,
                                    _authorizationResolver,
                                    _gQLFilterParser,
                                    ActivatorUtilities.GetServiceOrCreateInstance<IHttpContextAccessor>(serviceProvider),
                                    runtimeConfigProvider);
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
        private static RuntimeConfig AddCustomEntities(List<string[]> customEntities, RuntimeConfig runtimeConfig)
        {
            if (customEntities is not null)
            {
                foreach (string[] customEntity in customEntities)
                {
                    string objectKey = customEntity[0];
                    string objectName = customEntity[1];
                    runtimeConfig = TestHelper.AddMissingEntitiesToConfig(runtimeConfig, objectKey, objectName);
                }
            }

            return runtimeConfig;
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
                    await _queryExecutor.ExecuteQueryAsync<object>(query, dataSourceName: string.Empty, parameters: null, dataReaderHandler: null);
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

        protected static void SetUpSQLMetadataProvider(RuntimeConfigProvider runtimeConfigProvider)
        {
            _sqlMetadataLogger = new Mock<ILogger<ISqlMetadataProvider>>().Object;
            _queryManagerFactory = new Mock<IAbstractQueryManagerFactory>();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            string dataSourceName = runtimeConfigProvider.GetConfig().DefaultDataSourceName;
            switch (DatabaseEngine)
            {
                case TestCategory.POSTGRESQL:
                    Mock<ILogger<PostgreSqlQueryExecutor>> pgQueryExecutorLogger = new();
                    _queryBuilder = new PostgresQueryBuilder();
                    _defaultSchemaName = "public";
                    _dbExceptionParser = new PostgreSqlDbExceptionParser(runtimeConfigProvider);
                    _queryExecutor = new PostgreSqlQueryExecutor(
                        runtimeConfigProvider,
                        _dbExceptionParser,
                        pgQueryExecutorLogger.Object,
                        httpContextAccessor.Object);
                    _queryManagerFactory.Setup(x => x.GetQueryBuilder(It.IsAny<DatabaseType>())).Returns(_queryBuilder);
                    _queryManagerFactory.Setup(x => x.GetQueryExecutor(It.IsAny<DatabaseType>())).Returns(_queryExecutor);

                    _sqlMetadataProvider =
                        new PostgreSqlMetadataProvider(
                            runtimeConfigProvider,
                            _queryManagerFactory.Object,
                            _sqlMetadataLogger,
                            dataSourceName);
                    break;
                case TestCategory.MSSQL:
                    Mock<ILogger<QueryExecutor<SqlConnection>>> msSqlQueryExecutorLogger = new();
                    _queryBuilder = new MsSqlQueryBuilder();
                    _defaultSchemaName = "dbo";
                    _dbExceptionParser = new MsSqlDbExceptionParser(runtimeConfigProvider);
                    _queryExecutor = new MsSqlQueryExecutor(
                        runtimeConfigProvider,
                        _dbExceptionParser,
                        msSqlQueryExecutorLogger.Object,
                        httpContextAccessor.Object);
                    _queryManagerFactory.Setup(x => x.GetQueryBuilder(It.IsAny<DatabaseType>())).Returns(_queryBuilder);
                    _queryManagerFactory.Setup(x => x.GetQueryExecutor(It.IsAny<DatabaseType>())).Returns(_queryExecutor);

                    _sqlMetadataProvider =
                        new MsSqlMetadataProvider(
                            runtimeConfigProvider,
                            _queryManagerFactory.Object,
                            _sqlMetadataLogger,
                            dataSourceName);
                    break;
                case TestCategory.MYSQL:
                    Mock<ILogger<MySqlQueryExecutor>> mySqlQueryExecutorLogger = new();
                    _queryBuilder = new MySqlQueryBuilder();
                    _defaultSchemaName = "mysql";
                    _dbExceptionParser = new MySqlDbExceptionParser(runtimeConfigProvider);
                    _queryExecutor = new MySqlQueryExecutor(
                        runtimeConfigProvider,
                        _dbExceptionParser,
                        mySqlQueryExecutorLogger.Object,
                        httpContextAccessor.Object);
                    _queryManagerFactory.Setup(x => x.GetQueryBuilder(It.IsAny<DatabaseType>())).Returns(_queryBuilder);
                    _queryManagerFactory.Setup(x => x.GetQueryExecutor(It.IsAny<DatabaseType>())).Returns(_queryExecutor);

                    _sqlMetadataProvider =
                         new MySqlMetadataProvider(
                             runtimeConfigProvider,
                             _queryManagerFactory.Object,
                             _sqlMetadataLogger,
                             dataSourceName);
                    break;
                case TestCategory.DWSQL:
                    Mock<ILogger<MsSqlQueryExecutor>> DwSqlQueryExecutorLogger = new();
                    _queryBuilder = new DwSqlQueryBuilder();
                    _defaultSchemaName = "dbo";
                    _dbExceptionParser = new MsSqlDbExceptionParser(runtimeConfigProvider);
                    _queryExecutor = new MsSqlQueryExecutor(
                        runtimeConfigProvider,
                        _dbExceptionParser,
                        DwSqlQueryExecutorLogger.Object,
                        httpContextAccessor.Object);
                    _queryManagerFactory.Setup(x => x.GetQueryBuilder(It.IsAny<DatabaseType>())).Returns(_queryBuilder);
                    _queryManagerFactory.Setup(x => x.GetQueryExecutor(It.IsAny<DatabaseType>())).Returns(_queryExecutor);

                    _sqlMetadataProvider =
                         new MsSqlMetadataProvider(
                             runtimeConfigProvider,
                             _queryManagerFactory.Object,
                             _sqlMetadataLogger,
                             dataSourceName);
                    break;

            }
        }

        protected static async Task ResetDbStateAsync()
        {
            await _queryExecutor.ExecuteQueryAsync<object>(
                File.ReadAllText($"DatabaseSchema-{DatabaseEngine}.sql"),
                dataSourceName: string.Empty,
                parameters: null,
                dataReaderHandler: null);
        }

        /// <summary>
        /// Sends raw SQL query to database engine to retrieve expected result in JSON format.
        /// </summary>
        /// <param name="queryText">raw database query, typically a SELECT</param>
        /// <returns>string in JSON format</returns>
        protected static async Task<string> GetDatabaseResultAsync(
            string queryText,
            bool expectJson = true)
        {
            string result;

            if (expectJson)
            {
                using JsonDocument sqlResult =
                    await _queryExecutor.ExecuteQueryAsync(
                        queryText,
                        parameters: null,
                        _queryExecutor.GetJsonResultAsync<JsonDocument>,
                        string.Empty);

                result = sqlResult is not null ?
                    sqlResult.RootElement.ToString() :
                    new JsonArray().ToString();
            }
            else
            {
                JsonArray resultArray =
                    await _queryExecutor.ExecuteQueryAsync(
                        queryText,
                        parameters: null,
                        _queryExecutor.GetJsonArrayAsync,
                        string.Empty);
                using JsonDocument sqlResult = resultArray is not null ? JsonDocument.Parse(resultArray.ToJsonString()) : null;
                result = sqlResult is not null ? sqlResult.RootElement.ToString() : null;
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
        /// <param name="entityNameOrPath">string represents the name/path of the entity</param>
        /// <param name="sqlQuery">string represents the query to be executed</param>
        /// <param name="operationType">The operation type to be tested.</param>
        /// <param name="requestBody">string represents JSON data used in mutation operations</param>
        /// <param name="exceptionExpected">bool represents if we expect an exception</param>
        /// <param name="expectedErrorMessage">string represents the error message in the JsonResponse</param>
        /// <param name="expectedStatusCode">int represents the returned http status code</param>
        /// <param name="expectedSubStatusCode">enum represents the returned sub status code</param>
        /// <param name="expectedLocationHeader">The expected location header in the response (if any)</param>
        /// <param name="isExpectedErrorMsgSubstr">When set to true, will look for a substring 'expectedErrorMessage'
        /// in the actual error message to verify the test result. This is helpful when the actual error message is dynamic and changes
        /// on every single run of the test.</param>
        /// <param name="clientRoleHeader">The custom role in whose context the request will be executed.</param>
        /// <returns></returns>
        protected static async Task SetupAndRunRestApiTest(
            string primaryKeyRoute,
            string queryString,
            string entityNameOrPath,
            string sqlQuery,
            EntityActionOperation operationType = EntityActionOperation.Read,
            string restPath = "api",
            IHeaderDictionary headers = null,
            string requestBody = null,
            bool exceptionExpected = false,
            string expectedErrorMessage = "",
            HttpStatusCode expectedStatusCode = HttpStatusCode.OK,
            SupportedHttpVerb? restHttpVerb = null,
            string expectedSubStatusCode = "BadRequest",
            string expectedLocationHeader = null,
            string expectedAfterQueryString = "",
            bool paginated = false,
            int verifyNumRecords = -1,
            bool expectJson = true,
            bool isExpectedErrorMsgSubstr = false,
            string clientRoleHeader = null)
        {
            // Create the rest endpoint using the path and entity name.
            string restEndPoint = restPath + "/" + entityNameOrPath;

            // Append primaryKeyRoute to the endpoint if it is not empty.
            if (!string.IsNullOrEmpty(primaryKeyRoute))
            {
                restEndPoint = restEndPoint + "/" + primaryKeyRoute;
            }

            // Append queryString to the endpoint if it is not empty.
            if (!string.IsNullOrEmpty(queryString))
            {
                restEndPoint = restEndPoint + queryString;
            }

            // Use UnsafeRelaxedJsonEscaping to be less strict about what is encoded.
            // For eg. Without using this encoder, quotation mark (") will be encoded as
            // \u0022 rather than \". And single quote(') will be encoded as \u0027 rather
            // than being left unescaped.
            // More details can be found here:
            // https://docs.microsoft.com/en-us/dotnet/api/system.text.encodings.web.javascriptencoder.unsaferelaxedjsonescaping?view=net-6.0
            JsonSerializerOptions options = new()
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };

            // Get the httpMethod based on the operation to be executed.
            HttpMethod httpMethod = SqlTestHelper.GetHttpMethodFromOperation(operationType, restHttpVerb);

            // Create the request to be sent to the engine.
            HttpRequestMessage request;
            if (!string.IsNullOrEmpty(requestBody))
            {
                JsonElement requestBodyElement = JsonDocument.Parse(requestBody).RootElement.Clone();
                request = new(httpMethod, restEndPoint)
                {
                    Content = JsonContent.Create(requestBodyElement, options: options)
                };
            }
            else
            {
                request = new(httpMethod, restEndPoint);
            }

            // Add headers to the request if any.
            if (headers is not null)
            {
                foreach ((string key, StringValues value) in headers)
                {
                    request.Headers.Add(key, value.ToString());
                }
            }

            if (clientRoleHeader is not null)
            {
                request.Headers.Add(AuthorizationResolver.CLIENT_ROLE_HEADER, clientRoleHeader.ToString());
                request.Headers.Add(AuthenticationOptions.CLIENT_PRINCIPAL_HEADER,
                    AuthTestHelper.CreateStaticWebAppsEasyAuthToken(addAuthenticated: true, specificRole: clientRoleHeader));
            }

            // Send request to the engine.
            HttpResponseMessage response = await HttpClient.SendAsync(request);

            // if an exception is expected we generate the correct error
            // The expected result should be a Query that confirms the result state
            // of the Operation performed for the test. However:
            // Initial DELETE request results in 204 no content, no exception thrown.
            // Subsequent DELETE requests result in 404, which result in an exception.
            string expected;
            if ((operationType is EntityActionOperation.Delete ||
                 operationType is EntityActionOperation.Upsert ||
                 operationType is EntityActionOperation.UpsertIncremental ||
                 operationType is EntityActionOperation.Update ||
                 operationType is EntityActionOperation.UpdateIncremental)
                 && response.StatusCode == HttpStatusCode.NoContent
                )
            {
                expected = string.Empty;
            }
            else
            {
                if (exceptionExpected)
                {
                    expected = JsonSerializer.Serialize(RestController.ErrorResponse(
                        expectedSubStatusCode.ToString(),
                        expectedErrorMessage,
                        expectedStatusCode).Value,
                        options);
                }
                else
                {
                    string baseUrl = HttpClient.BaseAddress.ToString() + restPath + "/" + entityNameOrPath;
                    if (!string.IsNullOrEmpty(queryString))
                    {
                        // Parse query string with AspNetCore components for consistent URI encoding.
                        baseUrl = QueryHelpers.AddQueryString(uri: baseUrl, queryString: QueryHelpers.ParseQuery(queryString));
                    }

                    string dbResult = await GetDatabaseResultAsync(sqlQuery, expectJson);
                    // For FIND requests, null result signifies an empty result set
                    dbResult = (operationType is EntityActionOperation.Read && dbResult is null) ? "[]" : dbResult;
                    expected = $"{{\"{SqlTestHelper.jsonResultTopLevelKey}\":" +
                        $"{FormatExpectedValue(dbResult)}{ExpectedNextLinkIfAny(paginated, baseUrl, $"{expectedAfterQueryString}")}}}";
                }
            }

            // Verify the expected and actual response are identical.
            await SqlTestHelper.VerifyResultAsync(
                expected: expected,
                request: request,
                response: response,
                exceptionExpected: exceptionExpected,
                httpMethod: httpMethod,
                expectedLocationHeader: expectedLocationHeader,
                verifyNumRecords: verifyNumRecords,
                isExpectedErrorMsgSubstr: isExpectedErrorMsgSubstr);
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
            string clientRoleHeader = null,
            bool expectsError = false)
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

        [TestCleanup]
        public void CleanupAfterEachTest()
        {
            TestHelper.UnsetAllDABEnvironmentVariables();
        }
    }
}
