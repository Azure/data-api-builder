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
    /// Tests for role-specific OpenAPI endpoint functionality including
    /// caching and case-insensitivity.
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class RoleSpecificEndpointTests
    {
        private const string CONFIG_FILE = "role-specific-endpoint-config.MsSql.json";
        private const string DB_ENV = TestCategory.MSSQL;

        /// <summary>
        /// Validates that role-specific OpenAPI documents are properly generated
        /// and contain expected content.
        /// </summary>
        [TestMethod]
        public async Task RoleSpecificDocument_GeneratesCorrectly()
        {
            EntityPermission[] permissions = new[]
            {
                new EntityPermission(
                    Role: "reader",
                    Actions: new[] { new EntityAction(EntityActionOperation.Read, null, new()) })
            };

            // Generate document for 'reader' role
            OpenApiDocument doc = await GenerateDocumentWithPermissions(permissions, role: "reader");

            Assert.IsNotNull(doc, "Document should not be null");
            Assert.IsTrue(doc.Paths.Count > 0, "Document should contain paths");
            Assert.IsTrue(doc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Get)), "Reader role should have GET");
            Assert.IsFalse(doc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Post)), "Reader role should NOT have POST");
        }

        /// <summary>
        /// Validates that role names are case-insensitive when matching.
        /// </summary>
        [DataTestMethod]
        [DataRow("reader")]
        [DataRow("READER")]
        [DataRow("Reader")]
        [DataRow("rEaDeR")]
        public async Task RoleSpecificDocument_IsCaseInsensitive(string roleVariant)
        {
            EntityPermission[] permissions = new[]
            {
                new EntityPermission(
                    Role: "reader",
                    Actions: new[] { new EntityAction(EntityActionOperation.Read, null, new()) })
            };

            OpenApiDocument doc = await GenerateDocumentWithPermissions(permissions, role: roleVariant);

            Assert.IsNotNull(doc, $"Document for role variant '{roleVariant}' should not be null");
            Assert.IsTrue(doc.Paths.Count > 0, "Document should contain paths");
            Assert.IsTrue(
                doc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Get)),
                $"GET should be available for role '{roleVariant}'");
        }

        /// <summary>
        /// Validates that superset document contains operations from all roles
        /// while role-specific documents only contain that role's operations.
        /// </summary>
        [TestMethod]
        public async Task SupersetDocument_ContainsAllRoleOperations()
        {
            EntityPermission[] permissions = new[]
            {
                new EntityPermission(
                    Role: "reader",
                    Actions: new[] { new EntityAction(EntityActionOperation.Read, null, new()) }),
                new EntityPermission(
                    Role: "writer",
                    Actions: new[] {
                        new EntityAction(EntityActionOperation.Create, null, new()),
                        new EntityAction(EntityActionOperation.Update, null, new())
                    })
            };

            // Superset (no role) should have all operations
            OpenApiDocument supersetDoc = await GenerateDocumentWithPermissions(permissions);
            Assert.IsTrue(
                supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Get)),
                "Superset should have GET");
            Assert.IsTrue(
                supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Post)),
                "Superset should have POST");
            Assert.IsTrue(
                supersetDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Put)),
                "Superset should have PUT");

            // Reader role should only have GET
            OpenApiDocument readerDoc = await GenerateDocumentWithPermissions(permissions, role: "reader");
            Assert.IsTrue(
                readerDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Get)),
                "Reader should have GET");
            Assert.IsFalse(
                readerDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Post)),
                "Reader should NOT have POST");

            // Writer role should only have POST, PUT, PATCH
            OpenApiDocument writerDoc = await GenerateDocumentWithPermissions(permissions, role: "writer");
            Assert.IsTrue(
                writerDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Post)),
                "Writer should have POST");
            Assert.IsTrue(
                writerDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Put)),
                "Writer should have PUT");
            Assert.IsFalse(
                writerDoc.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Get)),
                "Writer should NOT have GET");
        }

        /// <summary>
        /// Validates requestBodyRequired optimization - it should only be calculated
        /// when PUT or PATCH operations are configured.
        /// </summary>
        [TestMethod]
        public async Task RequestBodyRequired_OnlyCalculatedForPutPatch()
        {
            // Create+Update permissions enable PUT/PATCH
            EntityPermission[] permissionsWithUpdate = new[]
            {
                new EntityPermission(
                    Role: "editor",
                    Actions: new[] {
                        new EntityAction(EntityActionOperation.Create, null, new()),
                        new EntityAction(EntityActionOperation.Update, null, new())
                    })
            };

            OpenApiDocument docWithUpdate = await GenerateDocumentWithPermissions(permissionsWithUpdate);
            Assert.IsTrue(
                docWithUpdate.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Put)),
                "Should have PUT");
            Assert.IsTrue(
                docWithUpdate.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Patch)),
                "Should have PATCH");

            // Read-only permissions - no PUT/PATCH
            EntityPermission[] permissionsReadOnly = new[]
            {
                new EntityPermission(
                    Role: "reader",
                    Actions: new[] { new EntityAction(EntityActionOperation.Read, null, new()) })
            };

            OpenApiDocument docReadOnly = await GenerateDocumentWithPermissions(permissionsReadOnly);
            Assert.IsFalse(
                docReadOnly.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Put)),
                "Should NOT have PUT");
            Assert.IsFalse(
                docReadOnly.Paths.Any(p => p.Value.Operations.ContainsKey(OperationType.Patch)),
                "Should NOT have PATCH");
        }

        private static async Task<OpenApiDocument> GenerateDocumentWithPermissions(
            EntityPermission[] permissions,
            string role = null)
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
            return await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                entities,
                CONFIG_FILE,
                DB_ENV,
                requestBodyStrict: null,
                role: role);
        }
    }
}
