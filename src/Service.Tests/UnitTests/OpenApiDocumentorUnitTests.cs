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

        #region Helpers

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
            bool restEnabledGlobally = true)
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

            RuntimeConfig config = CreateConfig(entities, restEnabledGlobally);
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);

            return new OpenApiDocumentor(
                mockFactory.Object,
                configProvider,
                handler: null,
                logger: NullLogger<OpenApiDocumentor>.Instance);
        }

        private static RuntimeConfig CreateConfig(Dictionary<string, Entity> entities, bool restEnabledGlobally)
        {
            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: string.Empty, Options: null),
                Runtime: new(
                    Rest: new(Enabled: restEnabledGlobally),
                    GraphQL: new(),
                    Mcp: null,
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
                Entities: new(entities));
        }

        #endregion
    }
}
