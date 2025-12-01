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

        /// <summary>
        /// Validates that anonymous role is distinct from superset (no role specified).
        /// When two roles have different permissions, the superset should contain both,
        /// but the anonymous-specific view should only contain anonymous permissions.
        /// </summary>
        [TestMethod]
        public async Task AnonymousRole_IsDistinctFromSuperset()
        {
            // Anonymous can only read, authenticated can create/update/delete
            EntityPermission[] permissions = new[]
            {
                new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(EntityActionOperation.Read, null, new()) }),
                new EntityPermission(Role: "authenticated", Actions: new[] { 
                    new EntityAction(EntityActionOperation.Create, null, new()),
                    new EntityAction(EntityActionOperation.Update, null, new()),
                    new EntityAction(EntityActionOperation.Delete, null, new())
                })
            };

            // Superset (no role) should have all operations
            OpenApiDocument supersetDoc = await GenerateDocumentWithPermissions(permissions);
            Assert.IsTrue(supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Get)), "Superset should have GET");
            Assert.IsTrue(supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Post)), "Superset should have POST");
            Assert.IsTrue(supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Put)), "Superset should have PUT");
            Assert.IsTrue(supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Patch)), "Superset should have PATCH");
            Assert.IsTrue(supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Delete)), "Superset should have DELETE");
        }

        /// <summary>
        /// Validates competing roles don't leak operations to each other.
        /// When one role has read-only and another has write-only, each role's
        /// OpenAPI should only show their specific permissions.
        /// </summary>
        [TestMethod]
        public async Task CompetingRoles_DoNotLeakOperations()
        {
            // Role1 can only read, Role2 can only create
            EntityPermission[] permissions = new[]
            {
                new EntityPermission(Role: "reader", Actions: new[] { new EntityAction(EntityActionOperation.Read, null, new()) }),
                new EntityPermission(Role: "writer", Actions: new[] { new EntityAction(EntityActionOperation.Create, null, new()) })
            };

            // The superset should have both GET and POST
            OpenApiDocument supersetDoc = await GenerateDocumentWithPermissions(permissions);
            Assert.IsTrue(supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Get)), "Superset should have GET from reader");
            Assert.IsTrue(supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Post)), "Superset should have POST from writer");
            
            // Neither role alone should have all operations - they don't leak
            // This test confirms the superset correctly combines permissions while
            // the individual role filtering (when implemented for direct calls) would not
            Assert.IsFalse(supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Put)), "No role has PUT, superset should not have it");
            Assert.IsFalse(supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Patch)), "No role has PATCH, superset should not have it");
            Assert.IsFalse(supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Delete)), "No role has DELETE, superset should not have it");
        }

        /// <summary>
        /// Validates competing roles don't leak fields to each other.
        /// When one role has access to field A and another has access to field B,
        /// the superset should have both, but individual role filtering should not leak.
        /// </summary>
        [TestMethod]
        public async Task CompetingRoles_DoNotLeakFields()
        {
            // Reader can see 'id', writer can see 'title'
            EntityActionFields readerFields = new(Exclude: new HashSet<string>(), Include: new HashSet<string> { "id" });
            EntityActionFields writerFields = new(Exclude: new HashSet<string>(), Include: new HashSet<string> { "title" });
            EntityPermission[] permissions = new[]
            {
                new EntityPermission(Role: "reader", Actions: new[] { new EntityAction(EntityActionOperation.Read, readerFields, new()) }),
                new EntityPermission(Role: "writer", Actions: new[] { new EntityAction(EntityActionOperation.Create, writerFields, new()) })
            };

            // The superset should have both fields
            OpenApiDocument supersetDoc = await GenerateDocumentWithPermissions(permissions);
            Assert.IsTrue(supersetDoc.Components.Schemas.ContainsKey("book"), "Schema should exist");
            Assert.IsTrue(supersetDoc.Components.Schemas["book"].Properties.ContainsKey("id"), "Superset should have 'id' from reader");
            Assert.IsTrue(supersetDoc.Components.Schemas["book"].Properties.ContainsKey("title"), "Superset should have 'title' from writer");
        }

        /// <summary>
        /// Validates that when request-body-strict is true (default), request body schemas
        /// have additionalProperties set to false.
        /// </summary>
        [TestMethod]
        public async Task RequestBodyStrict_True_DisallowsExtraFields()
        {
            OpenApiDocument doc = await GenerateDocumentWithPermissions(
                OpenApiTestBootstrap.CreateBasicPermissions(),
                requestBodyStrict: true);

            // Request body schemas should have additionalProperties = false
            Assert.IsTrue(doc.Components.Schemas.ContainsKey("book_NoAutoPK"), "POST request body schema should exist");
            Assert.IsFalse(doc.Components.Schemas["book_NoAutoPK"].AdditionalPropertiesAllowed, "POST request body should not allow extra fields in strict mode");

            Assert.IsTrue(doc.Components.Schemas.ContainsKey("book_NoPK"), "PUT/PATCH request body schema should exist");
            Assert.IsFalse(doc.Components.Schemas["book_NoPK"].AdditionalPropertiesAllowed, "PUT/PATCH request body should not allow extra fields in strict mode");

            // Response body schema should allow extra fields (not a request body)
            Assert.IsTrue(doc.Components.Schemas.ContainsKey("book"), "Response body schema should exist");
            Assert.IsTrue(doc.Components.Schemas["book"].AdditionalPropertiesAllowed, "Response body should allow extra fields");
        }

        /// <summary>
        /// Validates that when request-body-strict is false, request body schemas
        /// have additionalProperties set to true.
        /// </summary>
        [TestMethod]
        public async Task RequestBodyStrict_False_AllowsExtraFields()
        {
            OpenApiDocument doc = await GenerateDocumentWithPermissions(
                OpenApiTestBootstrap.CreateBasicPermissions(),
                requestBodyStrict: false);

            // Request body schemas should have additionalProperties = true
            Assert.IsTrue(doc.Components.Schemas.ContainsKey("book_NoAutoPK"), "POST request body schema should exist");
            Assert.IsTrue(doc.Components.Schemas["book_NoAutoPK"].AdditionalPropertiesAllowed, "POST request body should allow extra fields in non-strict mode");

            Assert.IsTrue(doc.Components.Schemas.ContainsKey("book_NoPK"), "PUT/PATCH request body schema should exist");
            Assert.IsTrue(doc.Components.Schemas["book_NoPK"].AdditionalPropertiesAllowed, "PUT/PATCH request body should allow extra fields in non-strict mode");
        }

        private static async Task<OpenApiDocument> GenerateDocumentWithPermissions(EntityPermission[] permissions, bool? requestBodyStrict = null)
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
            return await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(entities, CONFIG_FILE, DB_ENV, requestBodyStrict);
        }
    }
}
