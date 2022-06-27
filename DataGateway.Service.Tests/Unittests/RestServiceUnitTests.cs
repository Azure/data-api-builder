using System.Net;
using Azure.DataGateway.Config;
using Azure.DataGateway.Service.Authorization;
using Azure.DataGateway.Service.Configurations;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using Azure.DataGateway.Service.Tests.SqlTests;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataGateway.Service.Tests.Unittests
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
        [DataRow("foo////bar////baz/qux", "////foo", "bar", "baz/qux")]
        [DataRow("foo/bar/baz/qux/1/fred/23/thud/456789", "",
            "foo", "bar/baz/qux/1/fred/23/thud/456789")]
        public void ParseEntityNameAndPrimaryKeyTest(string route,
                                                     string path,
                                                     string expectedEntityName,
                                                     string expectedPrimaryKeyRoute)
        {
            InitializeTest(path);
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
            InitializeTest(path);
            try
            {
                (string actualEntityName, string actualPrimaryKeyRoute) =
                _restService.GetEntityNameAndPrimaryKeyRouteFromRoute(route);
            }
            catch (DataGatewayException e)
            {
                Assert.AreEqual(e.Message, $"Invalid Path for route: {route}.");
                Assert.AreEqual(e.StatusCode, HttpStatusCode.BadRequest);
                Assert.AreEqual(e.SubStatusCode, DataGatewayException.SubStatusCodes.BadRequest);
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
        public static void InitializeTest(string path)
        {
            RuntimeConfig _runtimeConfig = SqlTestHelper.LoadConfig($"{_testCategory}").CurrentValue;
            Mock<RuntimeConfigProvider> mockRuntimeConfigProvider = new();
            mockRuntimeConfigProvider.Setup(x => x.GetRuntimeConfiguration()).Returns(_runtimeConfig);
            mockRuntimeConfigProvider.Setup(x => x.RestPath).Returns(path);
            RuntimeConfigProvider runtimeConfigProvider = mockRuntimeConfigProvider.Object;
            MsSqlQueryBuilder queryBuilder = new();
            DbExceptionParser dbExceptionParser = new(runtimeConfigProvider);
            QueryExecutor<SqlConnection> queryExecutor = new(runtimeConfigProvider, dbExceptionParser);
            MsSqlMetadataProvider sqlMetadataProvider = new(
                runtimeConfigProvider,
                queryExecutor,
                queryBuilder);

            Mock<IAuthorizationService> authorizationService = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            SqlQueryEngine queryEngine = new(
                queryExecutor,
                queryBuilder,
                sqlMetadataProvider);

            SqlMutationEngine mutationEngine =
                new(
                queryEngine,
                queryExecutor,
                queryBuilder,
                sqlMetadataProvider);

            AuthorizationResolver authZResolver = new(runtimeConfigProvider, sqlMetadataProvider);
            // Setup REST Service
            _restService = new RestService(
                queryEngine,
                mutationEngine,
                sqlMetadataProvider,
                httpContextAccessor.Object,
                authorizationService.Object,
                authZResolver,
                runtimeConfigProvider);
        }

        #endregion
    }
}
