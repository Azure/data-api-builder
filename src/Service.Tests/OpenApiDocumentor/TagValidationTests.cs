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
    /// Integration tests validating that OpenAPI tags are correctly deduplicated
    /// and shared between global document tags and operation-level tags.
    /// Covers bug fix for duplicate entity groups in Swagger UI (#2968).
    /// </summary>
    [TestCategory(TestCategory.MSSQL)]
    [TestClass]
    public class TagValidationTests
    {
        private const string CONFIG_FILE = "tag-validation-config.MsSql.json";
        private const string DB_ENV = TestCategory.MSSQL;

        /// <summary>
        /// Validates no duplicate tags and shared tag instances across various entity configurations.
        /// Exercises:
        /// - Multiple entities (one with description, one without)
        /// - Leading slash in REST path
        /// - Default REST path (entity name as path)
        /// - Stored procedure entity
        /// </summary>
        /// <param name="entityName">Entity name.</param>
        /// <param name="configuredRestPath">REST path override (null means use entity name).</param>
        /// <param name="description">Entity description (null means no description).</param>
        /// <param name="sourceType">Source type: Table or StoredProcedure.</param>
        /// <param name="sourceObject">Database source object name.</param>
        [DataRow("book", null, "A book entity", EntitySourceType.Table, "books",
            DisplayName = "Table entity with description and default REST path")]
        [DataRow("author", null, null, EntitySourceType.Table, "authors",
            DisplayName = "Table entity without description and default REST path")]
        [DataRow("genre", "/Genre", "Genre entity", EntitySourceType.Table, "brokers",
            DisplayName = "Table entity with leading slash REST path and description")]
        [DataRow("sp_entity", null, "SP description", EntitySourceType.StoredProcedure, "insert_and_display_all_books_for_given_publisher",
            DisplayName = "Stored procedure entity with description")]
        [DataTestMethod]
        public async Task NoDuplicateTags_AndSharedInstances(
            string entityName,
            string configuredRestPath,
            string description,
            EntitySourceType sourceType,
            string sourceObject)
        {
            // Arrange: Create a multi-entity configuration.
            // Always include a secondary entity so we exercise multi-entity deduplication.
            Entity primaryEntity = CreateEntity(sourceObject, sourceType, configuredRestPath, description);
            Entity secondaryEntity = CreateEntity("publishers", EntitySourceType.Table, null, "Secondary entity for dedup test");

            Dictionary<string, Entity> entities = new()
            {
                { entityName, primaryEntity },
                { "publisher", secondaryEntity }
            };

            RuntimeEntities runtimeEntities = new(entities);
            OpenApiDocument doc = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CONFIG_FILE,
                databaseEnvironment: DB_ENV);

            // Assert: No duplicate tag names in global tags
            List<string> tagNames = doc.Tags.Select(t => t.Name).ToList();
            List<string> distinctTagNames = tagNames.Distinct().ToList();
            Assert.AreEqual(distinctTagNames.Count, tagNames.Count,
                $"Duplicate tags found in OpenAPI document. Tags: {string.Join(", ", tagNames)}");

            // Assert: The expected REST path (normalized, no leading slash) is present as a tag
            string expectedTagName = configuredRestPath?.TrimStart('/') ?? entityName;
            Assert.IsTrue(doc.Tags.Any(t => t.Name == expectedTagName),
                $"Expected tag '{expectedTagName}' not found. Actual tags: {string.Join(", ", tagNames)}");

            // Assert: All operation tags reference the same instance as global tags
            AssertOperationTagsAreSharedInstances(doc);
        }

        // Note: A test for duplicate REST paths (e.g., two entities both mapped to "/SharedPath") is intentionally
        // omitted because RuntimeConfigValidator rejects duplicate REST paths at startup (see RuntimeConfigValidator
        // line ~685). The TryAdd in BuildOpenApiDocument is defensive code for this edge case, but it cannot be
        // exercised through integration tests since the server won't start with an invalid configuration.

        /// <summary>
        /// Validates REST-disabled entities produce no tags and no paths.
        /// </summary>
        [TestMethod]
        public async Task RestDisabledEntity_ProducesNoTagOrPath()
        {
            Entity disabledEntity = new(
                Source: new("books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: EntityRestOptions.DEFAULT_SUPPORTED_VERBS, Path: null, Enabled: false),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
                Mappings: null,
                Relationships: null,
                Description: "Should not appear");

            Entity enabledEntity = CreateEntity("publishers", EntitySourceType.Table, null, "Enabled entity");

            Dictionary<string, Entity> entities = new()
            {
                { "disabled_book", disabledEntity },
                { "publisher", enabledEntity }
            };

            RuntimeEntities runtimeEntities = new(entities);
            OpenApiDocument doc = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CONFIG_FILE,
                databaseEnvironment: DB_ENV);

            Assert.IsFalse(doc.Tags.Any(t => t.Name == "disabled_book"),
                "REST-disabled entity should not have a tag in the OpenAPI document.");
            Assert.IsFalse(doc.Paths.Any(p => p.Key.Contains("disabled_book")),
                "REST-disabled entity should not have paths in the OpenAPI document.");
            Assert.IsTrue(doc.Tags.Any(t => t.Name == "publisher"),
                "Enabled entity should still have a tag.");

            AssertOperationTagsAreSharedInstances(doc);
        }

        /// <summary>
        /// Validates that entities with no permissions produce no tag when viewed
        /// for a specific role that has no access.
        /// </summary>
        [TestMethod]
        public async Task EntityWithNoPermissionsForRole_ProducesNoTag()
        {
            EntityPermission[] permissions = new[]
            {
                new EntityPermission(Role: "admin", Actions: new[]
                {
                    new EntityAction(EntityActionOperation.All, null, new())
                })
            };

            Entity entity = new(
                Source: new("books", EntitySourceType.Table, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: new(Methods: EntityRestOptions.DEFAULT_SUPPORTED_VERBS),
                Permissions: permissions,
                Mappings: null,
                Relationships: null,
                Description: "Admin-only entity");

            Entity publicEntity = CreateEntity("publishers", EntitySourceType.Table, null, "Public entity");

            Dictionary<string, Entity> entities = new()
            {
                { "book", entity },
                { "publisher", publicEntity }
            };

            RuntimeEntities runtimeEntities = new(entities);

            // Request OpenAPI doc for "anonymous" role - book should not appear
            OpenApiDocument doc = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CONFIG_FILE,
                databaseEnvironment: DB_ENV,
                role: "anonymous");

            Assert.IsFalse(doc.Tags.Any(t => t.Name == "book"),
                "Entity with no permissions for 'anonymous' role should not have a tag.");
            Assert.IsFalse(doc.Paths.Any(p => p.Key.Contains("book")),
                "Entity with no permissions for 'anonymous' role should not have paths.");

            AssertOperationTagsAreSharedInstances(doc);
        }

        /// <summary>
        /// Validates that entity descriptions are correctly reflected in OpenAPI tags.
        /// </summary>
        /// <param name="description">Entity description to test.</param>
        /// <param name="shouldHaveDescription">Whether the tag should have a description.</param>
        [DataRow("A meaningful description", true, DisplayName = "Entity with description")]
        [DataRow(null, false, DisplayName = "Entity without description")]
        [DataRow("", false, DisplayName = "Entity with empty description")]
        [DataRow("   ", false, DisplayName = "Entity with whitespace description")]
        [DataTestMethod]
        public async Task TagDescription_MatchesEntityDescription(string description, bool shouldHaveDescription)
        {
            Entity entity = CreateEntity("books", EntitySourceType.Table, null, description);

            Dictionary<string, Entity> entities = new()
            {
                { "book", entity }
            };

            RuntimeEntities runtimeEntities = new(entities);
            OpenApiDocument doc = await OpenApiTestBootstrap.GenerateOpenApiDocumentAsync(
                runtimeEntities: runtimeEntities,
                configFileName: CONFIG_FILE,
                databaseEnvironment: DB_ENV);

            OpenApiTag tag = doc.Tags.FirstOrDefault(t => t.Name == "book");
            Assert.IsNotNull(tag, "Expected tag 'book' to exist.");

            if (shouldHaveDescription)
            {
                Assert.AreEqual(description, tag.Description,
                    $"Tag description should match entity description.");
            }
            else
            {
                Assert.IsNull(tag.Description,
                    "Tag description should be null for empty/whitespace/null entity descriptions.");
            }
        }

        /// <summary>
        /// Asserts that every operation tag in the document is the exact same object instance
        /// as the corresponding tag in the global Tags list. This prevents Swagger UI from
        /// treating them as separate groups.
        /// </summary>
        /// <param name="doc">OpenAPI document to validate.</param>
        private static void AssertOperationTagsAreSharedInstances(OpenApiDocument doc)
        {
            foreach (KeyValuePair<string, OpenApiPathItem> path in doc.Paths)
            {
                foreach (KeyValuePair<OperationType, OpenApiOperation> operation in path.Value.Operations)
                {
                    foreach (OpenApiTag operationTag in operation.Value.Tags)
                    {
                        bool isSharedInstance = doc.Tags.Any(globalTag => ReferenceEquals(globalTag, operationTag));
                        Assert.IsTrue(isSharedInstance,
                            $"Operation tag '{operationTag.Name}' at path '{path.Key}' ({operation.Key}) " +
                            $"is not the same instance as the global tag. This will cause duplicate groups in Swagger UI.");
                    }
                }
            }
        }

        /// <summary>
        /// Helper to create an Entity with common defaults for tag validation tests.
        /// </summary>
        private static Entity CreateEntity(
            string sourceObject,
            EntitySourceType sourceType,
            string configuredRestPath,
            string description)
        {
            return new Entity(
                Source: new(sourceObject, sourceType, null, null),
                Fields: null,
                GraphQL: new(Singular: null, Plural: null, Enabled: false),
                Rest: sourceType == EntitySourceType.StoredProcedure
                    ? new(Methods: EntityRestOptions.DEFAULT_SUPPORTED_VERBS, Path: configuredRestPath)
                    : new(Methods: EntityRestOptions.DEFAULT_SUPPORTED_VERBS, Path: configuredRestPath),
                Permissions: OpenApiTestBootstrap.CreateBasicPermissions(),
                Mappings: null,
                Relationships: null,
                Description: description);
        }
    }
}
