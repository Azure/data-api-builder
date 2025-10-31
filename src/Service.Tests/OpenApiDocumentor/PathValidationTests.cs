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
    /// Integration tests validating correct keys are created for OpenApiDocument.Paths
    /// which represent the path used to access an entity in DAB's REST API endpoint.
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class PathValidationTests
    {
        private const string CUSTOM_CONFIG = "path-config.MsSql.json";
        private const string MSSQL_ENVIRONMENT = TestCategory.MSSQL;

        // Error messages
        private const string PATH_GENERATION_ERROR = "Unexpected path value for entity in OpenAPI description document: ";

        /// <summary>
        /// Validates that the OpenApiDocument object's Paths property for an entity is generated
        /// with the entity's explicitly configured REST path, if set. Otherwise, the top level
        /// entity name is used.
        /// When OpenApiDocumentor.BuildPaths() is called, the entityBasePathComponent is created using
        /// the formula "/{entityRestPath}" where {entityRestPath} has no starting slashes and is either
        /// the entity name or the explicitly configured entity REST path
        /// </summary>
        /// <param name="entityName">Top level entity name defined in runtime config.</param>
        /// <param name="configuredRestPath">Entity's configured REST path.</param>
        /// <param name="expectedOpenApiPath">Expected path generated for OpenApiDocument.Paths with format: "/{entityRestPath}"</param>
        [DataRow("entity", "/customEntityPath", "/customEntityPath", DisplayName = "Entity REST path has leading slash - REST path override used.")]
        [DataRow("entity", "//customEntityPath", "/customEntityPath", DisplayName = "Entity REST path has two leading slashes - REST path override used.")]
        [DataRow("entity", "///customEntityPath", "/customEntityPath", DisplayName = "Entity REST path has many leading slashes - REST path override used.")]
        [DataRow("entity", "customEntityPath", "/customEntityPath", DisplayName = "Entity REST path has no leading slash(es) - REST path override used.")]
        [DataRow("entity", "", "/entity", DisplayName = "Entity REST path is an emtpy string - top level entity name used.")]
        [DataRow("entity", null, "/entity", DisplayName = "Entity REST path is null - top level entity name used.")]
        [DataTestMethod]
        public async Task ValidateEntityRestPath(string entityName, string configuredRestPath, string expectedOpenApiPath)
        {
            Entity entity = new(
                Source: new(Object: "books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: EntityRestOptions.DEFAULT_SUPPORTED_VERBS, Path: configuredRestPath),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
                Mappings: null,
                Relationships: null);

            Dictionary<string, Entity> entities = new()
            {
                { entityName, entity }
            };

            RuntimeEntities runtimeEntities = new(entities);
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // For a given table backed entity, there will be two paths:
            // 1. GetById path: "/customEntityPath/id/{id}"
            // 2. GetAll path:  "/customEntityPath"
            foreach (string actualPath in openApiDocument.Paths.Keys)
            {
                Assert.AreEqual(expected: true, actual: actualPath.StartsWith(expectedOpenApiPath), message: PATH_GENERATION_ERROR + actualPath);
            }
        }

        /// <summary>
        /// Validates that a read-only entity (Methods: ["Get"]) only exposes GET operations in the OpenAPI document.
        /// </summary>
        [TestMethod]
        public async Task ValidateMethodFiltering_ReadOnlyEntity_OnlyGetOperations()
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
                { "ReadOnlyEntity", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert: Validate operations in OpenAPI document
            string collectionPath = "/ReadOnlyEntity";
            string byIdPath = null;

            // Find by-ID path
            foreach (string path in openApiDocument.Paths.Keys)
            {
                if (path.StartsWith(collectionPath + "/"))
                {
                    byIdPath = path;
                    break;
                }
            }

            // Collection path should have GET but not POST
            if (openApiDocument.Paths.ContainsKey(collectionPath))
            {
                OpenApiPathItem collectionPathItem = openApiDocument.Paths[collectionPath];
                Assert.IsTrue(collectionPathItem.Operations.ContainsKey(OperationType.Get), "Expected GET operation on collection path");
                Assert.IsFalse(collectionPathItem.Operations.ContainsKey(OperationType.Post), "Expected no POST operation on collection path");
            }

            // By-ID path should have GET but not PUT, PATCH, DELETE
            if (byIdPath != null && openApiDocument.Paths.ContainsKey(byIdPath))
            {
                OpenApiPathItem byIdPathItem = openApiDocument.Paths[byIdPath];
                Assert.IsTrue(byIdPathItem.Operations.ContainsKey(OperationType.Get), "Expected GET operation on by-ID path");
                Assert.IsFalse(byIdPathItem.Operations.ContainsKey(OperationType.Put), "Expected no PUT operation on by-ID path");
                Assert.IsFalse(byIdPathItem.Operations.ContainsKey(OperationType.Patch), "Expected no PATCH operation on by-ID path");
                Assert.IsFalse(byIdPathItem.Operations.ContainsKey(OperationType.Delete), "Expected no DELETE operation on by-ID path");
            }
        }

        /// <summary>
        /// Validates that a write-only entity (Methods: ["Post"]) only exposes POST operation in the OpenAPI document.
        /// </summary>
        [TestMethod]
        public async Task ValidateMethodFiltering_WriteOnlyEntity_OnlyPostOperation()
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
                { "WriteOnlyEntity", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert: Validate operations in OpenAPI document
            string collectionPath = "/WriteOnlyEntity";

            // Collection path should have POST but not GET
            if (openApiDocument.Paths.ContainsKey(collectionPath))
            {
                OpenApiPathItem collectionPathItem = openApiDocument.Paths[collectionPath];
                Assert.IsTrue(collectionPathItem.Operations.ContainsKey(OperationType.Post), "Expected POST operation on collection path");
                Assert.IsFalse(collectionPathItem.Operations.ContainsKey(OperationType.Get), "Expected no GET operation on collection path");
            }

            // By-ID path should not exist (no GET, PUT, PATCH, DELETE operations)
            foreach (string path in openApiDocument.Paths.Keys)
            {
                Assert.IsFalse(path.StartsWith(collectionPath + "/"), "Expected no by-ID path for write-only entity");
            }
        }

        /// <summary>
        /// Validates that an entity with null Methods defaults to all operations in the OpenAPI document.
        /// </summary>
        [TestMethod]
        public async Task ValidateMethodFiltering_NullMethods_AllOperationsPresent()
        {
            // Arrange: Entity with null Methods (defaults to all verbs)
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
                { "DefaultMethodsEntity", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert: Validate all operations present
            string collectionPath = "/DefaultMethodsEntity";
            string byIdPath = null;

            // Find by-ID path
            foreach (string path in openApiDocument.Paths.Keys)
            {
                if (path.StartsWith(collectionPath + "/"))
                {
                    byIdPath = path;
                    break;
                }
            }

            // Collection path should have GET and POST
            Assert.IsTrue(openApiDocument.Paths.ContainsKey(collectionPath), "Expected collection path to exist");
            OpenApiPathItem collectionPathItem = openApiDocument.Paths[collectionPath];
            Assert.IsTrue(collectionPathItem.Operations.ContainsKey(OperationType.Get), "Expected GET operation on collection path");
            Assert.IsTrue(collectionPathItem.Operations.ContainsKey(OperationType.Post), "Expected POST operation on collection path");

            // By-ID path should have GET, PUT, PATCH, DELETE
            Assert.IsNotNull(byIdPath, "Expected by-ID path to exist");
            OpenApiPathItem byIdPathItem = openApiDocument.Paths[byIdPath];
            Assert.IsTrue(byIdPathItem.Operations.ContainsKey(OperationType.Get), "Expected GET operation on by-ID path");
            Assert.IsTrue(byIdPathItem.Operations.ContainsKey(OperationType.Put), "Expected PUT operation on by-ID path");
            Assert.IsTrue(byIdPathItem.Operations.ContainsKey(OperationType.Patch), "Expected PATCH operation on by-ID path");
            Assert.IsTrue(byIdPathItem.Operations.ContainsKey(OperationType.Delete), "Expected DELETE operation on by-ID path");
        }

        /// <summary>
        /// Validates that an entity with empty Methods array defaults to all operations in the OpenAPI document.
        /// </summary>
        [TestMethod]
        public async Task ValidateMethodFiltering_EmptyMethodsArray_AllOperationsPresent()
        {
            // Arrange: Entity with empty Methods array (defaults to all verbs)
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

            // Assert: Validate all operations present
            string collectionPath = "/EmptyMethodsEntity";

            Assert.IsTrue(openApiDocument.Paths.ContainsKey(collectionPath), "Expected collection path to exist");
            OpenApiPathItem collectionPathItem = openApiDocument.Paths[collectionPath];
            Assert.IsTrue(collectionPathItem.Operations.ContainsKey(OperationType.Get), "Expected GET operation on collection path");
            Assert.IsTrue(collectionPathItem.Operations.ContainsKey(OperationType.Post), "Expected POST operation on collection path");
        }
    }
}
