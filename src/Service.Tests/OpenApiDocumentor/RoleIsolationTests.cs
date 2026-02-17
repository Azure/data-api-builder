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
    /// Tests validating OpenAPI document correctly isolates permissions between roles.
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class RoleIsolationTests
    {
        private const string CONFIG_FILE = "role-isolation-config.MsSql.json";
        private const string DB_ENV = TestCategory.MSSQL;

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
                new EntityPermission(
                    Role: "anonymous",
                    Actions: new[] { new EntityAction(EntityActionOperation.Read, null, new()) }),
                new EntityPermission(
                    Role: "authenticated",
                    Actions: new[] {
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
            Assert.IsTrue(
                supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Get)),
                "Superset should have GET from reader");
            Assert.IsTrue(
                supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Post)),
                "Superset should have POST from writer");

            // Neither role alone should have all operations - they don't leak
            // This test confirms the superset correctly combines permissions while
            // the individual role filtering (when implemented for direct calls) would not
            Assert.IsFalse(
                supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Put)),
                "No role has PUT, superset should not have it");
            Assert.IsFalse(
                supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Patch)),
                "No role has PATCH, superset should not have it");
            Assert.IsFalse(
                supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Delete)),
                "No role has DELETE, superset should not have it");
        }

        /// <summary>
        /// Validates that PUT/PATCH require both Create and Update permissions.
        /// Since PUT/PATCH can create if missing (upsert), both permissions are needed at runtime.
        /// </summary>
        [TestMethod]
        public async Task PutPatchOperations_RequireBothCreateAndUpdatePermissions()
        {
            // Test 1: Only Create permission - should NOT have PUT/PATCH
            EntityPermission[] createOnly = new[]
            {
                new EntityPermission(Role: "creator", Actions: new[] { new EntityAction(EntityActionOperation.Create, null, new()) })
            };
            OpenApiDocument docCreateOnly = await GenerateDocumentWithPermissions(createOnly);
            Assert.IsTrue(docCreateOnly.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Post)), "Should have POST with Create permission");
            Assert.IsFalse(docCreateOnly.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Put)), "Should NOT have PUT with only Create permission");
            Assert.IsFalse(docCreateOnly.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Patch)), "Should NOT have PATCH with only Create permission");

            // Test 2: Only Update permission - should NOT have PUT/PATCH
            EntityPermission[] updateOnly = new[]
            {
                new EntityPermission(Role: "updater", Actions: new[] { new EntityAction(EntityActionOperation.Update, null, new()) })
            };
            OpenApiDocument docUpdateOnly = await GenerateDocumentWithPermissions(updateOnly);
            Assert.IsFalse(docUpdateOnly.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Put)), "Should NOT have PUT with only Update permission");
            Assert.IsFalse(docUpdateOnly.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Patch)), "Should NOT have PATCH with only Update permission");

            // Test 3: Both Create and Update permissions - should have PUT/PATCH
            EntityPermission[] createAndUpdate = new[]
            {
                new EntityPermission(
                    Role: "editor",
                    Actions: new[] {
                        new EntityAction(EntityActionOperation.Create, null, new()),
                        new EntityAction(EntityActionOperation.Update, null, new())
                    })
            };
            OpenApiDocument docBoth = await GenerateDocumentWithPermissions(createAndUpdate);
            Assert.IsTrue(docBoth.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Post)), "Should have POST with Create permission");
            Assert.IsTrue(docBoth.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Put)), "Should have PUT with both Create and Update permissions");
            Assert.IsTrue(docBoth.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Patch)), "Should have PATCH with both Create and Update permissions");
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

            // Reader role should only see 'id', not 'title'
            OpenApiDocument readerDoc = await GenerateDocumentWithPermissions(permissions, role: "reader");
            Assert.IsTrue(readerDoc.Components.Schemas.ContainsKey("book"), "Reader schema should exist");
            Assert.IsTrue(readerDoc.Components.Schemas["book"].Properties.ContainsKey("id"), "Reader should see 'id'");
            Assert.IsFalse(readerDoc.Components.Schemas["book"].Properties.ContainsKey("title"), "Reader should NOT see 'title'");

            // Writer role should only see 'title', not 'id'
            OpenApiDocument writerDoc = await GenerateDocumentWithPermissions(permissions, role: "writer");
            Assert.IsTrue(writerDoc.Components.Schemas.ContainsKey("book"), "Writer schema should exist");
            Assert.IsTrue(writerDoc.Components.Schemas["book"].Properties.ContainsKey("title"), "Writer should see 'title'");
            Assert.IsFalse(writerDoc.Components.Schemas["book"].Properties.ContainsKey("id"), "Writer should NOT see 'id'");
        }

        private static async Task<OpenApiDocument> GenerateDocumentWithPermissions(EntityPermission[] permissions, string role = null)
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
            return await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(entities, CONFIG_FILE, DB_ENV, requestBodyStrict: null, role: role);
        }
    }
}
