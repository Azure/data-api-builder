// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for <see cref="OpenApiDocumentor"/> that exercise document generation
    /// against an in-memory (mocked) metadata provider, avoiding any live database dependency.
    /// </summary>
    [TestClass]
    public class OpenApiDocumentorUnitTests
    {
        private const string ENTITY_NAME = "Book";

        [TestMethod]
        public void TryGetDocument_BeforeGeneration_ReturnsFalse()
        {
            OpenApiDocumentor documentor = CreateDocumentorWithBookTable();

            Assert.IsFalse(documentor.TryGetDocument(out string? document));
            Assert.IsNull(document);
        }

        [TestMethod]
        public void CreateDocument_RestDisabledGlobally_Throws()
        {
            OpenApiDocumentor documentor = CreateDocumentorWithBookTable(restEnabledGlobally: false);

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => documentor.CreateDocument());

            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.GlobalRestEndpointDisabled, ex.SubStatusCode);
        }

        [TestMethod]
        public void CreateDocument_TableEntity_GeneratesExpectedPathsAndSchemas()
        {
            OpenApiDocumentor documentor = CreateDocumentorWithBookTable();

            documentor.CreateDocument();

            Assert.IsTrue(documentor.TryGetDocument(out string? document));
            using JsonDocument doc = JsonDocument.Parse(document!);
            JsonElement root = doc.RootElement;

            JsonElement paths = root.GetProperty("paths");
            // Path excluding primary key exposes GET(all) and POST.
            JsonElement basePath = paths.GetProperty("/Book");
            Assert.IsTrue(basePath.TryGetProperty("get", out _));
            Assert.IsTrue(basePath.TryGetProperty("post", out _));

            // Path including primary key exposes GET(one), PUT, PATCH, DELETE.
            JsonElement pkPath = paths.GetProperty("/Book/id/{id}");
            Assert.IsTrue(pkPath.TryGetProperty("get", out _));
            Assert.IsTrue(pkPath.TryGetProperty("put", out _));
            Assert.IsTrue(pkPath.TryGetProperty("patch", out _));
            Assert.IsTrue(pkPath.TryGetProperty("delete", out _));

            JsonElement schemas = root.GetProperty("components").GetProperty("schemas");
            Assert.IsTrue(schemas.TryGetProperty("Book", out _));
            // Request-body schemas generated for mutation operations under strict mode.
            Assert.IsTrue(schemas.TryGetProperty("Book_NoAutoPK", out _));
            Assert.IsTrue(schemas.TryGetProperty("Book_NoPK", out _));
        }

        [TestMethod]
        public void CreateDocument_CalledTwiceWithoutOverride_Throws()
        {
            OpenApiDocumentor documentor = CreateDocumentorWithBookTable();
            documentor.CreateDocument();

            DataApiBuilderException ex = Assert.ThrowsException<DataApiBuilderException>(
                () => documentor.CreateDocument());

            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.OpenApiDocumentAlreadyExists, ex.SubStatusCode);
        }

        [TestMethod]
        public void CreateDocument_WithOverride_Regenerates()
        {
            OpenApiDocumentor documentor = CreateDocumentorWithBookTable();
            documentor.CreateDocument();

            // Should not throw when override is requested.
            documentor.CreateDocument(doOverrideExistingDocument: true);

            Assert.IsTrue(documentor.TryGetDocument(out _));
        }

        [TestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("   ")]
        public void TryGetDocumentForRole_NullOrWhitespace_ReturnsFalse(string role)
        {
            OpenApiDocumentor documentor = CreateDocumentorWithBookTable();

            Assert.IsFalse(documentor.TryGetDocumentForRole(role, out string? document));
            Assert.IsNull(document);
        }

        [TestMethod]
        public void TryGetDocumentForRole_UnknownRole_ReturnsFalse()
        {
            OpenApiDocumentor documentor = CreateDocumentorWithBookTable();

            Assert.IsFalse(documentor.TryGetDocumentForRole("nonexistent-role", out string? document));
            Assert.IsNull(document);
        }

        [TestMethod]
        public void TryGetDocumentForRole_KnownRole_ReturnsDocument()
        {
            OpenApiDocumentor documentor = CreateDocumentorWithBookTable();

            Assert.IsTrue(documentor.TryGetDocumentForRole("anonymous", out string? document));
            Assert.IsNotNull(document);

            // Second call should hit the role-specific document cache.
            Assert.IsTrue(documentor.TryGetDocumentForRole("anonymous", out string? cachedDocument));
            Assert.AreEqual(document, cachedDocument);
        }

        [TestMethod]
        public void CreateDocument_EntityWithRestDisabled_ExcludedFromPaths()
        {
            Dictionary<string, Entity> entities = new()
            {
                [ENTITY_NAME] = CreateTableEntity(restEnabled: false)
            };
            OpenApiDocumentor documentor = CreateDocumentor(entities, CreateBookMetadata());

            documentor.CreateDocument();

            Assert.IsTrue(documentor.TryGetDocument(out string? document));
            using JsonDocument doc = JsonDocument.Parse(document!);
            JsonElement paths = doc.RootElement.GetProperty("paths");
            Assert.AreEqual(0, paths.EnumerateObject().Count());
        }

        [TestMethod]
        public void CreateDocument_ReadOnlyPermission_OnlyExposesGetOperations()
        {
            Entity readOnlyEntity = new(
                Source: new(ENTITY_NAME, EntitySourceType.Table, null, null),
                GraphQL: new(ENTITY_NAME, ENTITY_NAME + "s"),
                Fields: null,
                Rest: new(Enabled: true),
                Permissions: new[]
                {
                    new EntityPermission(Role: "anonymous", Actions: new[]
                    {
                        new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null)
                    })
                },
                Mappings: null,
                Relationships: null,
                Mcp: null);

            Dictionary<string, Entity> entities = new() { [ENTITY_NAME] = readOnlyEntity };
            OpenApiDocumentor documentor = CreateDocumentor(entities, CreateBookMetadata());

            documentor.CreateDocument();

            Assert.IsTrue(documentor.TryGetDocument(out string? document));
            using JsonDocument doc = JsonDocument.Parse(document!);
            JsonElement paths = doc.RootElement.GetProperty("paths");

            JsonElement basePath = paths.GetProperty("/Book");
            Assert.IsTrue(basePath.TryGetProperty("get", out _));
            Assert.IsFalse(basePath.TryGetProperty("post", out _));

            JsonElement pkPath = paths.GetProperty("/Book/id/{id}");
            Assert.IsTrue(pkPath.TryGetProperty("get", out _));
            Assert.IsFalse(pkPath.TryGetProperty("put", out _));
            Assert.IsFalse(pkPath.TryGetProperty("delete", out _));
        }

        [TestMethod]
        public void CreateDocument_StoredProcedureEntity_GeneratesExecutePathAndSchemas()
        {
            Entity spEntity = new(
                Source: new("get_book", EntitySourceType.StoredProcedure, null, null),
                GraphQL: new("GetBook", "GetBook"),
                Fields: null,
                Rest: new(Enabled: true),
                Permissions: new[]
                {
                    new EntityPermission(Role: "anonymous", Actions: new[]
                    {
                        new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null)
                    })
                },
                Mappings: null,
                Relationships: null,
                Mcp: null);

            StoredProcedureDefinition spDefinition = new()
            {
                Parameters = new Dictionary<string, ParameterDefinition>
                {
                    ["id"] = new ParameterDefinition { SystemType = typeof(int), Required = true }
                }
            };
            spDefinition.Columns.Add("id", new ColumnDefinition { SystemType = typeof(int) });
            spDefinition.Columns.Add("title", new ColumnDefinition { SystemType = typeof(string) });

            DatabaseStoredProcedure dbSp = new(schemaName: "dbo", tableName: "get_book")
            {
                SourceType = EntitySourceType.StoredProcedure,
                StoredProcedureDefinition = spDefinition
            };

            Dictionary<string, DatabaseObject> dbObjects = new() { ["GetBook"] = dbSp };
            Dictionary<string, Entity> entities = new() { ["GetBook"] = spEntity };
            OpenApiDocumentor documentor = CreateDocumentor(entities, dbObjects, new[] { "id", "title" });

            documentor.CreateDocument();

            Assert.IsTrue(documentor.TryGetDocument(out string? document));
            using JsonDocument doc = JsonDocument.Parse(document!);
            JsonElement root = doc.RootElement;

            // Default SP REST method is POST.
            JsonElement spPath = root.GetProperty("paths").GetProperty("/GetBook");
            Assert.IsTrue(spPath.TryGetProperty("post", out _));

            JsonElement schemas = root.GetProperty("components").GetProperty("schemas");
            Assert.IsTrue(schemas.TryGetProperty("GetBook_sp_request", out _));
            Assert.IsTrue(schemas.TryGetProperty("GetBook_sp_response", out _));
        }

        [TestMethod]
        public void CreateDocument_ViewEntity_GeneratesPaths()
        {
            ViewDefinition sourceDefinition = new() { PrimaryKey = new List<string> { "id" } };
            sourceDefinition.Columns.Add("id", new ColumnDefinition { SystemType = typeof(int), IsAutoGenerated = true });
            sourceDefinition.Columns.Add("title", new ColumnDefinition { SystemType = typeof(string) });

            DatabaseView dbView = new(schemaName: "dbo", tableName: "book_view")
            {
                SourceType = EntitySourceType.View,
                ViewDefinition = sourceDefinition
            };

            Entity viewEntity = new(
                Source: new("book_view", EntitySourceType.View, null, null),
                GraphQL: new("BookView", "BookViews"),
                Fields: null,
                Rest: new(Enabled: true),
                Permissions: new[]
                {
                    new EntityPermission(Role: "anonymous", Actions: new[]
                    {
                        new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null)
                    })
                },
                Mappings: null,
                Relationships: null,
                Mcp: null);

            Dictionary<string, DatabaseObject> dbObjects = new() { ["BookView"] = dbView };
            Dictionary<string, Entity> entities = new() { ["BookView"] = viewEntity };
            OpenApiDocumentor documentor = CreateDocumentor(entities, dbObjects, new[] { "id", "title" });

            documentor.CreateDocument();

            Assert.IsTrue(documentor.TryGetDocument(out string? document));
            using JsonDocument doc = JsonDocument.Parse(document!);
            JsonElement paths = doc.RootElement.GetProperty("paths");
            // Read-only view exposes GET operations only.
            Assert.IsTrue(paths.GetProperty("/BookView").TryGetProperty("get", out _));
            Assert.IsFalse(paths.GetProperty("/BookView").TryGetProperty("post", out _));
        }

        [TestMethod]
        public void CreateDocument_MultipleEntities_GeneratesPathsForEach()
        {
            Dictionary<string, DatabaseObject> dbObjects = CreateBookMetadata();
            dbObjects["Author"] = new DatabaseTable(schemaName: "dbo", tableName: "authors")
            {
                SourceType = EntitySourceType.Table,
                TableDefinition = CreateSimpleSourceDefinition()
            };

            Dictionary<string, Entity> entities = new()
            {
                [ENTITY_NAME] = CreateTableEntity(restEnabled: true),
                ["Author"] = new Entity(
                    Source: new("Author", EntitySourceType.Table, null, null),
                    GraphQL: new("Author", "Authors"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[]
                    {
                        new EntityPermission(Role: "anonymous", Actions: new[]
                        {
                            new EntityAction(Action: EntityActionOperation.All, Fields: null, Policy: null)
                        })
                    },
                    Mappings: null,
                    Relationships: null,
                    Mcp: null)
            };

            OpenApiDocumentor documentor = CreateDocumentor(entities, dbObjects);
            documentor.CreateDocument();

            Assert.IsTrue(documentor.TryGetDocument(out string? document));
            using JsonDocument doc = JsonDocument.Parse(document!);
            JsonElement paths = doc.RootElement.GetProperty("paths");
            Assert.IsTrue(paths.TryGetProperty("/Book", out _));
            Assert.IsTrue(paths.TryGetProperty("/Author", out _));
        }

        [TestMethod]
        public void CreateDocument_CustomRestPath_UsesConfiguredPath()
        {
            Entity entity = new(
                Source: new(ENTITY_NAME, EntitySourceType.Table, null, null),
                GraphQL: new(ENTITY_NAME, ENTITY_NAME + "s"),
                Fields: null,
                Rest: new(Enabled: true) { Path = "/customBooks" },
                Permissions: new[]
                {
                    new EntityPermission(Role: "anonymous", Actions: new[]
                    {
                        new EntityAction(Action: EntityActionOperation.All, Fields: null, Policy: null)
                    })
                },
                Mappings: null,
                Relationships: null,
                Mcp: null);

            Dictionary<string, Entity> entities = new() { [ENTITY_NAME] = entity };
            OpenApiDocumentor documentor = CreateDocumentor(entities, CreateBookMetadata());
            documentor.CreateDocument();

            Assert.IsTrue(documentor.TryGetDocument(out string? document));
            using JsonDocument doc = JsonDocument.Parse(document!);
            JsonElement paths = doc.RootElement.GetProperty("paths");
            Assert.IsTrue(paths.TryGetProperty("/customBooks", out _));
            Assert.IsFalse(paths.TryGetProperty("/Book", out _));
        }

        [TestMethod]
        public void CreateDocument_RequestBodyStrictFalse_OmitsStrictRequestBodySchemas()
        {
            Dictionary<string, Entity> entities = new() { [ENTITY_NAME] = CreateTableEntity(restEnabled: true) };
            OpenApiDocumentor documentor = CreateDocumentor(entities, CreateBookMetadata(), requestBodyStrict: false);
            documentor.CreateDocument();

            Assert.IsTrue(documentor.TryGetDocument(out string? document));
            using JsonDocument doc = JsonDocument.Parse(document!);
            JsonElement schemas = doc.RootElement.GetProperty("components").GetProperty("schemas");
            // Non-strict mode does not generate the separate strict request-body schemas.
            Assert.IsTrue(schemas.TryGetProperty("Book", out _));
            Assert.IsFalse(schemas.TryGetProperty("Book_NoAutoPK", out _));
            Assert.IsFalse(schemas.TryGetProperty("Book_NoPK", out _));
        }

        [TestMethod]
        public void CreateDocument_WithBaseRoute_ServerUrlIncludesBaseRoute()
        {
            Dictionary<string, Entity> entities = new() { [ENTITY_NAME] = CreateTableEntity(restEnabled: true) };
            OpenApiDocumentor documentor = CreateDocumentor(entities, CreateBookMetadata(), baseRoute: "base");
            documentor.CreateDocument();

            Assert.IsTrue(documentor.TryGetDocument(out string? document));
            using JsonDocument doc = JsonDocument.Parse(document!);
            string serverUrl = doc.RootElement.GetProperty("servers")[0].GetProperty("url").GetString()!;
            StringAssert.Contains(serverUrl, "base");
        }

        #region Helpers

        private static SourceDefinition CreateSimpleSourceDefinition()
        {
            SourceDefinition sourceDefinition = new() { PrimaryKey = new List<string> { "id" } };
            sourceDefinition.Columns.Add("id", new ColumnDefinition { SystemType = typeof(int), IsAutoGenerated = true });
            sourceDefinition.Columns.Add("title", new ColumnDefinition { SystemType = typeof(string) });
            sourceDefinition.Columns.Add("publisher_id", new ColumnDefinition { SystemType = typeof(int), IsNullable = true });
            return sourceDefinition;
        }

        private static OpenApiDocumentor CreateDocumentorWithBookTable(bool restEnabledGlobally = true)
        {
            Dictionary<string, Entity> entities = new() { [ENTITY_NAME] = CreateTableEntity(restEnabled: true) };
            return CreateDocumentor(entities, CreateBookMetadata(), restEnabledGlobally: restEnabledGlobally);
        }

        private static Entity CreateTableEntity(bool restEnabled)
        {
            return new Entity(
                Source: new(ENTITY_NAME, EntitySourceType.Table, null, null),
                GraphQL: new(ENTITY_NAME, ENTITY_NAME + "s"),
                Fields: null,
                Rest: new(Enabled: restEnabled),
                Permissions: new[]
                {
                    new EntityPermission(Role: "anonymous", Actions: new[]
                    {
                        new EntityAction(Action: EntityActionOperation.All, Fields: null, Policy: null)
                    })
                },
                Mappings: null,
                Relationships: null,
                Mcp: null);
        }

        /// <summary>
        /// Builds a Book table database object with an auto-generated integer primary key
        /// and a couple of scalar columns.
        /// </summary>
        private static Dictionary<string, DatabaseObject> CreateBookMetadata()
        {
            SourceDefinition sourceDefinition = new()
            {
                PrimaryKey = new List<string> { "id" }
            };
            sourceDefinition.Columns.Add("id", new ColumnDefinition { SystemType = typeof(int), IsAutoGenerated = true });
            sourceDefinition.Columns.Add("title", new ColumnDefinition { SystemType = typeof(string) });
            sourceDefinition.Columns.Add("publisher_id", new ColumnDefinition { SystemType = typeof(int), IsNullable = true });

            DatabaseTable dbTable = new(schemaName: "dbo", tableName: "books")
            {
                SourceType = EntitySourceType.Table,
                TableDefinition = sourceDefinition
            };

            return new Dictionary<string, DatabaseObject> { [ENTITY_NAME] = dbTable };
        }

        private static OpenApiDocumentor CreateDocumentor(
            Dictionary<string, Entity> entities,
            Dictionary<string, DatabaseObject> dbObjects,
            IEnumerable<string>? columnNames = null,
            bool restEnabledGlobally = true,
            bool requestBodyStrict = true,
            string? baseRoute = null)
        {
            Mock<ISqlMetadataProvider> mockMetadataProvider = new();
            mockMetadataProvider.Setup(x => x.EntityToDatabaseObject).Returns(dbObjects);
            mockMetadataProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.MSSQL);

            foreach (KeyValuePair<string, DatabaseObject> entry in dbObjects)
            {
                DatabaseObject dbObject = entry.Value;
                mockMetadataProvider.Setup(x => x.GetSourceDefinition(entry.Key)).Returns(dbObject.SourceDefinition);
            }

            // Column-name identity mappings (no aliasing) required by schema/path generation.
            IEnumerable<string> columns = columnNames ?? new[] { "id", "title", "publisher_id" };
            foreach (string column in columns)
            {
                string exposedName = column;
                string backingName = column;
                mockMetadataProvider
                    .Setup(x => x.TryGetExposedColumnName(It.IsAny<string>(), column, out exposedName))
                    .Returns(true);
                mockMetadataProvider
                    .Setup(x => x.TryGetBackingColumn(It.IsAny<string>(), column, out backingName))
                    .Returns(true);
            }

            Mock<IMetadataProviderFactory> mockFactory = new();
            mockFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(mockMetadataProvider.Object);

            RuntimeConfig config = CreateConfig(entities, restEnabledGlobally, requestBodyStrict, baseRoute);
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);

            return new OpenApiDocumentor(
                mockFactory.Object,
                configProvider,
                handler: null,
                logger: NullLogger<OpenApiDocumentor>.Instance);
        }

        private static RuntimeConfig CreateConfig(Dictionary<string, Entity> entities, bool restEnabledGlobally, bool requestBodyStrict = true, string? baseRoute = null)
        {
            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: string.Empty, Options: null),
                Runtime: new(
                    Rest: new(Enabled: restEnabledGlobally, RequestBodyStrict: requestBodyStrict),
                    GraphQL: new(),
                    Mcp: null,
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development),
                    BaseRoute: baseRoute),
                Entities: new(entities));
        }

        #endregion
    }
}
