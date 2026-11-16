// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Data;
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
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Unit tests verifying how <see cref="DescribeEntitiesTool"/> merges DB column metadata
    /// (<see cref="SourceDefinition.Columns"/> and <see cref="SourceDefinition.PrimaryKey"/>)
    /// with config field overrides (<see cref="Entity.Fields"/> and <see cref="Entity.Mappings"/>)
    /// for table and view entities (GitHub issue #3590).
    /// </summary>
    [TestClass]
    public class DescribeEntitiesTableAndViewFieldsTests
    {
        private const string TEST_ENTITY_NAME = "Book";

        /// <summary>
        /// Tables and views with no fields block (null or empty list) surface all DB columns,
        /// resolving primaryKey from SourceDefinition.PrimaryKey and dataType from ColumnDefinition.
        /// </summary>
        [DataTestMethod]
        [DataRow(EntitySourceType.Table, false, DisplayName = "Table with null Fields")]
        [DataRow(EntitySourceType.View, true, DisplayName = "View with empty Fields")]
        public async Task DescribeEntities_TableAndViewWithoutConfigFields_SurfacesAllDbColumnsAndPrimaryKey(
            EntitySourceType sourceType,
            bool useEmptyFieldsList)
        {
            Entity entity = CreateEntity(
                sourceType: sourceType,
                fields: useEmptyFieldsList ? new List<FieldMetadata>() : null);

            Dictionary<string, ColumnDefinition> dbColumns = new(StringComparer.InvariantCultureIgnoreCase)
            {
                ["id"] = new(typeof(int)),
                ["title"] = new(typeof(string)),
                ["price"] = new(typeof(decimal)),
                ["is_published"] = new(typeof(bool)),
                ["tags"] = new(typeof(string[])) { IsArrayType = true, ElementSystemType = typeof(string) }
            };

            DatabaseObject dbObject = CreateTableOrViewObject(
                sourceType,
                dbColumns,
                primaryKeys: new List<string> { "id" });

            JsonElement fields = await RunDescribeEntityFieldsAsync(
                entity,
                dbObject,
                allowedColumns: new[] { "id", "title", "price", "is_published", "tags" });

            Assert.AreEqual(5, fields.GetArrayLength());
            AssertField(fields, name: "id", expectedDescription: string.Empty, expectedPrimaryKey: true, expectedDataType: "integer");
            AssertField(fields, name: "title", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "price", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "number");
            AssertField(fields, name: "is_published", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "boolean");
            AssertField(fields, name: "tags", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "array");
        }

        /// <summary>
        /// Partial config fields overlay Alias and Description onto matching DB columns while still
        /// surfacing unconfigured DB columns, preserving DB PrimaryKey when no config PrimaryKey=true
        /// is set, and ignoring bogus config fields absent from the DB schema.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_PartialConfigFields_OverlaysConfigAndSurfacesRemainingDbColumns()
        {
            List<FieldMetadata> configFields = new()
            {
                // PrimaryKey defaults to false in C# when omitted in JSON; must NOT clobber DB PK on 'id'.
                new() { Name = "id", Description = "Primary book identifier" },
                new() { Name = "publisher_id", Alias = "publisherId", Description = "Owning publisher ID" },
                // Non-existent column in config must not fabricate an entry when DB columns are authoritative.
                new() { Name = "bogus_column", Description = "Not in DB" }
            };

            Entity entity = CreateEntity(EntitySourceType.Table, fields: configFields);

            Dictionary<string, ColumnDefinition> dbColumns = new(StringComparer.InvariantCultureIgnoreCase)
            {
                ["id"] = new(typeof(int)),
                ["title"] = new(typeof(string)),
                ["publisher_id"] = new(typeof(int))
            };

            DatabaseObject dbObject = CreateTableOrViewObject(
                EntitySourceType.Table,
                dbColumns,
                primaryKeys: new List<string> { "id" });

            JsonElement fields = await RunDescribeEntityFieldsAsync(
                entity,
                dbObject,
                allowedColumns: new[] { "id", "title", "publisherId", "bogus_column" });

            Assert.AreEqual(3, fields.GetArrayLength());
            AssertField(fields, name: "id", expectedDescription: "Primary book identifier", expectedPrimaryKey: true, expectedDataType: "integer");
            AssertField(fields, name: "title", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "publisherId", expectedDescription: "Owning publisher ID", expectedPrimaryKey: false, expectedDataType: "integer");
        }

        /// <summary>
        /// When DB metadata is available, SourceDefinition.PrimaryKey (populated upstream by
        /// SqlMetadataProvider and enforced by RequestValidator.ValidatePrimaryKey) is used as
        /// the source of truth, including single-column and composite primary keys.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_PostMergeSourceDefinitionPrimaryKeys_ProjectsAuthoritativeKeys()
        {
            Dictionary<string, ColumnDefinition> dbColumns = new(StringComparer.InvariantCultureIgnoreCase)
            {
                ["id"] = new(typeof(int)),
                ["isbn"] = new(typeof(string)),
                ["publisher_id"] = new(typeof(int))
            };

            // Case 1: post-merge SourceDefinition.PrimaryKey reflects fields[].primary-key override ('isbn').
            Entity entityWithFieldPkOverride = CreateEntity(
                EntitySourceType.Table,
                fields: new List<FieldMetadata>
                {
                    new() { Name = "isbn", PrimaryKey = true, Description = "Natural key override" }
                });

            DatabaseObject tableWithMergedPk = CreateTableOrViewObject(
                EntitySourceType.Table,
                dbColumns,
                primaryKeys: new List<string> { "isbn" });

            JsonElement overriddenFields = await RunDescribeEntityFieldsAsync(
                entityWithFieldPkOverride,
                tableWithMergedPk,
                allowedColumns: new[] { "id", "isbn", "publisher_id" });

            AssertField(overriddenFields, name: "id", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "integer");
            AssertField(overriddenFields, name: "isbn", expectedDescription: "Natural key override", expectedPrimaryKey: true, expectedDataType: "string");
            AssertField(overriddenFields, name: "publisher_id", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "integer");

            // Case 2: view whose post-merge SourceDefinition.PrimaryKey reflects composite source.key-fields.
            Entity viewWithSourceKeyFields = CreateEntity(
                EntitySourceType.View,
                fields: null,
                keyFields: new[] { "id", "publisher_id" });

            DatabaseObject viewWithMergedCompositePk = CreateTableOrViewObject(
                EntitySourceType.View,
                dbColumns,
                primaryKeys: new List<string> { "id", "publisher_id" });

            JsonElement compositeKeyFields = await RunDescribeEntityFieldsAsync(
                viewWithSourceKeyFields,
                viewWithMergedCompositePk,
                allowedColumns: new[] { "id", "isbn", "publisher_id" });

            AssertField(compositeKeyFields, name: "id", expectedDescription: string.Empty, expectedPrimaryKey: true, expectedDataType: "integer");
            AssertField(compositeKeyFields, name: "isbn", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(compositeKeyFields, name: "publisher_id", expectedDescription: string.Empty, expectedPrimaryKey: true, expectedDataType: "integer");
        }

        /// <summary>
        /// Verifies DAB's exact alias precedence from SqlMetadataProvider.GenerateExposedToBackingColumnMapUtil:
        /// 1) fields[].alias overrides Mappings;
        /// 2) an entry in fields[] WITHOUT an alias claims the column under its backing name so Mappings
        ///    does not rename it behind AuthorizationResolver's back;
        /// 3) Mappings aliases columns not listed in fields[].
        /// Also verifies DbType fallbacks (Int32 -> integer, VarNumeric -> number, Object -> null).
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_MappingsAndFieldsAlias_AppliesExpectedPrecedenceAndDbTypeFallbacks()
        {
            Entity entity = CreateEntity(
                EntitySourceType.Table,
                fields: new List<FieldMetadata>
                {
                    new() { Name = "title", Alias = "bookTitle", Description = "Explicit alias in fields" },
                    new() { Name = "isbn", Description = "In fields without alias" }
                },
                mappings: new Dictionary<string, string>
                {
                    ["TITLE"] = "mappedTitle",
                    ["ISBN"] = "mappedIsbnShouldBeIgnored",
                    ["PUBLISHER_ID"] = "publisherId"
                });

            Dictionary<string, ColumnDefinition> dbColumns = new()
            {
                ["id"] = new() { DbType = DbType.Int32 },
                ["title"] = new(typeof(string)),
                ["isbn"] = new(typeof(string)),
                ["publisher_id"] = new(typeof(int)),
                ["royalty_rate"] = new() { DbType = DbType.VarNumeric },
                ["variant_payload"] = new() { DbType = DbType.Object }
            };

            DatabaseObject dbObject = CreateTableOrViewObject(
                EntitySourceType.Table,
                dbColumns,
                primaryKeys: new List<string> { "id" });

            JsonElement fields = await RunDescribeEntityFieldsAsync(
                entity,
                dbObject,
                allowedColumns: new[] { "id", "bookTitle", "isbn", "publisherId", "royalty_rate", "variant_payload" });

            Assert.AreEqual(6, fields.GetArrayLength());
            AssertField(fields, name: "id", expectedDescription: string.Empty, expectedPrimaryKey: true, expectedDataType: "integer");
            AssertField(fields, name: "bookTitle", expectedDescription: "Explicit alias in fields", expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "isbn", expectedDescription: "In fields without alias", expectedPrimaryKey: false, expectedDataType: "string");
            AssertField(fields, name: "publisherId", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "integer");
            AssertField(fields, name: "royalty_rate", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: "number");
            AssertField(fields, name: "variant_payload", expectedDescription: string.Empty, expectedPrimaryKey: false, expectedDataType: null);
        }

        /// <summary>
        /// Column-level authorization filters DB-discovered columns as well as aliased columns using
        /// exposed field names from IAuthorizationResolver.GetAllowedExposedColumns.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_ColumnLevelAuthorization_WithholdsExcludedDbColumns()
        {
            Entity entity = CreateEntity(
                EntitySourceType.Table,
                fields: new List<FieldMetadata>
                {
                    new() { Name = "publisher_id", Alias = "publisherId", Description = "Publisher FK" }
                });

            Dictionary<string, ColumnDefinition> dbColumns = new(StringComparer.InvariantCultureIgnoreCase)
            {
                ["id"] = new(typeof(int)),
                ["title"] = new(typeof(string)),
                ["publisher_id"] = new(typeof(int)),
                ["internal_cost"] = new(typeof(decimal))
            };

            DatabaseObject dbObject = CreateTableOrViewObject(
                EntitySourceType.Table,
                dbColumns,
                primaryKeys: new List<string> { "id" });

            // Role is permitted to see id, title, and aliased publisherId, but NOT internal_cost.
            JsonElement fields = await RunDescribeEntityFieldsAsync(
                entity,
                dbObject,
                allowedColumns: new[] { "id", "title", "publisherId" });

            Assert.AreEqual(3, fields.GetArrayLength());
            List<string> surfacedNames = fields.EnumerateArray()
                .Select(f => f.GetProperty("name").GetString()!)
                .ToList();

            CollectionAssert.AreEquivalent(new[] { "id", "title", "publisherId" }, surfacedNames);
            Assert.IsFalse(surfacedNames.Contains("internal_cost"), "Excluded DB column must not be surfaced.");
        }

        /// <summary>
        /// Degraded fallback: when DB metadata is unavailable (dbObject is null), config-declared
        /// fields are emitted as-is (honoring both fields[].primary-key and source.key-fields)
        /// rather than dropping the entity or returning an empty array.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_WhenDatabaseMetadataUnavailable_FallsBackToConfigFields()
        {
            Entity entity = CreateEntity(
                EntitySourceType.Table,
                fields: new List<FieldMetadata>
                {
                    new() { Name = "id", PrimaryKey = true, Description = "Config PK" },
                    new() { Name = "title", Alias = "bookTitle", Description = "Config title" }
                });

            JsonElement fields = await RunDescribeEntityFieldsAsync(
                entity,
                dbObject: null,
                allowedColumns: new[] { "id", "bookTitle" });

            Assert.AreEqual(2, fields.GetArrayLength());
            AssertField(fields, name: "id", expectedDescription: "Config PK", expectedPrimaryKey: true, expectedDataType: null);
            AssertField(fields, name: "bookTitle", expectedDescription: "Config title", expectedPrimaryKey: false, expectedDataType: null);

            // Also verify degraded fallback resolves primaryKey from source.key-fields when fields[].primary-key is omitted.
            Entity viewWithKeyFieldsFallback = CreateEntity(
                EntitySourceType.View,
                fields: new List<FieldMetadata>
                {
                    new() { Name = "id", Description = "View key" },
                    new() { Name = "title", Description = "View title" }
                },
                keyFields: new[] { "id" });

            JsonElement viewFallbackFields = await RunDescribeEntityFieldsAsync(
                viewWithKeyFieldsFallback,
                dbObject: null,
                allowedColumns: new[] { "id", "title" });

            Assert.AreEqual(2, viewFallbackFields.GetArrayLength());
            AssertField(viewFallbackFields, name: "id", expectedDescription: "View key", expectedPrimaryKey: true, expectedDataType: null);
            AssertField(viewFallbackFields, name: "title", expectedDescription: "View title", expectedPrimaryKey: false, expectedDataType: null);

            // Also verify that a partially populated DatabaseTable with zero columns falls back consistently
            // for both fields[] and primaryKey.
            DatabaseObject emptyColumnsTable = CreateTableOrViewObject(
                EntitySourceType.Table,
                columns: new Dictionary<string, ColumnDefinition>(),
                primaryKeys: new List<string>());

            JsonElement partialDbFallbackFields = await RunDescribeEntityFieldsAsync(
                entity,
                dbObject: emptyColumnsTable,
                allowedColumns: new[] { "id", "bookTitle" });

            Assert.AreEqual(2, partialDbFallbackFields.GetArrayLength());
            AssertField(partialDbFallbackFields, name: "id", expectedDescription: "Config PK", expectedPrimaryKey: true, expectedDataType: null);
            AssertField(partialDbFallbackFields, name: "bookTitle", expectedDescription: "Config title", expectedPrimaryKey: false, expectedDataType: null);
        }

        /// <summary>
        /// Exercises ISqlMetadataProvider.TryGetExposedColumnName directly:
        /// 1) When the provider returns an authoritative exposed name, it takes precedence.
        /// 2) When the provider throws KeyNotFoundException (uninitialized map), the tool gracefully
        ///    falls back to config naming without dropping the entity.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_MetadataProviderExposedColumnName_HonorsProviderAndHandlesKeyNotFound()
        {
            Entity entity = CreateEntity(
                EntitySourceType.Table,
                fields: new List<FieldMetadata>
                {
                    new() { Name = "title", Alias = "configTitle", Description = "Book title" }
                });

            Dictionary<string, ColumnDefinition> dbColumns = new(StringComparer.InvariantCultureIgnoreCase)
            {
                ["id"] = new(typeof(int)),
                ["title"] = new(typeof(string))
            };

            DatabaseObject dbObject = CreateTableOrViewObject(
                EntitySourceType.Table,
                dbColumns,
                primaryKeys: new List<string> { "id" });

            // Branch 1: provider returns authoritative exposed name ('providerTitle').
            string? outId = "id";
            string? outTitle = "providerTitle";
            JsonElement providerNamedFields = await RunDescribeEntityFieldsAsync(
                entity,
                dbObject,
                allowedColumns: new[] { "id", "providerTitle" },
                configureMetadataProvider: mock =>
                {
                    mock.Setup(p => p.TryGetExposedColumnName(TEST_ENTITY_NAME, "id", out outId)).Returns(true);
                    mock.Setup(p => p.TryGetExposedColumnName(TEST_ENTITY_NAME, "title", out outTitle)).Returns(true);
                });

            Assert.AreEqual(2, providerNamedFields.GetArrayLength());
            AssertField(providerNamedFields, name: "id", expectedDescription: string.Empty, expectedPrimaryKey: true, expectedDataType: "integer");
            AssertField(providerNamedFields, name: "providerTitle", expectedDescription: "Book title", expectedPrimaryKey: false, expectedDataType: "string");

            // Branch 2: provider throws KeyNotFoundException -> falls back to config alias ('configTitle').
            string? unusedOut = null;
            JsonElement fallbackNamedFields = await RunDescribeEntityFieldsAsync(
                entity,
                dbObject,
                allowedColumns: new[] { "id", "configTitle" },
                configureMetadataProvider: mock =>
                {
                    mock.Setup(p => p.TryGetExposedColumnName(It.IsAny<string>(), It.IsAny<string>(), out unusedOut))
                        .Throws(new KeyNotFoundException("Uninitialized column map"));
                });

            Assert.AreEqual(2, fallbackNamedFields.GetArrayLength());
            AssertField(fallbackNamedFields, name: "id", expectedDescription: string.Empty, expectedPrimaryKey: true, expectedDataType: "integer");
            AssertField(fallbackNamedFields, name: "configTitle", expectedDescription: "Book title", expectedPrimaryKey: false, expectedDataType: "string");
        }

        private static async Task<JsonElement> RunDescribeEntityFieldsAsync(
            Entity entity,
            DatabaseObject? dbObject,
            IEnumerable<string> allowedColumns,
            Action<Mock<ISqlMetadataProvider>>? configureMetadataProvider = null)
        {
            RuntimeConfig config = CreateRuntimeConfig(entity);
            ServiceCollection services = new();
            RegisterCommonServices(services, config, new HashSet<string>(allowedColumns, StringComparer.OrdinalIgnoreCase));
            RegisterMetadataProvider(services, TEST_ENTITY_NAME, dbObject, configureMetadataProvider);

            IServiceProvider serviceProvider = services.BuildServiceProvider();
            DescribeEntitiesTool tool = new();

            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);
            Assert.IsTrue(result.IsError == false || result.IsError == null);

            JsonElement entityJson = GetSingleEntityFromResult(result);
            return entityJson.GetProperty("fields");
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

            Assert.AreEqual(expectedDescription, field.GetProperty("description").GetString(), $"description mismatch for field '{name}'.");
            Assert.AreEqual(expectedPrimaryKey, field.GetProperty("primaryKey").GetBoolean(), $"primaryKey mismatch for field '{name}'.");

            JsonElement dataTypeElement = field.GetProperty("dataType");
            if (expectedDataType is null)
            {
                Assert.AreEqual(JsonValueKind.Null, dataTypeElement.ValueKind, $"dataType should be JSON null for field '{name}'.");
            }
            else
            {
                Assert.AreEqual(expectedDataType, dataTypeElement.GetString(), $"dataType mismatch for field '{name}'.");
            }
        }

        private static Entity CreateEntity(
            EntitySourceType sourceType,
            List<FieldMetadata>? fields,
            string[]? keyFields = null,
            Dictionary<string, string>? mappings = null)
        {
            return new Entity(
                Source: new("books", sourceType, Parameters: null, KeyFields: keyFields),
                GraphQL: new(TEST_ENTITY_NAME, $"{TEST_ENTITY_NAME}s"),
                Fields: fields,
                Rest: new(Enabled: true),
                Permissions: new[]
                {
                    new EntityPermission(
                        Role: AuthorizationResolver.ROLE_ANONYMOUS,
                        Actions: new[]
                        {
                            new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null)
                        })
                },
                Mappings: mappings,
                Relationships: null,
                Mcp: null);
        }

        private static DatabaseObject CreateTableOrViewObject(
            EntitySourceType sourceType,
            Dictionary<string, ColumnDefinition> columns,
            List<string> primaryKeys)
        {
            if (sourceType == EntitySourceType.View)
            {
                ViewDefinition viewDefinition = new()
                {
                    PrimaryKey = primaryKeys
                };

                foreach ((string colName, ColumnDefinition colDef) in columns)
                {
                    viewDefinition.Columns[colName] = colDef;
                }

                return new DatabaseView("dbo", "books")
                {
                    SourceType = EntitySourceType.View,
                    ViewDefinition = viewDefinition
                };
            }

            SourceDefinition tableDefinition = new()
            {
                PrimaryKey = primaryKeys
            };

            foreach ((string colName, ColumnDefinition colDef) in columns)
            {
                tableDefinition.Columns[colName] = colDef;
            }

            return new DatabaseTable("dbo", "books")
            {
                SourceType = EntitySourceType.Table,
                TableDefinition = tableDefinition
            };
        }

        private static RuntimeConfig CreateRuntimeConfig(Entity entity)
        {
            Dictionary<string, Entity> entities = new()
            {
                [TEST_ENTITY_NAME] = entity
            };

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(Enabled: true, Path: "/mcp", DmlTools: null),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
                Entities: new(entities));
        }

        private static void RegisterCommonServices(
            ServiceCollection services,
            RuntimeConfig config,
            HashSet<string> allowedColumns)
        {
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            services.AddSingleton(configProvider);

            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = AuthorizationResolver.ROLE_ANONYMOUS;
            httpContext.User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Role, AuthorizationResolver.ROLE_ANONYMOUS) },
                authenticationType: "TestAuth"));

            Mock<IAuthorizationResolver> mockAuthResolver = new();
            mockAuthResolver
                .Setup(x => x.IsValidRoleContext(It.IsAny<HttpContext>()))
                .Returns((HttpContext ctx) =>
                {
                    string headerValue = ctx.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].ToString();
                    return !string.IsNullOrWhiteSpace(headerValue)
                        && ctx.User is not null
                        && ctx.User.IsInRole(headerValue);
                });
            mockAuthResolver
                .Setup(x => x.AreRoleAndOperationDefinedForEntity(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EntityActionOperation>()))
                .Returns((string entityName, string roleName, EntityActionOperation operation) =>
                    string.Equals(entityName, TEST_ENTITY_NAME, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(roleName, AuthorizationResolver.ROLE_ANONYMOUS, StringComparison.OrdinalIgnoreCase)
                    && operation == EntityActionOperation.Read);
            mockAuthResolver
                .Setup(x => x.GetAllowedExposedColumns(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EntityActionOperation>()))
                .Returns(allowedColumns);
            services.AddSingleton(mockAuthResolver.Object);

            Mock<IHttpContextAccessor> mockHttpContextAccessor = new();
            mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
            services.AddSingleton(mockHttpContextAccessor.Object);

            services.AddLogging();
        }

        private static void RegisterMetadataProvider(
            ServiceCollection services,
            string entityName,
            DatabaseObject? dbObject,
            Action<Mock<ISqlMetadataProvider>>? configureMetadataProvider = null)
        {
            Dictionary<string, DatabaseObject> entityMap = dbObject is null
                ? new Dictionary<string, DatabaseObject>()
                : new Dictionary<string, DatabaseObject>(StringComparer.OrdinalIgnoreCase) { [entityName] = dbObject };

            Mock<ISqlMetadataProvider> mockSqlMetadataProvider = new();
            mockSqlMetadataProvider.Setup(x => x.EntityToDatabaseObject).Returns(entityMap);
            mockSqlMetadataProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.MSSQL);
            configureMetadataProvider?.Invoke(mockSqlMetadataProvider);

            Mock<IMetadataProviderFactory> mockMetadataProviderFactory = new();
            mockMetadataProviderFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(mockSqlMetadataProvider.Object);
            services.AddSingleton(mockMetadataProviderFactory.Object);
        }

        private static JsonElement GetSingleEntityFromResult(CallToolResult result)
        {
            Assert.IsNotNull(result.Content);
            Assert.IsTrue(result.Content.Count > 0);
            Assert.IsInstanceOfType(result.Content[0], typeof(TextContentBlock));

            string text = ((TextContentBlock)result.Content[0]).Text;
            JsonElement root = JsonDocument.Parse(text).RootElement;
            JsonElement entities = root.GetProperty("entities");
            Assert.AreEqual(1, entities.GetArrayLength());
            return entities[0];
        }
    }
}
