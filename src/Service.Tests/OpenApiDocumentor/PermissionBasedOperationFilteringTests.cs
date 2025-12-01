// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.OpenApi.Models;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.OpenApiIntegration
{
    /// <summary>
    /// Integration tests validating that OpenAPI document REST methods are filtered
    /// based on entity permissions across all roles.
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class PermissionBasedOperationFilteringTests
    {
        private const string CUSTOM_CONFIG = "permission-filter-config.MsSql.json";
        private const string MSSQL_ENVIRONMENT = TestCategory.MSSQL;

        /// <summary>
        /// Validates that when an entity has only read permission for all roles,
        /// the OpenAPI document only shows GET operations and omits POST, PUT, PATCH, DELETE.
        /// </summary>
        [TestMethod]
        public async Task ReadOnlyEntity_ShowsOnlyGetOperations()
        {
            // Arrange: Create entity with read-only permissions
            EntityPermission[] readOnlyPermissions = new[]
            {
                new EntityPermission(Role: "anonymous", Actions: new EntityAction[]
                {
                    new(Action: EntityActionOperation.Read, Fields: null, Policy: new())
                }),
                new EntityPermission(Role: "authenticated", Actions: new EntityAction[]
                {
                    new(Action: EntityActionOperation.Read, Fields: null, Policy: new())
                })
            };

            Entity entity = new(
                Source: new(Object: "books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                Permissions: readOnlyPermissions,
                Mappings: null,
                Relationships: null);

            Dictionary<string, Entity> entities = new()
            {
                { "book", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert: Only GET operations should be present
            foreach (KeyValuePair<string, OpenApiPathItem> path in openApiDocument.Paths)
            {
                Assert.IsTrue(
                    path.Value.Operations.ContainsKey(OperationType.Get),
                    $"GET operation should be present for path {path.Key}");

                Assert.IsFalse(
                    path.Value.Operations.ContainsKey(OperationType.Post),
                    $"POST operation should not be present for read-only entity at path {path.Key}");

                Assert.IsFalse(
                    path.Value.Operations.ContainsKey(OperationType.Put),
                    $"PUT operation should not be present for read-only entity at path {path.Key}");

                Assert.IsFalse(
                    path.Value.Operations.ContainsKey(OperationType.Patch),
                    $"PATCH operation should not be present for read-only entity at path {path.Key}");

                Assert.IsFalse(
                    path.Value.Operations.ContainsKey(OperationType.Delete),
                    $"DELETE operation should not be present for read-only entity at path {path.Key}");
            }
        }

        /// <summary>
        /// Validates that when an entity has create and read permissions (but no update or delete),
        /// the OpenAPI document shows GET and POST but omits PUT, PATCH, DELETE.
        /// </summary>
        [TestMethod]
        public async Task CreateAndReadEntity_ShowsGetAndPostOnly()
        {
            // Arrange: Create entity with create and read permissions
            EntityPermission[] createReadPermissions = new[]
            {
                new EntityPermission(Role: "anonymous", Actions: new EntityAction[]
                {
                    new(Action: EntityActionOperation.Read, Fields: null, Policy: new()),
                    new(Action: EntityActionOperation.Create, Fields: null, Policy: new())
                })
            };

            Entity entity = new(
                Source: new(Object: "books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                Permissions: createReadPermissions,
                Mappings: null,
                Relationships: null);

            Dictionary<string, Entity> entities = new()
            {
                { "book", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert: GET should be present on all paths, POST only on base path (without PK)
            string basePath = "/book";
            string pkPath = openApiDocument.Paths.Keys.FirstOrDefault(k => k.StartsWith(basePath) && k != basePath);

            // Base path should have GET and POST
            Assert.IsTrue(openApiDocument.Paths[basePath].Operations.ContainsKey(OperationType.Get), "GET should be present on base path");
            Assert.IsTrue(openApiDocument.Paths[basePath].Operations.ContainsKey(OperationType.Post), "POST should be present on base path");

            // PK path should have GET but not PUT, PATCH, DELETE
            if (pkPath != null)
            {
                Assert.IsTrue(openApiDocument.Paths[pkPath].Operations.ContainsKey(OperationType.Get), "GET should be present on PK path");
                Assert.IsFalse(openApiDocument.Paths[pkPath].Operations.ContainsKey(OperationType.Put), "PUT should not be present");
                Assert.IsFalse(openApiDocument.Paths[pkPath].Operations.ContainsKey(OperationType.Patch), "PATCH should not be present");
                Assert.IsFalse(openApiDocument.Paths[pkPath].Operations.ContainsKey(OperationType.Delete), "DELETE should not be present");
            }
        }

        /// <summary>
        /// Validates that when different roles have different permissions, the OpenAPI document
        /// shows the superset of operations available across all roles.
        /// </summary>
        [TestMethod]
        public async Task MultipleRolesWithDifferentPermissions_ShowsSupersetOfOperations()
        {
            // Arrange: Create entity where anonymous has read-only, but authenticated has all
            EntityPermission[] mixedPermissions = new[]
            {
                new EntityPermission(Role: "anonymous", Actions: new EntityAction[]
                {
                    new(Action: EntityActionOperation.Read, Fields: null, Policy: new())
                }),
                new EntityPermission(Role: "authenticated", Actions: new EntityAction[]
                {
                    new(Action: EntityActionOperation.All, Fields: null, Policy: new())
                })
            };

            Entity entity = new(
                Source: new(Object: "books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                Permissions: mixedPermissions,
                Mappings: null,
                Relationships: null);

            Dictionary<string, Entity> entities = new()
            {
                { "book", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert: All operations should be present since authenticated role has all permissions
            string basePath = "/book";
            string pkPath = openApiDocument.Paths.Keys.FirstOrDefault(k => k.StartsWith(basePath) && k != basePath);

            // Base path should have GET and POST
            Assert.IsTrue(openApiDocument.Paths[basePath].Operations.ContainsKey(OperationType.Get), "GET should be present on base path");
            Assert.IsTrue(openApiDocument.Paths[basePath].Operations.ContainsKey(OperationType.Post), "POST should be present on base path");

            // PK path should have GET, PUT, PATCH, DELETE
            if (pkPath != null)
            {
                Assert.IsTrue(openApiDocument.Paths[pkPath].Operations.ContainsKey(OperationType.Get), "GET should be present on PK path");
                Assert.IsTrue(openApiDocument.Paths[pkPath].Operations.ContainsKey(OperationType.Put), "PUT should be present on PK path");
                Assert.IsTrue(openApiDocument.Paths[pkPath].Operations.ContainsKey(OperationType.Patch), "PATCH should be present on PK path");
                Assert.IsTrue(openApiDocument.Paths[pkPath].Operations.ContainsKey(OperationType.Delete), "DELETE should be present on PK path");
            }
        }

        /// <summary>
        /// Validates that wildcard (*) permission correctly expands to all CRUD operations.
        /// </summary>
        [TestMethod]
        public async Task WildcardPermission_ShowsAllOperations()
        {
            // Arrange: Create entity with wildcard permissions
            Entity entity = new(
                Source: new(Object: "books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(), // Uses wildcard
                Mappings: null,
                Relationships: null);

            Dictionary<string, Entity> entities = new()
            {
                { "book", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert: All operations should be present
            string basePath = "/book";
            string pkPath = openApiDocument.Paths.Keys.FirstOrDefault(k => k.StartsWith(basePath) && k != basePath);

            // Base path should have GET and POST
            Assert.IsTrue(openApiDocument.Paths[basePath].Operations.ContainsKey(OperationType.Get), "GET should be present on base path");
            Assert.IsTrue(openApiDocument.Paths[basePath].Operations.ContainsKey(OperationType.Post), "POST should be present on base path");

            // PK path should have GET, PUT, PATCH, DELETE
            if (pkPath != null)
            {
                Assert.IsTrue(openApiDocument.Paths[pkPath].Operations.ContainsKey(OperationType.Get), "GET should be present on PK path");
                Assert.IsTrue(openApiDocument.Paths[pkPath].Operations.ContainsKey(OperationType.Put), "PUT should be present on PK path");
                Assert.IsTrue(openApiDocument.Paths[pkPath].Operations.ContainsKey(OperationType.Patch), "PATCH should be present on PK path");
                Assert.IsTrue(openApiDocument.Paths[pkPath].Operations.ContainsKey(OperationType.Delete), "DELETE should be present on PK path");
            }
        }

        /// <summary>
        /// Validates that an entity with only delete permission shows only DELETE operation on PK path.
        /// </summary>
        [TestMethod]
        public async Task DeleteOnlyEntity_ShowsOnlyDeleteOnPkPath()
        {
            // Arrange: Create entity with delete-only permissions
            EntityPermission[] deleteOnlyPermissions = new[]
            {
                new EntityPermission(Role: "anonymous", Actions: new EntityAction[]
                {
                    new(Action: EntityActionOperation.Delete, Fields: null, Policy: new())
                })
            };

            Entity entity = new(
                Source: new(Object: "books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                Permissions: deleteOnlyPermissions,
                Mappings: null,
                Relationships: null);

            Dictionary<string, Entity> entities = new()
            {
                { "book", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Act
            OpenApiDocument openApiDocument = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CUSTOM_CONFIG,
                databaseEnvironment: MSSQL_ENVIRONMENT);

            // Assert: Only DELETE should be present on PK path, base path should not exist
            string basePath = "/book";
            string pkPath = openApiDocument.Paths.Keys.FirstOrDefault(k => k.StartsWith(basePath) && k != basePath);

            // Base path should not exist (no GET or POST)
            Assert.IsFalse(openApiDocument.Paths.ContainsKey(basePath), "Base path should not exist for delete-only entity");

            // PK path should have only DELETE
            if (pkPath != null)
            {
                Assert.IsFalse(openApiDocument.Paths[pkPath].Operations.ContainsKey(OperationType.Get), "GET should not be present");
                Assert.IsFalse(openApiDocument.Paths[pkPath].Operations.ContainsKey(OperationType.Put), "PUT should not be present");
                Assert.IsFalse(openApiDocument.Paths[pkPath].Operations.ContainsKey(OperationType.Patch), "PATCH should not be present");
                Assert.IsTrue(openApiDocument.Paths[pkPath].Operations.ContainsKey(OperationType.Delete), "DELETE should be present on PK path");
            }
        }
    }
}
