// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Net;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.Cache;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using ZiggyCreatures.Caching.Fusion;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class RestServiceUnitTests
    {
        private static RestService _restService;

        #region Positive Cases

        /// <summary>
        /// Validates that the RestService helper function GetEntityNameAndPrimaryKeyRouteFromRoute
        /// properly parses the entity name and primary key route from the route,
        /// given the input path (which does not include the path base).
        /// </summary>
        /// <param name="route">The route to parse.</param>
        /// <param name="path">The path that the route starts with.</param>
        /// <param name="expectedEntityName">The entity name we expect to parse
        /// from route.</param>
        /// <param name="expectedPrimaryKeyRoute">The primary key route we
        /// expect to parse from route.</param>
        [DataTestMethod]
        [DataRow("rest-api/Book/id/1", "/rest-api", "Book", "id/1")]
        [DataRow("rest api/Book/id/1", "/rest api", "Book", "id/1")]
        [DataRow(" rest_api/commodities/categoryid/1/pieceid/1", "/ rest_api", "commodities", "categoryid/1/pieceid/1")]
        [DataRow("rest-api/Book/id/1", "/rest-api", "Book", "id/1")]
        public void ParseEntityNameAndPrimaryKeyTest(
            string route,
            string path,
            string expectedEntityName,
            string expectedPrimaryKeyRoute)
        {
            InitializeTest(path, expectedEntityName);
            string routeAfterPathBase = _restService.GetRouteAfterPathBase(route);
            (string actualEntityName, string actualPrimaryKeyRoute) =
                _restService.GetEntityNameAndPrimaryKeyRouteFromRoute(routeAfterPathBase);
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
        [DataRow("/foo/bar", "foo")]
        [DataRow("food/Book", "foo")]
        [DataRow("\"foo\"", "foo")]
        [DataRow("foo/bar", "bar")]
        public void ErrorForInvalidRouteAndPathToParseTest(string route,
                                                           string path)
        {
            InitializeTest(path, route);
            try
            {
                string routeAfterPathBase = _restService.GetRouteAfterPathBase(route);
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
        /// Mock and instantiates required components
        /// for the REST Service.
        /// </summary>
        /// <param name="restRoutePrefix">path to return from mocked config.</param>
        public static void InitializeTest(string restRoutePrefix, string entityName)
        {
            RuntimeConfig mockConfig = new(
               Schema: "",
               DataSource: new(DatabaseType.PostgreSQL, "", new()),
               Runtime: new(
                   Rest: new(Path: restRoutePrefix),
                   GraphQL: new(),
                   Mcp: new(),
                   Host: new(null, null)
               ),
               Entities: new(new Dictionary<string, Entity>())
           );

            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(mockConfig.ToJson()));
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader);
            MsSqlQueryBuilder queryBuilder = new();
            Mock<DbExceptionParser> dbExceptionParser = new(provider);
            Mock<ILogger<QueryExecutor<SqlConnection>>> queryExecutorLogger = new();
            Mock<ILogger<ISqlMetadataProvider>> sqlMetadataLogger = new();
            Mock<ILogger<IQueryEngine>> queryEngineLogger = new();
            Mock<ILogger<SqlMutationEngine>> mutationEngineLogger = new();
            Mock<ILogger<AuthorizationResolver>> authLogger = new();
            Mock<IHttpContextAccessor> httpContextAccessor = new();
            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            Mock<IAbstractQueryManagerFactory> queryManagerFactory = new();
            Mock<IQueryEngineFactory> queryEngineFactory = new();

            MsSqlQueryExecutor queryExecutor = new(
                provider,
                dbExceptionParser.Object,
                queryExecutorLogger.Object,
                httpContextAccessor.Object);

            queryManagerFactory.Setup(x => x.GetQueryBuilder(It.IsAny<DatabaseType>())).Returns(queryBuilder);
            queryManagerFactory.Setup(x => x.GetQueryExecutor(It.IsAny<DatabaseType>())).Returns(queryExecutor);

            RuntimeConfig loadedConfig = provider.GetConfig();
            loadedConfig.TryAddEntityPathNameToEntityName(entityName, entityName);

            Mock<ISqlMetadataProvider> sqlMetadataProvider = new();
            Mock<IAuthorizationService> authorizationService = new();
            DefaultHttpContext context = new();
            httpContextAccessor.Setup(_ => _.HttpContext).Returns(context);
            AuthorizationResolver authorizationResolver = new(provider, metadataProviderFactory.Object);
            GQLFilterParser gQLFilterParser = new(provider, metadataProviderFactory.Object);

            Mock<IFusionCache> cache = new();
            DabCacheService cacheService = new(cache.Object, logger: null, httpContextAccessor.Object);

            SqlQueryEngine queryEngine = new(
                queryManagerFactory.Object,
                metadataProviderFactory.Object,
                httpContextAccessor.Object,
                authorizationResolver,
                gQLFilterParser,
                queryEngineLogger.Object,
                provider,
                cacheService);

            queryEngineFactory.Setup(x => x.GetQueryEngine(It.IsAny<DatabaseType>())).Returns(queryEngine);

            SqlMutationEngine mutationEngine =
                new(
                queryManagerFactory.Object,
                metadataProviderFactory.Object,
                queryEngineFactory.Object,
                authorizationResolver,
                gQLFilterParser,
                httpContextAccessor.Object,
                provider);

            Mock<IMutationEngineFactory> mutationEngineFactory = new();
            mutationEngineFactory.Setup(x => x.GetMutationEngine(It.IsAny<DatabaseType>())).Returns(mutationEngine);
            RequestValidator requestValidator = new(metadataProviderFactory.Object, provider);

            // Setup REST Service
            _restService = new RestService(
                queryEngineFactory.Object,
                mutationEngineFactory.Object,
                metadataProviderFactory.Object,
                httpContextAccessor.Object,
                authorizationService.Object,
                provider,
                requestValidator);
        }

        /// <summary>
        /// Needed for the callback that is required
        /// to make use of out parameter with mocking.
        /// Without use of delegate the out param will
        /// not be populated with the correct value.
        /// This delegate is for the callback used
        /// with the mocked MetadataProvider.
        /// </summary>
        /// <param name="entityPath">The entity path.</param>
        /// <param name="entity">Name of entity.</param>
        delegate void metaDataCallback(string entityPath, out string entity);
        #endregion
    }
}
