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
    /// Tests validating OpenAPI document filters REST methods based on entity permissions.
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class PermissionBasedOperationFilteringTests
    {
        private const string CONFIG_FILE = "permission-filter-config.MsSql.json";
        private const string DB_ENV = TestCategory.MSSQL;

        /// <summary>
        /// Validates read-only entity shows only GET operations.
        /// </summary>
        [TestMethod]
        public async Task ReadOnlyEntity_ShowsOnlyGetOperations()
        {
            EntityPermission[] permissions = new[]
            {
                new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(EntityActionOperation.Read, null, new()) })
            };

            OpenApiDocument doc = await GenerateDocumentWithPermissions(permissions);

            foreach (var path in doc.Paths)
            {
                Assert.IsTrue(path.Value.Operations.ContainsKey(OperationType.Get), $"GET missing at {path.Key}");
                Assert.IsFalse(path.Value.Operations.ContainsKey(OperationType.Post), $"POST should not exist at {path.Key}");
                Assert.IsFalse(path.Value.Operations.ContainsKey(OperationType.Put), $"PUT should not exist at {path.Key}");
                Assert.IsFalse(path.Value.Operations.ContainsKey(OperationType.Patch), $"PATCH should not exist at {path.Key}");
                Assert.IsFalse(path.Value.Operations.ContainsKey(OperationType.Delete), $"DELETE should not exist at {path.Key}");
            }
        }

        /// <summary>
        /// Validates wildcard (*) permission shows all CRUD operations.
        /// </summary>
        [TestMethod]
        public async Task WildcardPermission_ShowsAllOperations()
        {
            OpenApiDocument doc = await GenerateDocumentWithPermissions(OpenApiTestBootstrap.CreateBasicPermissions());

            Assert.IsTrue(doc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Get)));
            Assert.IsTrue(doc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Post)));
            Assert.IsTrue(doc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Put)));
            Assert.IsTrue(doc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Patch)));
            Assert.IsTrue(doc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Delete)));
        }

        /// <summary>
        /// Validates entity with no permissions is omitted from OpenAPI document.
        /// </summary>
        [TestMethod]
        public async Task EntityWithNoPermissions_IsOmittedFromDocument()
        {
            // Entity with no permissions
            Entity entityNoPerms = new(
                Source: new("books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(null, null, false),
                Rest: new(EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                Permissions: [],
                Mappings: null,
                Relationships: null);

            // Entity with permissions for reference
            Entity entityWithPerms = new(
                Source: new("publishers", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(null, null, false),
                Rest: new(EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
                Mappings: null,
                Relationships: null);

            RuntimeEntities entities = new(new Dictionary<string, Entity>
            {
                { "book", entityNoPerms },
                { "publisher", entityWithPerms }
            });

            OpenApiDocument doc = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(entities, CONFIG_FILE, DB_ENV);

            Assert.IsFalse(doc.Paths.Keys.Any(k => k.Contains("book")), "Entity with no permissions should not have paths");
            Assert.IsFalse(doc.Tags.Any(t => t.Name == "book"), "Entity with no permissions should not have tag");
            Assert.IsTrue(doc.Paths.Keys.Any(k => k.Contains("publisher")), "Entity with permissions should have paths");
        }

        /// <summary>
        /// Validates superset of permissions across roles is shown.
        /// </summary>
        [TestMethod]
        public async Task MixedRolePermissions_ShowsSupersetOfOperations()
        {
            EntityPermission[] permissions = new[]
            {
                new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(EntityActionOperation.Read, null, new()) }),
                new EntityPermission(Role: "authenticated", Actions: new[] { new EntityAction(EntityActionOperation.Create, null, new()) })
            };

            OpenApiDocument doc = await GenerateDocumentWithPermissions(permissions);

            // Should have both GET (from anonymous read) and POST (from authenticated create)
            Assert.IsTrue(doc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Get)), "GET should exist from anonymous read");
            Assert.IsTrue(doc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Post)), "POST should exist from authenticated create");
        }

        /// <summary>
        /// Validates that excluded fields are not shown in OpenAPI schema.
        /// </summary>
        [TestMethod]
        public async Task ExcludedFields_NotShownInSchema()
        {
            // Create permission with excluded field
            EntityActionFields fields = new(Exclude: new HashSet<string> { "publisher_id" }, Include: null);
            EntityPermission[] permissions = new[]
            {
                new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(EntityActionOperation.All, fields, new()) })
            };

            OpenApiDocument doc = await GenerateDocumentWithPermissions(permissions);

            // Check that the excluded field is not in the schema
            Assert.IsTrue(doc.Components.Schemas.ContainsKey("book"), "Schema should exist for book entity");
            Assert.IsFalse(doc.Components.Schemas["book"].Properties.ContainsKey("publisher_id"), "Excluded field should not be in schema");
        }

        /// <summary>
        /// Validates superset of fields across different role permissions is shown.
        /// </summary>
        [TestMethod]
        public async Task MixedRoleFieldPermissions_ShowsSupersetOfFields()
        {
            // Anonymous can see id only, authenticated can see title only
            EntityActionFields anonymousFields = new(Exclude: new HashSet<string>(), Include: new HashSet<string> { "id" });
            EntityActionFields authenticatedFields = new(Exclude: new HashSet<string>(), Include: new HashSet<string> { "title" });
            EntityPermission[] permissions = new[]
            {
                new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(EntityActionOperation.Read, anonymousFields, new()) }),
                new EntityPermission(Role: "authenticated", Actions: new[] { new EntityAction(EntityActionOperation.Read, authenticatedFields, new()) })
            };

            OpenApiDocument doc = await GenerateDocumentWithPermissions(permissions);

            // Should have both id (from anonymous) and title (from authenticated) - superset of fields
            Assert.IsTrue(doc.Components.Schemas.ContainsKey("book"), "Schema should exist for book entity");
            Assert.IsTrue(doc.Components.Schemas["book"].Properties.ContainsKey("id"), "Field 'id' should be in schema from anonymous role");
            Assert.IsTrue(doc.Components.Schemas["book"].Properties.ContainsKey("title"), "Field 'title' should be in schema from authenticated role");
        }

        private static async Task<OpenApiDocument> GenerateDocumentWithPermissions(EntityPermission[] permissions)
        {
            Entity entity = new(
                Source: new("books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(null, null, false),
                Rest: new(EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                Permissions: permissions,
                Mappings: null,
                Relationships: null);

            RuntimeEntities entities = new(new Dictionary<string, Entity> { { "book", entity } });
            return await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(entities, CONFIG_FILE, DB_ENV);
        }
    }
}
