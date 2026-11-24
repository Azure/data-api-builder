// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// End-to-end MSSQL integration tests for <see cref="DescribeEntitiesTool"/>'s table and view
    /// field merge against a real <see cref="MsSqlMetadataProvider"/> and <see cref="AuthorizationResolver"/>
    /// (GitHub issue #3590).
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class DescribeEntitiesTableAndViewFieldsMsSqlIntegrationTests : SqlTestBase
    {
        private static RuntimeConfig _baseConfig;

        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
            _baseConfig = SqlTestHelper.SetupRuntimeConfig();
        }

        /// <summary>
        /// Table with an empty fields array in config ('Publisher' -> dbo.publishers) surfaces
        /// all DB-introspected columns and marks the DB primary key ('id').
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_TableWithEmptyConfigFields_SurfacesAllDbColumnsAndPrimaryKey()
        {
            JsonElement fields = await DescribeEntityFieldsAsync(entityKey: "Publisher", expectedDisplayName: "Publisher");

            Assert.AreEqual(2, fields.GetArrayLength());
            AssertField(fields, name: "id", expectedDescription: string.Empty, expectedPrimaryKey: true, expectedDataType: "integer");
            AssertField(fields, name: "name", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
        }

        /// <summary>
        /// Table with a partial fields array in config ('Book' -> dbo.books, where config lists only
        /// 'id' and 'title' with primary-key: false) still surfaces the unconfigured DB column
        /// 'publisher_id' and preserves the DB primary key on 'id'.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_TableWithPartialConfigFields_SurfacesUnconfiguredDbColumnAndDbPrimaryKey()
        {
            JsonElement fields = await DescribeEntityFieldsAsync(entityKey: "Book", expectedDisplayName: "book");

            Assert.AreEqual(3, fields.GetArrayLength());
            AssertField(fields, name: "id", expectedDescription: string.Empty, expectedPrimaryKey: true, expectedDataType: "integer");
            AssertField(fields, name: "title", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "publisher_id", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "integer");
        }

        /// <summary>
        /// Table covering all SQL scalar families ('SupportedType' -> dbo.type_table, where config
        /// aliases 'id' -> 'typeid' with primary-key: false) preserves the DB primary key on 'typeid'
        /// and maps integer, floating/decimal, boolean, temporal, binary, and UUID columns accurately.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_SupportedTypeTable_MapsScalarColumnDataTypesAndPreservesAliasedDbPrimaryKey()
        {
            JsonElement fields = await DescribeEntityFieldsAsync(
                entityKey: "SupportedType",
                expectedDisplayName: "SupportedType");

            Assert.AreEqual(19, fields.GetArrayLength());
            AssertField(fields, name: "typeid", expectedDescription: string.Empty, expectedPrimaryKey: true, expectedDataType: "integer");
            AssertField(fields, name: "byte_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "integer");
            AssertField(fields, name: "short_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "integer");
            AssertField(fields, name: "int_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "integer");
            AssertField(fields, name: "long_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "integer");
            AssertField(fields, name: "single_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "number");
            AssertField(fields, name: "float_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "number");
            AssertField(fields, name: "decimal_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "number");
            AssertField(fields, name: "boolean_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "boolean");
            AssertField(fields, name: "string_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "nvarchar_string_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "date_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "datetime_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "datetime2_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "datetimeoffset_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "smalldatetime_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "time_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "bytearray_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "uuid_types", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
        }

        /// <summary>
        /// View with a partial fields block setting both an alias ('id' -> 'book_id') and
        /// primary-key: true ('books_view_with_mapping') surfaces the aliased PK alongside
        /// the remaining DB view columns ('title', 'publisher_id').
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_ViewWithAliasedPrimaryKeyInConfig_MergesOverlayWithRemainingViewColumns()
        {
            JsonElement fields = await DescribeEntityFieldsAsync(
                entityKey: "books_view_with_mapping",
                expectedDisplayName: "books_view_with_mapping");

            Assert.AreEqual(3, fields.GetArrayLength());
            AssertField(fields, name: "book_id", expectedDescription: string.Empty, expectedPrimaryKey: true, expectedDataType: "integer");
            AssertField(fields, name: "title", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "publisher_id", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "integer");
        }

        /// <summary>
        /// View configured with composite source.key-fields ('id', 'publisher_id') and no fields block
        /// ('books_publishers_view_composite_insertable') surfaces all view columns and marks both keys.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_ViewWithCompositeSourceKeyFields_SurfacesAllViewColumnsAndCompositeKeys()
        {
            JsonElement fields = await DescribeEntityFieldsAsync(
                entityKey: "books_publishers_view_composite_insertable",
                expectedDisplayName: "books_publishers_view_composite_insertable");

            Assert.AreEqual(4, fields.GetArrayLength());
            AssertField(fields, name: "id", expectedDescription: string.Empty, expectedPrimaryKey: true, expectedDataType: "integer");
            AssertField(fields, name: "title", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "name", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "publisher_id", expectedDescription: string.Empty, expectedPrimaryKey: true, expectedDataType: "integer");
        }

        /// <summary>
        /// End-to-end check with an isolated MsSqlMetadataProvider and AuthorizationResolver on dbo.books
        /// combining config Description (with conflicting Mappings entry on 'title'), legacy Mappings alias
        /// on 'id', and role column exclusion (fields.exclude on 'publisher_id') without mutating shared
        /// static SqlTestBase fixture state.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_TableWithDescriptionMappingsAndExcludedColumn_AppliesOverlayAndAuthFilter()
        {
            RuntimeConfig baseConfig = _baseConfig;
            const string EntityName = "BooksCustomOverlay";

            Entity customEntity = new(
                Source: new("books", EntitySourceType.Table, Parameters: null, KeyFields: null),
                GraphQL: new(EntityName, EntityName, Enabled: false),
                Rest: new(Enabled: false),
                Fields: new List<FieldMetadata>
                {
                    // 'title' is in Fields WITHOUT Alias while Mappings also defines 'title' -> 'mappedTitle'.
                    // SqlMetadataProvider keeps 'title' as the exposed name; DescribeEntitiesTool must match
                    // so AuthorizationResolver does not filter out 'title'.
                    new() { Name = "title", Description = "Human-readable book title" }
                },
                Permissions: new[]
                {
                    new EntityPermission(
                        Role: AuthorizationResolver.ROLE_ANONYMOUS,
                        Actions: new[]
                        {
                            new EntityAction(
                                Action: EntityActionOperation.Read,
                                Fields: new EntityActionFields(
                                    Exclude: new HashSet<string> { "publisher_id" },
                                    Include: null),
                                Policy: null)
                        })
                },
                Relationships: null,
                Mappings: new Dictionary<string, string>
                {
                    ["id"] = "bookId",
                    ["title"] = "mappedTitle",
                    ["publisher_id"] = "publisherId"
                },
                Mcp: null);

            Dictionary<string, Entity> entityMap = new(baseConfig.Entities.Entities)
            {
                [EntityName] = customEntity
            };
            RuntimeConfig isolatedConfig = baseConfig with { Entities = new(entityMap) };

            (RuntimeConfigProvider isolatedConfigProvider, IMetadataProviderFactory isolatedFactory, IAuthorizationResolver isolatedAuthResolver) =
                await CreateIsolatedMetadataAndAuthAsync(isolatedConfig);

            IServiceProvider serviceProvider = BuildDescribeEntitiesServiceProvider(
                isolatedConfigProvider,
                isolatedFactory,
                isolatedAuthResolver);

            JsonElement fields = await DescribeEntityFieldsAsync(
                entityKey: EntityName,
                expectedDisplayName: EntityName,
                serviceProvider: serviceProvider);

            // publisher_id (aliased to publisherId) is excluded by anonymous READ permission;
            // id (aliased via Mappings to bookId) and title (with Description, un-aliased) remain.
            Assert.AreEqual(2, fields.GetArrayLength());
            AssertField(fields, name: "bookId", expectedDescription: string.Empty, expectedPrimaryKey: true, expectedDataType: "integer");
            AssertField(fields, name: "title", expectedDescription: "Human-readable book title", expectedPrimaryKey: false, expectedDataType: "string");
        }

        /// <summary>
        /// Degraded fallback: when DB metadata is unavailable for a table/view entity (e.g., non-SQL
        /// provider such as Cosmos DB where EntityToDatabaseObject is empty), config-declared fields
        /// are projected while still enforcing role column-level authorization from a consistent
        /// AuthorizationResolver initialized against that same provider.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_WhenDatabaseMetadataUnavailable_FallsBackToConfigFields()
        {
            const string EntityName = "CosmosFallbackBook";

            Entity fallbackEntity = new(
                Source: new("books", EntitySourceType.Table, Parameters: null, KeyFields: null),
                GraphQL: new(EntityName, EntityName),
                Rest: new(Enabled: false),
                Fields: new List<FieldMetadata>
                {
                    new() { Name = "id", PrimaryKey = true, Description = "Document identifier" },
                    new() { Name = "title", Description = "Book title" },
                    new() { Name = "internal_cost", Description = "Restricted cost" }
                },
                Permissions: new[]
                {
                    new EntityPermission(
                        Role: AuthorizationResolver.ROLE_ANONYMOUS,
                        Actions: new[]
                        {
                            new EntityAction(
                                Action: EntityActionOperation.Read,
                                Fields: new EntityActionFields(
                                    Exclude: new HashSet<string> { "internal_cost" },
                                    Include: null),
                                Policy: null)
                        })
                },
                Relationships: null,
                Mappings: null,
                Mcp: null);

            RuntimeConfig fallbackConfig = _baseConfig with
            {
                DataSource = _baseConfig.DataSource with { DatabaseType = DatabaseType.CosmosDB_NoSQL },
                Entities = new(new Dictionary<string, Entity> { [EntityName] = fallbackEntity })
            };

            RuntimeConfigProvider fallbackConfigProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(fallbackConfig);

            Mock<ISqlMetadataProvider> unpopulatedProvider = new();
            unpopulatedProvider.Setup(x => x.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject>());
            unpopulatedProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.CosmosDB_NoSQL);
            unpopulatedProvider
                .Setup(x => x.GetSchemaGraphQLFieldNamesForEntityName(EntityName))
                .Returns(new List<string> { "id", "title", "internal_cost" });
            string? exposedOut = null;
            unpopulatedProvider
                .Setup(x => x.TryGetExposedColumnName(EntityName, It.IsAny<string>(), out exposedOut))
                .Callback(new TryGetExposedCallback((string _, string backing, out string? exposed) => exposed = backing))
                .Returns(true);

            Mock<IMetadataProviderFactory> unpopulatedFactory = new();
            unpopulatedFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(unpopulatedProvider.Object);

            AuthorizationResolver consistentAuthResolver = new(fallbackConfigProvider, unpopulatedFactory.Object);

            IServiceProvider serviceProvider = BuildDescribeEntitiesServiceProvider(
                fallbackConfigProvider,
                unpopulatedFactory.Object,
                consistentAuthResolver);

            JsonElement fields = await DescribeEntityFieldsAsync(
                entityKey: EntityName,
                expectedDisplayName: EntityName,
                serviceProvider: serviceProvider);

            Assert.AreEqual(2, fields.GetArrayLength());
            AssertField(fields, name: "id", expectedDescription: "Document identifier", expectedPrimaryKey: true, expectedDataType: null);
            AssertField(fields, name: "title", expectedDescription: "Book title", expectedPrimaryKey: false, expectedDataType: null);
        }

        private delegate void TryGetExposedCallback(string entityName, string backingFieldName, out string? exposedName);

        private static Task<JsonElement> DescribeEntityFieldsAsync(string entityKey, string expectedDisplayName) =>
            DescribeEntityFieldsAsync(
                entityKey,
                expectedDisplayName,
                BuildDescribeEntitiesServiceProvider(
                    TestHelper.GenerateInMemoryRuntimeConfigProvider(_baseConfig),
                    _metadataProviderFactory.Object,
                    _authorizationResolver));

        private static async Task<JsonElement> DescribeEntityFieldsAsync(
            string entityKey,
            string expectedDisplayName,
            IServiceProvider serviceProvider)
        {
            DescribeEntitiesTool tool = new();

            string argsJson = JsonSerializer.Serialize(new { entities = new[] { entityKey } });
            using JsonDocument arguments = JsonDocument.Parse(argsJson);

            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);

            Assert.IsTrue(result.IsError == false || result.IsError == null,
                $"describe_entities returned an error for entity '{entityKey}'. Content: {SerializeFirstContent(result)}");

            return GetEntityFields(result, expectedDisplayName);
        }

        private static async Task<(RuntimeConfigProvider configProvider, IMetadataProviderFactory metadataProviderFactory, IAuthorizationResolver authorizationResolver)>
            CreateIsolatedMetadataAndAuthAsync(RuntimeConfig config)
        {
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            IFileSystem fileSystem = new FileSystem();
            RuntimeConfigValidator validator = new(
                configProvider,
                fileSystem,
                new Mock<ILogger<RuntimeConfigValidator>>().Object);

            MsSqlQueryBuilder queryBuilder = new();
            MsSqlDbExceptionParser dbExceptionParser = new(configProvider);
            MsSqlQueryExecutor queryExecutor = new(
                configProvider,
                dbExceptionParser,
                new Mock<ILogger<QueryExecutor<SqlConnection>>>().Object,
                new Mock<IHttpContextAccessor>().Object);

            Mock<IAbstractQueryManagerFactory> queryManagerFactory = new();
            queryManagerFactory.Setup(x => x.GetQueryBuilder(It.IsAny<DatabaseType>())).Returns(queryBuilder);
            queryManagerFactory.Setup(x => x.GetQueryExecutor(It.IsAny<DatabaseType>())).Returns(queryExecutor);

            MsSqlMetadataProvider sqlMetadataProvider = new(
                configProvider,
                validator,
                queryManagerFactory.Object,
                new Mock<ILogger<ISqlMetadataProvider>>().Object,
                configProvider.GetConfig().DefaultDataSourceName);
            await sqlMetadataProvider.InitializeAsync();

            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            metadataProviderFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(sqlMetadataProvider);

            AuthorizationResolver authorizationResolver = new(configProvider, metadataProviderFactory.Object);
            return (configProvider, metadataProviderFactory.Object, authorizationResolver);
        }

        private static IServiceProvider BuildDescribeEntitiesServiceProvider(
            RuntimeConfigProvider configProvider,
            IMetadataProviderFactory metadataProviderFactory,
            IAuthorizationResolver authorizationResolver)
        {
            ServiceCollection services = new();

            services.AddSingleton(configProvider);
            services.AddSingleton(metadataProviderFactory);
            services.AddSingleton(authorizationResolver);

            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = AuthorizationResolver.ROLE_ANONYMOUS;

            ClaimsIdentity identity = new(
                authenticationType: "TestAuth",
                nameType: null,
                roleType: AuthenticationOptions.ROLE_CLAIM_TYPE);
            identity.AddClaim(new Claim(AuthenticationOptions.ROLE_CLAIM_TYPE, AuthorizationResolver.ROLE_ANONYMOUS));
            httpContext.User = new ClaimsPrincipal(identity);

            IHttpContextAccessor httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
            services.AddSingleton(httpContextAccessor);

            services.AddLogging();

            return services.BuildServiceProvider();
        }

        private static JsonElement GetEntityFields(CallToolResult result, string expectedDisplayName)
        {
            Assert.IsNotNull(result.Content);
            Assert.IsTrue(result.Content.Count > 0);
            Assert.IsInstanceOfType(result.Content[0], typeof(TextContentBlock));

            TextContentBlock firstContent = (TextContentBlock)result.Content[0];
            JsonElement root = JsonDocument.Parse(firstContent.Text).RootElement;
            JsonElement entities = root.GetProperty("entities");

            JsonElement entity = entities.EnumerateArray().Single(e =>
                string.Equals(e.GetProperty("name").GetString(), expectedDisplayName, StringComparison.Ordinal));

            return entity.GetProperty("fields");
        }

        private static string SerializeFirstContent(CallToolResult result)
        {
            if (result.Content is null || result.Content.Count == 0)
            {
                return "<no content>";
            }

            return result.Content[0] is TextContentBlock textBlock
                ? textBlock.Text ?? string.Empty
                : result.Content[0].GetType().Name;
        }

        private static void AssertField(
            JsonElement fields,
            string name,
            string expectedDescription,
            bool expectedPrimaryKey,
            string? expectedDataType)
        {
            JsonElement field = fields.EnumerateArray()
                .Single(f => f.GetProperty("name").GetString() == name);

            Assert.AreEqual(expectedDescription, field.GetProperty("description").GetString(),
                $"description mismatch for field '{name}'.");
            Assert.AreEqual(expectedPrimaryKey, field.GetProperty("primaryKey").GetBoolean(),
                $"primaryKey mismatch for field '{name}'.");

            JsonElement dataTypeElement = field.GetProperty("dataType");
            if (expectedDataType is null)
            {
                Assert.AreEqual(JsonValueKind.Null, dataTypeElement.ValueKind,
                    $"dataType should be JSON null for field '{name}'.");
            }
            else
            {
                Assert.AreEqual(expectedDataType, dataTypeElement.GetString(),
                    $"dataType mismatch for field '{name}'.");
            }
        }
    }
}
