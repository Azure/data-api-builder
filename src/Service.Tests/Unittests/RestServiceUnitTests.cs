using System.Collections.Generic;
using System.Net;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Service.Authorization;
using Azure.DataApiBuilder.Service.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Resolvers;
using Azure.DataApiBuilder.Service.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Unittests
{
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class RestServiceUnitTests
    {
        private static RestService _restService;
        private static string _testCategory = "mssql";

        #region Positive Cases

        /// <summary>
        /// Test the REST Service for parsing entity name
        /// and primary key route from the route, given a
        /// particular path.
        /// </summary>
        /// <param name="route">The route to parse.</param>
        /// <param name="path">The path that the route starts with.</param>
        /// <param name="expectedEntityName">The entity name we expect to parse
        /// from route.</param>
        /// <param name="expectedPrimaryKeyRoute">The primary key route we
        /// expect to parse from route.</param>
        [DataTestMethod]
        [DataRow("foo", "", "foo", "")]
        [DataRow("foo/", "", "foo", "")]
        [DataRow("foo", "/", "foo", "")]
        [DataRow("foo", "foo", "", "")]
        [DataRow("foo/bar", "", "foo", "bar")]
        [DataRow("foo/bar", "/foo", "bar", "")]
        [DataRow("foo/bar", "foo/", "bar", "")]
        [DataRow("foo/bar", "/foo/", "bar", "")]
        [DataRow("foo/bar/baz", "", "foo", "bar/baz")]
        [DataRow("foo/bar/baz", "/foo", "bar", "baz")]
        [DataRow("foo/bar/baz", "/foo/bar", "baz", "")]
        [DataRow("foo/bar/baz", "/foo/bar/", "baz", "")]
        [DataRow("foo/bar/baz/qux", "", "foo", "bar/baz/qux")]
        [DataRow("foo/bar/baz/qux", "/foo", "bar", "baz/qux")]
        [DataRow("foo/bar/baz/qux", "/foo/bar/", "baz", "qux")]
        [DataRow("foo/bar/baz/qux", "/foo/bar/baz", "qux", "")]
        [DataRow("foo////bar////baz/qux", "////foo", "bar", "///baz/qux")]
        [DataRow("foo/bar/baz/qux/1/fred/23/thud/456789", "",
            "foo", "bar/baz/qux/1/fred/23/thud/456789")]
        public void ParseEntityNameAndPrimaryKeyTest(string route,
                                                     string path,
                                                     string expectedEntityName,
                                                     string expectedPrimaryKeyRoute)
        {
            InitializeTest(path, expectedEntityName);
            (string actualEntityName, string actualPrimaryKeyRoute) =
                _restService.GetEntityNameAndPrimaryKeyRouteFromRoute(route);
            Assert.AreEqual(expectedEntityName, actualEntityName);
            Assert.AreEqual(expectedPrimaryKeyRoute, actualPrimaryKeyRoute);
        }

        #endregion

        #region Negative Cases

        /// <summary>
        /// Verify that the correct exception with the
        /// proper messaging and codes is thrown for
        /// an invalid route and path combination.
        /// </summary>
        /// <param name="route">The route to be parsed.</param>
        /// <param name="path">An invalid path for the given route.</param>
        [DataTestMethod]
        [DataRow("foo", "bar")]
        [DataRow("\\foo", "foo")]
        [DataRow("\"foo\"", "foo")]
        [DataRow("foo/bar", ".")]
        [DataRow("foo/bar", "bar")]
        public void ErrorForInvalidRouteAndPathToParseTest(string route,
                                                           string path)
        {
            InitializeTest(path, route);
            try
            {
                (string actualEntityName, string actualPrimaryKeyRoute) =
                _restService.GetEntityNameAndPrimaryKeyRouteFromRoute(route);
            }
            catch (DataApiBuilderException e)
            {
                Assert.AreEqual(e.Message, $"Invalid Path for route: {route}.");
                Assert.AreEqual(e.StatusCode, HttpStatusCode.BadRequest);
                Assert.AreEqual(e.SubStatusCode, DataApiBuilderException.SubStatusCodes.BadRequest);
            }
            catch
            {
                Assert.Fail();
            }
        }

        #endregion

        #region Helper Functions

        /// <summary>
        /// Mock and instantitate required components
        /// for the REST Service.
        /// </summary>
        /// <param name="path">path to return from mocked
        /// runtimeconfigprovider.</param>
        public static void InitializeTest(string path, string entityName)
        {
            RuntimeConfigPath runtimeConfigPath = TestHelper.GetRuntimeConfigPath(_testCategory);
            RuntimeConfigProvider runtimeConfigProvider =
                TestHelper.GetMockRuntimeConfigProvider(runtimeConfigPath, path);
            MsSqlQueryBuilder queryBuilder = new();
            DbExceptionParser dbExceptionParser = new(runtimeConfigProvider);
            Mock<ILogger<QueryExecutor<SqlConnection>>> queryExecutorLogger = new();
            Mock<ILogger<ISqlMetadataProvider>> sqlMetadataLogger = new();
            Mock<ILogger<SqlQueryEngine>> queryEngineLogger = new();
            Mock<ILogger<SqlMutationEngine>> mutationEngingLogger = new();
            Mock<ILogger<AuthorizationResolver>> authLogger = new();

            QueryExecutor<SqlConnection> queryExecutor = new(
                runtimeConfigProvider,
                dbExceptionParser,
                queryExecutorLogger.Object);
            Mock<MsSqlMetadataProvider> sqlMetadataProvider = new(
                runtimeConfigProvider,
                queryExecutor,
                queryBuilder,
                sqlMetadataLogger.Object);
            string outParam;
            sqlMetadataProvider.Setup(x => x.TryGetEntityNameFromPath(It.IsAny<string>(), out outParam)).Returns(true);
            Dictionary<string, string> _pathToEntityMock = new() { { entityName, entityName } };
            sqlMetadataProvider.Setup(x => x.TryGetEntityNameFromPath(It.IsAny<string>(), out outParam))
                               .Callback(new metaDataCallback((string entityPath, out string entity) => _ = _pathToEntityMock.TryGetValue(entityPath, out entity)))
                               .Returns((string entityPath, out string entity) => _pathToEntityMock.TryGetValue(entityPath, out entity));
            Mock<IAuthorizationService> authorizationService = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            DefaultHttpContext context = new();
            httpContextAccessor.Setup(_ => _.HttpContext).Returns(context);
            AuthorizationResolver authorizationResolver = new(runtimeConfigProvider, sqlMetadataProvider.Object, authLogger.Object);

            SqlQueryEngine queryEngine = new(
                queryExecutor,
                queryBuilder,
                sqlMetadataProvider.Object,
                httpContextAccessor.Object,
                authorizationResolver,
                queryEngineLogger.Object,
                runtimeConfigProvider);

            SqlMutationEngine mutationEngine =
                new(
                queryEngine,
                queryExecutor,
                queryBuilder,
                sqlMetadataProvider.Object,
                authorizationResolver,
                httpContextAccessor.Object,
                mutationEngingLogger.Object);

            // Setup REST Service
            _restService = new RestService(
                queryEngine,
                mutationEngine,
                sqlMetadataProvider.Object,
                httpContextAccessor.Object,
                authorizationService.Object,
                authorizationResolver,
                runtimeConfigProvider);
        }

        /// <summary>
        /// Needed for the callback that is required
        /// to make use of out parameter with mocking.
        /// Without use of delegate the out param will
        /// not be populated with the correct value.
        /// This delegate is for the callback used
        /// with the mocked SqlMetadataProvider.
        /// </summary>
        /// <param name="entityPath">The entity path.</param>
        /// <param name="entity">Name of entity.</param>
        delegate void metaDataCallback(string entityPath, out string entity);
        #endregion
    }
}
