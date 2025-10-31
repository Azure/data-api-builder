// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.OpenApi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OpenApiIntegration
{
    /// <summary>
    /// Tests validating conditional schema generation based on configured Methods.
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class SchemaGenerationTests
    {
        private const string CUSTOM_CONFIG = "method-filtering-config.MsSql.json";
        private const string MSSQL_ENVIRONMENT = TestCategory.MSSQL;

        /// <summary>
        /// Validates that full CRUD entity generates all three schemas: base, _NoAutoPK, and _NoPK.
        /// </summary>
        [TestMethod]
        public async Task FullCrudEntity_GeneratesAllSchemas()
        {
            // Arrange: Entity with all methods configured
            Entity entity = new(
                Source: new(Object: "books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
                Mappings: null,
                Relationships: null,
                Description: null);

            Dictionary<string, Entity> entities = new()
            {
                { "FullCrudBook", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert: All three schemas should exist
            Assert.IsTrue(openApiDocument.Components.Schemas.ContainsKey("FullCrudBook"), "Base schema should exist");
            Assert.IsTrue(openApiDocument.Components.Schemas.ContainsKey("FullCrudBook_NoAutoPK"), "_NoAutoPK schema should exist for POST");
            Assert.IsTrue(openApiDocument.Components.Schemas.ContainsKey("FullCrudBook_NoPK"), "_NoPK schema should exist for PUT/PATCH");
        }

        /// <summary>
        /// Validates that read-only entity (GET only) generates only base schema, no _NoAutoPK or _NoPK.
        /// </summary>
        [TestMethod]
        public async Task ReadOnlyEntity_GeneratesOnlyBaseSchema()
        {
            // Arrange: Entity with only GET method configured
            Entity entity = new(
                Source: new(Object: "books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: new[] { SupportedHttpVerb.Get }),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
                Mappings: null,
                Relationships: null,
                Description: null);

            Dictionary<string, Entity> entities = new()
            {
                { "ReadOnlyBook", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert: Only base schema should exist
            Assert.IsTrue(openApiDocument.Components.Schemas.ContainsKey("ReadOnlyBook"), "Base schema should exist");
            Assert.IsFalse(openApiDocument.Components.Schemas.ContainsKey("ReadOnlyBook_NoAutoPK"), "_NoAutoPK schema should NOT exist (no POST)");
            Assert.IsFalse(openApiDocument.Components.Schemas.ContainsKey("ReadOnlyBook_NoPK"), "_NoPK schema should NOT exist (no PUT/PATCH)");
        }

        /// <summary>
        /// Validates that write-only entity (POST only) generates base and _NoAutoPK schemas, but not _NoPK.
        /// </summary>
        [TestMethod]
        public async Task WriteOnlyEntity_GeneratesBaseAndNoAutoPKSchemas()
        {
            // Arrange: Entity with only POST method configured
            Entity entity = new(
                Source: new(Object: "books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: new[] { SupportedHttpVerb.Post }),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
                Mappings: null,
                Relationships: null,
                Description: null);

            Dictionary<string, Entity> entities = new()
            {
                { "WriteOnlyBook", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert
            Assert.IsTrue(openApiDocument.Components.Schemas.ContainsKey("WriteOnlyBook"), "Base schema should exist");
            Assert.IsTrue(openApiDocument.Components.Schemas.ContainsKey("WriteOnlyBook_NoAutoPK"), "_NoAutoPK schema should exist for POST");
            Assert.IsFalse(openApiDocument.Components.Schemas.ContainsKey("WriteOnlyBook_NoPK"), "_NoPK schema should NOT exist (no PUT/PATCH)");
        }

        /// <summary>
        /// Validates that update-only entity (PUT/PATCH) generates base and _NoPK schemas, but not _NoAutoPK.
        /// </summary>
        [TestMethod]
        public async Task UpdateOnlyEntity_GeneratesBaseAndNoPKSchemas()
        {
            // Arrange: Entity with only PUT and PATCH methods configured
            Entity entity = new(
                Source: new(Object: "books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: new[] { SupportedHttpVerb.Put, SupportedHttpVerb.Patch }),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
                Mappings: null,
                Relationships: null,
                Description: null);

            Dictionary<string, Entity> entities = new()
            {
                { "UpdateOnlyBook", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert
            Assert.IsTrue(openApiDocument.Components.Schemas.ContainsKey("UpdateOnlyBook"), "Base schema should exist");
            Assert.IsFalse(openApiDocument.Components.Schemas.ContainsKey("UpdateOnlyBook_NoAutoPK"), "_NoAutoPK schema should NOT exist (no POST)");
            Assert.IsTrue(openApiDocument.Components.Schemas.ContainsKey("UpdateOnlyBook_NoPK"), "_NoPK schema should exist for PUT/PATCH");
        }

        /// <summary>
        /// Validates that partial CRUD entity (GET + PUT) generates base and _NoPK schemas.
        /// </summary>
        [TestMethod]
        public async Task PartialCrudEntity_GeneratesBaseAndNoPKSchemas()
        {
            // Arrange: Entity with GET and PUT methods configured
            Entity entity = new(
                Source: new(Object: "books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: new[] { SupportedHttpVerb.Get, SupportedHttpVerb.Put }),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
                Mappings: null,
                Relationships: null,
                Description: null);

            Dictionary<string, Entity> entities = new()
            {
                { "PartialCrudBook", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert
            Assert.IsTrue(openApiDocument.Components.Schemas.ContainsKey("PartialCrudBook"), "Base schema should exist");
            Assert.IsFalse(openApiDocument.Components.Schemas.ContainsKey("PartialCrudBook_NoAutoPK"), "_NoAutoPK schema should NOT exist (no POST)");
            Assert.IsTrue(openApiDocument.Components.Schemas.ContainsKey("PartialCrudBook_NoPK"), "_NoPK schema should exist for PUT");
        }
    }
}
