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
    /// Tests validating OpenAPI schema filters fields based on entity permissions.
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class FieldFilteringTests
    {
        private const string CONFIG_FILE = "field-filter-config.MsSql.json";
        private const string DB_ENV = TestCategory.MSSQL;

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
