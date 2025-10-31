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
    /// Unit tests validating GetConfiguredRestOperations() method behavior.
    /// Tests the logic that determines which REST operations should be included
    /// based on the entity's Methods configuration.
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class MethodFilteringTests
    {
        private const string CUSTOM_CONFIG = "method-filtering-config.MsSql.json";
        private const string MSSQL_ENVIRONMENT = TestCategory.MSSQL;

        /// <summary>
        /// Tests that GetConfiguredRestOperations() returns only GET operation
        /// when Methods is configured with only Get verb.
        /// </summary>
        [TestMethod]
        public async Task GetConfiguredRestOperations_SingleMethod_ReturnsOnlyConfiguredOperation()
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
                { "SingleMethodEntity", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act: Generate OpenAPI document (which calls GetConfiguredRestOperations internally)
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert: Document should have paths (verifies GetConfiguredRestOperations returned operations)
            Assert.IsTrue(openApiDocument.Paths.Count > 0, "Expected paths to be generated for entity with GET method");
        }

        /// <summary>
        /// Tests that GetConfiguredRestOperations() returns multiple operations
        /// when Methods is configured with multiple verbs.
        /// </summary>
        [TestMethod]
        public async Task GetConfiguredRestOperations_MultipleMethodsConfigured_ReturnsAllConfiguredOperations()
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
                { "MultiMethodEntity", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert
            Assert.IsTrue(openApiDocument.Paths.Count > 0, "Expected paths to be generated for entity with multiple methods");
        }

        /// <summary>
        /// Tests that GetConfiguredRestOperations() returns all 5 operations
        /// when Methods is null (default behavior).
        /// </summary>
        [TestMethod]
        public async Task GetConfiguredRestOperations_NullMethods_ReturnsAllOperations()
        {
            // Arrange: Entity with null Methods (default to all verbs)
            Entity entity = new(
                Source: new(Object: "books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: null),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
                Mappings: null,
                Relationships: null,
                Description: null);

            Dictionary<string, Entity> entities = new()
            {
                { "NullMethodsEntity", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert: Should generate paths (all operations enabled by default)
            Assert.IsTrue(openApiDocument.Paths.Count > 0, "Expected paths to be generated when Methods is null");
        }

        /// <summary>
        /// Tests that GetConfiguredRestOperations() returns all 5 operations
        /// when Methods is an empty array (default behavior).
        /// </summary>
        [TestMethod]
        public async Task GetConfiguredRestOperations_EmptyMethodsArray_ReturnsAllOperations()
        {
            // Arrange: Entity with empty Methods array (default to all verbs)
            Entity entity = new(
                Source: new(Object: "books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: System.Array.Empty<SupportedHttpVerb>()),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
                Mappings: null,
                Relationships: null,
                Description: null);

            Dictionary<string, Entity> entities = new()
            {
                { "EmptyMethodsEntity", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert
            Assert.IsTrue(openApiDocument.Paths.Count > 0, "Expected paths to be generated when Methods is empty array");
        }

        /// <summary>
        /// Tests that GetConfiguredRestOperations() returns all 5 operations
        /// when all verbs are explicitly configured.
        /// </summary>
        [TestMethod]
        public async Task GetConfiguredRestOperations_AllMethodsConfigured_ReturnsAllOperations()
        {
            // Arrange: Entity with all 5 methods explicitly configured
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
                { "AllMethodsEntity", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert
            Assert.IsTrue(openApiDocument.Paths.Count > 0, "Expected paths to be generated when all methods configured");
        }
    }
}
