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
    public class OperationFilteringTests
    {
        private const string CONFIG_FILE = "operation-filter-config.MsSql.json";
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
