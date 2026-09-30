// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Tests for DescribeEntitiesTool filtering logic (GitHub issue #3043).
    /// Validates that entities with dml-tools: false are filtered from describe_entities,
    /// regardless of entity type (tables, views, stored procedures).
    /// When dml-tools is disabled, entities are not exposed via DML tools and should not appear in describe_entities.
    /// </summary>
    [TestClass]
    public class DescribeEntitiesFilteringTests
    {
        /// <summary>
        /// Verifies that when ALL entities have dml-tools: false,
        /// describe_entities returns an AllEntitiesFilteredDmlDisabled error with guidance.
        /// This ensures users understand why describe_entities is empty.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_AllEntitiesFilteredWhenDmlToolsDisabled()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithCustomToolSP();
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            DescribeEntitiesTool tool = new();

            // Act
            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            // Assert
            AssertErrorResult(result, "AllEntitiesFilteredDmlDisabled");

            // Verify the error message is helpful
            JsonElement content = GetContentFromResult(result);
            content.TryGetProperty("error", out JsonElement error);
            Assert.IsTrue(error.TryGetProperty("message", out JsonElement errorMessage));
            string message = errorMessage.GetString() ?? string.Empty;
            Assert.IsTrue(message.Contains("DML tools disabled") || message.Contains("dml-tools"));
            Assert.IsTrue(message.Contains("tools/list") || message.Contains("custom-tool"));
        }

        /// <summary>
        /// Verifies that stored procedures with dml-tools enabled (or default) appear in describe_entities,
        /// while stored procedures with dml-tools: false are filtered out.
        /// This ensures filtering is based on dml-tools configuration.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_IncludesRegularStoredProcedures()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithMixedStoredProcedures();

            // Act & Assert
            CallToolResult result = await ExecuteToolAsync(config);
            AssertSuccessResultWithEntityNames(result, new[] { "CountBooks" }, new[] { "GetBook" });
        }

        /// <summary>
        /// Verifies that tables and views with default/enabled dml-tools appear in describe_entities,
        /// while stored procedures with dml-tools: false are filtered out.
        /// This ensures filtering applies based on the dml-tools setting, not entity type.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_IncludesTablesAndViewsWithDmlToolsEnabled()
        {
            // Arrange & Act & Assert
            RuntimeConfig config = CreateConfigWithMixedEntityTypes();
            CallToolResult result = await ExecuteToolAsync(config);
            AssertSuccessResultWithEntityNames(result, new[] { "Book", "BookView" }, new[] { "GetBook" });
        }

        /// <summary>
        /// Verifies that the 'count' field in describe_entities response accurately reflects
        /// the number of entities AFTER filtering (excludes entities with dml-tools: false).
        /// This ensures count matches the actual entities array length.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_CountReflectsFilteredList()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithMixedEntityTypes();

            // Act
            CallToolResult result = await ExecuteToolAsync(config);

            // Assert
            Assert.IsTrue(result.IsError == false || result.IsError == null);
            JsonElement content = GetContentFromResult(result);
            Assert.IsTrue(content.TryGetProperty("entities", out JsonElement entities));
            Assert.IsTrue(content.TryGetProperty("count", out JsonElement countElement));

            int entityCount = entities.GetArrayLength();
            Assert.AreEqual(2, entityCount, "Config has 3 entities but only 2 should be returned (entity with dml-tools:false excluded)");
            Assert.AreEqual(entityCount, countElement.GetInt32(), "Count field should match filtered entity array length");
        }

        /// <summary>
        /// Verifies that dml-tools filtering is applied consistently regardless of the nameOnly parameter.
        /// When nameOnly=true (lightweight response), entities with dml-tools: false are still filtered out.
        /// This ensures filtering behavior is consistent across both response modes.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_NameOnlyWorksWithFiltering()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithMixedEntityTypes();
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            DescribeEntitiesTool tool = new();
            JsonDocument arguments = JsonDocument.Parse("{\"nameOnly\": true}");

            // Act
            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);

            // Assert
            AssertSuccessResultWithEntityNames(result, new[] { "Book", "BookView" }, new[] { "GetBook" });
        }

        /// <summary>
        /// Test that NoEntitiesConfigured error is returned when runtime config truly has no entities.
        /// This is different from AllEntitiesFilteredDmlDisabled where entities exist but are filtered.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_ReturnsNoEntitiesConfigured_WhenConfigHasNoEntities()
        {
            // Arrange & Act
            RuntimeConfig config = CreateConfigWithNoEntities();
            CallToolResult result = await ExecuteToolAsync(config);

            // Assert
            AssertErrorResult(result, "NoEntitiesConfigured");

            // Verify the error message indicates no entities configured
            JsonElement content = GetContentFromResult(result);
            content.TryGetProperty("error", out JsonElement error);
            Assert.IsTrue(error.TryGetProperty("message", out JsonElement errorMessage));
            string message = errorMessage.GetString() ?? string.Empty;
            Assert.IsTrue(message.Contains("No entities are configured"));
        }

        [TestMethod]
        public async Task DescribeEntities_ReturnsToolDisabled_WhenDescribeToolIsDisabled()
        {
            RuntimeConfig config = new(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(Enabled: true, Path: "/mcp", DmlTools: DmlToolsConfig.FromBoolean(false)),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
                Entities: new(new Dictionary<string, Entity>()));

            CallToolResult result = await new DescribeEntitiesTool().ExecuteAsync(
                null, CreateServiceProvider(config), CancellationToken.None);

            AssertErrorResult(result, "ToolDisabled");
        }

        [TestMethod]
        public async Task DescribeEntities_ReturnsOperationCanceled_WhenCancellationRequested()
        {
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            CallToolResult result = await new DescribeEntitiesTool().ExecuteAsync(
                null, CreateServiceProvider(CreateConfigWithNoEntities()), cancellation.Token);

            AssertErrorResult(result, "OperationCanceled");
        }

        [TestMethod]
        public async Task DescribeEntities_ReturnsEntitiesNotFound_ForExplicitMissingFilter()
        {
            using JsonDocument arguments = JsonDocument.Parse("{\"entities\":[\"Missing\",\"  \",42]}");

            CallToolResult result = await new DescribeEntitiesTool().ExecuteAsync(
                arguments, CreateServiceProvider(CreateConfigWithNoEntities()), CancellationToken.None);

            AssertErrorResult(result, "EntitiesNotFound");
        }

        /// <summary>
        /// CRITICAL TEST: Verifies that stored procedures with BOTH custom-tool AND dml-tools enabled
        /// appear in describe_entities. This validates the truth table scenario:
        /// custom-tool: true, dml-tools: true → ✔ describe_entities + ✔ tools/list
        /// 
        /// This test ensures the filtering logic only filters when dml-tools is FALSE,
        /// not just when custom-tool is TRUE.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_IncludesCustomToolWithDmlEnabled()
        {
            // Arrange & Act
            RuntimeConfig config = CreateConfigWithCustomToolAndDmlEnabled();
            CallToolResult result = await ExecuteToolAsync(config);

            // Assert
            AssertSuccessResultWithEntityNames(result, new[] { "GetBook" }, Array.Empty<string>());
        }

        /// <summary>
        /// Verifies that when some (but not all) entities have dml-tools: false,
        /// only non-filtered entities appear in the response.
        /// This validates partial filtering works correctly with accurate count.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_ReturnsOnlyNonFilteredEntities_WhenPartiallyFiltered()
        {
            // Arrange & Act
            RuntimeConfig config = CreateConfigWithMixedEntityTypes();
            CallToolResult result = await ExecuteToolAsync(config);

            // Assert
            AssertSuccessResultWithEntityNames(result, new[] { "Book", "BookView" }, new[] { "GetBook" });

            // Verify count matches
            JsonElement content = GetContentFromResult(result);
            Assert.IsTrue(content.TryGetProperty("count", out JsonElement countElement));
            Assert.AreEqual(2, countElement.GetInt32());
        }

        /// <summary>
        /// Verifies that entities with DML tools disabled (dml-tools: false) are filtered from describe_entities.
        /// This ensures the filtering applies to all entity types, not just stored procedures.
        /// </summary>
        [DataTestMethod]
        [DataRow(EntitySourceType.Table, "Publisher", "Book", DisplayName = "Filters Table with DML disabled")]
        [DataRow(EntitySourceType.View, "Book", "BookView", DisplayName = "Filters View with DML disabled")]
        public async Task DescribeEntities_FiltersEntityWithDmlToolsDisabled(EntitySourceType filteredEntityType, string includedEntityName, string filteredEntityName)
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithEntityDmlDisabled(filteredEntityType, includedEntityName, filteredEntityName);
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            DescribeEntitiesTool tool = new();

            // Act
            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            // Assert
            AssertSuccessResultWithEntityNames(result, new[] { includedEntityName }, new[] { filteredEntityName });
        }

        /// <summary>
        /// Verifies that when ALL entities have dml-tools disabled, the appropriate error is returned.
        /// This tests the error scenario applies to all entity types, not just stored procedures.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_ReturnsAllEntitiesFilteredDmlDisabled_WhenAllEntitiesHaveDmlDisabled()
        {
            // Arrange & Act
            RuntimeConfig config = CreateConfigWithAllEntitiesDmlDisabled();
            CallToolResult result = await ExecuteToolAsync(config);

            // Assert
            AssertErrorResult(result, "AllEntitiesFilteredDmlDisabled");

            // Verify the error message is helpful
            JsonElement content = GetContentFromResult(result);
            content.TryGetProperty("error", out JsonElement error);
            Assert.IsTrue(error.TryGetProperty("message", out JsonElement errorMessage));
            string message = errorMessage.GetString() ?? string.Empty;
            Assert.IsTrue(message.Contains("DML tools disabled"), "Error message should mention DML tools disabled");
            Assert.IsTrue(message.Contains("dml-tools: false"), "Error message should mention the config syntax");
        }

        /// <summary>
        /// Verifies that describe_entities returns a NoEntitiesConfigured error
        /// when the caller's role has no permissions on any entity.
        /// This prevents information disclosure of schema metadata for unauthorized entities.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_RoleWithNoPermissions_ReturnsNoEntitiesError()
        {
            // Arrange - Create config with entities that only the "admin" role can access
            RuntimeConfig config = CreateConfigWithRestrictedRoleAccess();
            IServiceProvider serviceProvider = CreateServiceProvider(config, role: "guest");
            DescribeEntitiesTool tool = new();

            // Act
            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            // Assert - Guest role should see no entities because it has no permissions defined
            AssertErrorResult(result, "NoEntitiesConfigured");
        }

        /// <summary>
        /// Verifies that low-privilege roles see only entities
        /// they have explicit permission on. A "reader" role that has READ permission on Book
        /// should see Book but not GetBook (execute-only SP). Also asserts the exact
        /// permission set so <see cref="DescribeEntitiesTool"/>'s <c>BuildPermissionsInfo</c>
        /// cannot silently return an over-broad set.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_LowPrivRole_SeesOnlyAuthorizedEntities()
        {
            // Arrange - Create config where:
            //   - "Book" entity: reader role has READ permission
            //   - "GetBook" entity: admin role has EXECUTE permission (reader has none)
            RuntimeConfig config = CreateConfigWithMixedRoleAccess();
            IServiceProvider serviceProvider = CreateServiceProvider(config, role: "reader");
            DescribeEntitiesTool tool = new();

            // Act
            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            // Assert - Reader role should see only Book, not GetBook
            AssertSuccessResultWithEntityNames(result, new[] { "Book" }, new[] { "GetBook" });
            AssertEntityPermissions(result, "Book", new[] { "READ" });
        }

        /// <summary>
        /// Verifies that describe_entities returns a NoEntitiesConfigured error
        /// when no role header is provided (unauthenticated caller),
        /// even if some entities have "anonymous" permissions.
        /// describe_entities requires a valid role context to return results.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_NoRole_ReturnsNoEntitiesError()
        {
            // Arrange - Config with entities
            RuntimeConfig config = CreateConfigWithMixedEntityTypes();
            IServiceProvider serviceProvider = CreateServiceProvider(config, role: null);
            DescribeEntitiesTool tool = new();

            // Act
            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            // Assert - No role should result in empty entity list
            AssertErrorResult(result, "NoEntitiesConfigured");
        }

        /// <summary>
        /// Verifies that a high-privilege single role sees every entity it has any permission on
        /// and that the returned permission sets reflect resolver-driven wildcard expansion
        /// (<c>Action=All</c> expands to CRUD on a table, Execute on a stored procedure).
        /// DAB uses a single-role request model: the value validated by <c>IsValidRoleContext</c>
        /// is the role used for the rest of the request, matching REST, GraphQL, and the DML MCP tools.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_AdminRole_SeesEveryAuthorizedEntityWithWildcardExpansion()
        {
            // Arrange - Config where:
            //   - "Book" entity: admin role has Action=All permission
            //   - "GetBook" entity: admin role has EXECUTE permission
            RuntimeConfig config = CreateConfigWithMixedRoleAccess();
            IServiceProvider serviceProvider = CreateServiceProvider(config, role: "admin");
            DescribeEntitiesTool tool = new();

            // Act
            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            // Assert - Admin sees both entities and wildcard 'All' expands to the full CRUD set on Book;
            // GetBook (SP) shows EXECUTE only.
            AssertSuccessResultWithEntityNames(result, new[] { "Book", "GetBook" }, Array.Empty<string>());
            AssertEntityPermissions(result, "Book", new[] { "CREATE", "DELETE", "READ", "UPDATE" });
            AssertEntityPermissions(result, "GetBook", new[] { "EXECUTE" });
        }

        /// <summary>
        /// Verifies that entity visibility and the returned permissions honor the same role-inheritance
        /// chain the production <see cref="AuthorizationResolver"/> applies: anonymous permissions are
        /// inherited by authenticated, and a named role that is not explicitly configured on an entity
        /// falls back to authenticated. Without this, describe_entities would under-report permissions
        /// or hide entities the caller can actually reach via REST/GraphQL.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_HonorsRoleInheritance_AnonymousIntoAuthenticatedIntoNamedRole()
        {
            // Arrange - "Book" grants READ to anonymous only. authenticated and any named role
            // should inherit that READ per resolver semantics.
            RuntimeConfig config = CreateConfigWithAnonymousReadOnlyBook();

            // authenticated inherits anonymous's READ.
            IServiceProvider authedProvider = CreateServiceProvider(config, role: "authenticated");
            CallToolResult authedResult = await new DescribeEntitiesTool().ExecuteAsync(null, authedProvider, CancellationToken.None);
            AssertSuccessResultWithEntityNames(authedResult, new[] { "Book" }, Array.Empty<string>());
            AssertEntityPermissions(authedResult, "Book", new[] { "READ" });

            // Unconfigured named role inherits authenticated → which itself inherited from anonymous.
            IServiceProvider namedProvider = CreateServiceProvider(config, role: "some_unconfigured_role");
            CallToolResult namedResult = await new DescribeEntitiesTool().ExecuteAsync(null, namedProvider, CancellationToken.None);
            AssertSuccessResultWithEntityNames(namedResult, new[] { "Book" }, Array.Empty<string>());
            AssertEntityPermissions(namedResult, "Book", new[] { "READ" });
        }

        /// <summary>
        /// Column-level authorization regression: when a role's READ permission has fields.exclude
        /// on a sensitive column, that column must be absent from fields[] while permitted columns
        /// remain. Verifies <see cref="DescribeEntitiesTool"/>'s use of
        /// <see cref="IAuthorizationResolver.GetAllowedExposedColumns"/> for column projection.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_ExcludesRestrictedColumnsFromFieldsArray()
        {
            const string EntityName = "Book";
            List<FieldMetadata> fields = new()
            {
                new() { Name = "title",        Description = "Book title" },
                new() { Name = "publisher_id", Description = "Publisher FK" },
                new() { Name = "salary",       Description = "Internal cost" }   // sensitive – excluded
            };

            Entity bookEntity = new(
                Source: new("books", EntitySourceType.Table, null, null),
                GraphQL: new(EntityName, "Books"),
                Fields: fields,
                Rest: new(Enabled: true),
                Permissions: new[]
                {
                    new EntityPermission(Role: "anonymous", Actions: new[]
                    {
                        new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null)
                    })
                },
                Mappings: null,
                Relationships: null,
                Mcp: null);

            RuntimeConfig config = CreateRuntimeConfig(new Dictionary<string, Entity> { [EntityName] = bookEntity });

            // anonymous READ exposes title and publisher_id but not salary.
            HashSet<string> allowedColumns = new(StringComparer.OrdinalIgnoreCase) { "title", "publisher_id" };
            IServiceProvider serviceProvider = CreateServiceProviderWithColumnAccess(config, role: "anonymous", allowedColumns);
            DescribeEntitiesTool tool = new();

            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            Assert.IsTrue(result.IsError == false || result.IsError == null);
            JsonElement content = GetContentFromResult(result);
            JsonElement entity = content.GetProperty("entities").EnumerateArray().Single();
            JsonElement returnedFields = entity.GetProperty("fields");

            List<string> fieldNames = returnedFields.EnumerateArray()
                .Select(f => f.GetProperty("name").GetString()!)
                .ToList();

            CollectionAssert.Contains(fieldNames, "title", "title should be visible");
            CollectionAssert.Contains(fieldNames, "publisher_id", "publisher_id should be visible");
            Assert.IsFalse(fieldNames.Contains("salary"), "salary must be excluded by column-level authz");
        }

        /// <summary>
        /// SP entities must not apply column-level filtering: <c>ComputeAllowedFieldNames</c>
        /// returns null for stored procedures, so every entry in Fields passes through untouched.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_StoredProcedure_DoesNotFilterFields()
        {
            const string EntityName = "GetBook";
            List<FieldMetadata> resultFields = new()
            {
                new() { Name = "id",    Description = "Book id" },
                new() { Name = "title", Description = "Book title" }
            };

            Entity spEntity = new(
                Source: new("get_book", EntitySourceType.StoredProcedure, null, null),
                GraphQL: new(EntityName, EntityName),
                Fields: resultFields,
                Rest: new(Enabled: true),
                Permissions: new[]
                {
                    new EntityPermission(Role: "anonymous", Actions: new[]
                    {
                        new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null)
                    })
                },
                Mappings: null,
                Relationships: null,
                Mcp: null);

            RuntimeConfig config = CreateRuntimeConfig(new Dictionary<string, Entity> { [EntityName] = spEntity });

            // Even with an empty allowed-column set the SP fields must not be filtered.
            IServiceProvider serviceProvider = CreateServiceProviderWithColumnAccess(config, role: "anonymous", allowedColumns: new HashSet<string>());
            DescribeEntitiesTool tool = new();

            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            Assert.IsTrue(result.IsError == false || result.IsError == null);
            JsonElement content = GetContentFromResult(result);
            JsonElement entity = content.GetProperty("entities").EnumerateArray().Single();
            JsonElement returnedFields = entity.GetProperty("fields");

            List<string> fieldNames = returnedFields.EnumerateArray()
                .Select(f => f.GetProperty("name").GetString()!)
                .ToList();

            CollectionAssert.Contains(fieldNames, "id", "SP result field 'id' must not be filtered");
            CollectionAssert.Contains(fieldNames, "title", "SP result field 'title' must not be filtered");
        }

        #region Helper Methods

        /// <summary>
        /// Executes the DescribeEntitiesTool with the given config.
        /// </summary>
        private static async Task<CallToolResult> ExecuteToolAsync(RuntimeConfig config, JsonDocument arguments = null)
        {
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            DescribeEntitiesTool tool = new();
            return await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);
        }

        /// <summary>
        /// Runs the DescribeEntitiesTool and asserts successful execution with expected entity names.
        /// </summary>
        private static void AssertSuccessResultWithEntityNames(CallToolResult result, string[] includedEntities, string[] excludedEntities)
        {
            Assert.IsTrue(result.IsError == false || result.IsError == null);
            JsonElement content = GetContentFromResult(result);
            Assert.IsTrue(content.TryGetProperty("entities", out JsonElement entities));

            List<string> entityNames = entities.EnumerateArray()
                .Select(e => e.GetProperty("name").GetString()!)
                .ToList();

            foreach (string includedEntity in includedEntities)
            {
                Assert.IsTrue(entityNames.Contains(includedEntity), $"{includedEntity} should be included");
            }

            foreach (string excludedEntity in excludedEntities)
            {
                Assert.IsFalse(entityNames.Contains(excludedEntity), $"{excludedEntity} should be excluded");
            }

            Assert.AreEqual(includedEntities.Length, entities.GetArrayLength());
        }

        /// <summary>
        /// Asserts that the result contains an error with the specified type.
        /// </summary>
        private static void AssertErrorResult(CallToolResult result, string expectedErrorType)
        {
            Assert.IsTrue(result.IsError == true);
            JsonElement content = GetContentFromResult(result);
            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            Assert.IsTrue(error.TryGetProperty("type", out JsonElement errorType));
            Assert.AreEqual(expectedErrorType, errorType.GetString());
        }

        /// <summary>
        /// Asserts that the entity's permissions array is exactly the given set (order-insensitive,
        /// case-insensitive). Guards against BuildPermissionsInfo silently returning a partial or
        /// over-broad union of the caller's roles' operations.
        /// </summary>
        private static void AssertEntityPermissions(CallToolResult result, string entityName, string[] expectedPermissions)
        {
            JsonElement content = GetContentFromResult(result);
            Assert.IsTrue(content.TryGetProperty("entities", out JsonElement entities));

            JsonElement entity = entities.EnumerateArray()
                .FirstOrDefault(e => string.Equals(e.GetProperty("name").GetString(), entityName, StringComparison.Ordinal));
            Assert.AreNotEqual(default(JsonElement).ValueKind, entity.ValueKind, $"entity '{entityName}' not found in result");

            Assert.IsTrue(entity.TryGetProperty("permissions", out JsonElement permissions), $"entity '{entityName}' missing 'permissions'");

            HashSet<string> actual = permissions.EnumerateArray()
                .Select(p => p.GetString()!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            HashSet<string> expected = new(expectedPermissions, StringComparer.OrdinalIgnoreCase);

            Assert.IsTrue(
                actual.SetEquals(expected),
                $"entity '{entityName}' permissions mismatch. expected=[{string.Join(",", expected.OrderBy(s => s))}] actual=[{string.Join(",", actual.OrderBy(s => s))}]");
        }

        /// <summary>
        /// Creates a basic entity with standard permissions.
        /// </summary>
        private static Entity CreateEntity(string sourceName, EntitySourceType sourceType, string singularName, string pluralName, EntityMcpOptions mcpOptions = null)
        {
            EntityActionOperation action = sourceType == EntitySourceType.StoredProcedure
                ? EntityActionOperation.Execute
                : EntityActionOperation.Read;

            return new Entity(
                Source: new(sourceName, sourceType, null, null),
                GraphQL: new(singularName, pluralName),
                Fields: null,
                Rest: new(Enabled: true),
                Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(Action: action, Fields: null, Policy: null) }) },
                Mappings: null,
                Relationships: null,
                Mcp: mcpOptions
            );
        }

        /// <summary>
        /// Creates a runtime config with the specified entities.
        /// </summary>
        private static RuntimeConfig CreateRuntimeConfig(Dictionary<string, Entity> entities)
        {
            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(Enabled: true, Path: "/mcp", DmlTools: null),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(entities)
            );
        }

        /// <summary>
        /// Creates a runtime config with a stored procedure that has dml-tools: false.
        /// Used to test the AllEntitiesFilteredDmlDisabled error scenario.
        /// </summary>
        private static RuntimeConfig CreateConfigWithCustomToolSP()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["GetBook"] = CreateEntity("get_book", EntitySourceType.StoredProcedure, "GetBook", "GetBook",
                    new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: false))
            };

            return CreateRuntimeConfig(entities);
        }

        /// <summary>
        /// Creates a runtime config with mixed stored procedures:
        /// one SP with dml-tools enabled/default (CountBooks) and one with dml-tools: false (GetBook).
        /// Used to test that filtering is based on dml-tools configuration.
        /// </summary>
        private static RuntimeConfig CreateConfigWithMixedStoredProcedures()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["CountBooks"] = CreateEntity("count_books", EntitySourceType.StoredProcedure, "CountBooks", "CountBooks"),
                ["GetBook"] = CreateEntity("get_book", EntitySourceType.StoredProcedure, "GetBook", "GetBook",
                    new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: false))
            };

            return CreateRuntimeConfig(entities);
        }

        /// <summary>
        /// Creates a runtime config with mixed entity types:
        /// table (Book), view (BookView), and SP with dml-tools: false (GetBook).
        /// Used to test that filtering applies to all entity types based on dml-tools setting.
        /// </summary>
        private static RuntimeConfig CreateConfigWithMixedEntityTypes()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["Book"] = CreateEntity("books", EntitySourceType.Table, "Book", "Books"),
                ["BookView"] = CreateEntity("book_view", EntitySourceType.View, "BookView", "BookViews"),
                ["GetBook"] = CreateEntity("get_book", EntitySourceType.StoredProcedure, "GetBook", "GetBook",
                    new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: false))
            };

            return CreateRuntimeConfig(entities);
        }

        /// <summary>
        /// Creates a runtime config with an empty entities dictionary.
        /// Used to test the NoEntitiesConfigured error when no entities are configured at all.
        /// </summary>
        private static RuntimeConfig CreateConfigWithNoEntities()
        {
            return CreateRuntimeConfig(new Dictionary<string, Entity>());
        }

        /// <summary>
        /// Creates a runtime config with a stored procedure that has BOTH custom-tool and dml-tools enabled.
        /// Used to test the truth table scenario: custom-tool:true + dml-tools:true → should appear in describe_entities.
        /// </summary>
        private static RuntimeConfig CreateConfigWithCustomToolAndDmlEnabled()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["GetBook"] = CreateEntity("get_book", EntitySourceType.StoredProcedure, "GetBook", "GetBook",
                    new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: true))
            };

            return CreateRuntimeConfig(entities);
        }

        /// <summary>
        /// Creates a runtime config with an entity that has dml-tools disabled.
        /// Used to test that entities with dml-tools: false are filtered from describe_entities.
        /// </summary>
        private static RuntimeConfig CreateConfigWithEntityDmlDisabled(EntitySourceType filteredEntityType, string includedEntityName, string filteredEntityName)
        {
            Dictionary<string, Entity> entities = new();

            // Add the included entity (different type based on what's being filtered)
            if (filteredEntityType == EntitySourceType.Table)
            {
                entities[includedEntityName] = CreateEntity("publishers", EntitySourceType.Table, includedEntityName, $"{includedEntityName}s",
                    new EntityMcpOptions(customToolEnabled: null, dmlToolsEnabled: true));
                entities[filteredEntityName] = CreateEntity("books", EntitySourceType.Table, filteredEntityName, $"{filteredEntityName}s",
                    new EntityMcpOptions(customToolEnabled: null, dmlToolsEnabled: false));
            }
            else if (filteredEntityType == EntitySourceType.View)
            {
                entities[includedEntityName] = CreateEntity("books", EntitySourceType.Table, includedEntityName, $"{includedEntityName}s");
                entities[filteredEntityName] = CreateEntity("book_view", EntitySourceType.View, filteredEntityName, $"{filteredEntityName}s",
                    new EntityMcpOptions(customToolEnabled: null, dmlToolsEnabled: false));
            }

            return CreateRuntimeConfig(entities);
        }

        /// <summary>
        /// Creates a runtime config where all entities have dml-tools disabled.
        /// Used to test the AllEntitiesFilteredDmlDisabled error scenario.
        /// </summary>
        private static RuntimeConfig CreateConfigWithAllEntitiesDmlDisabled()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["Book"] = CreateEntity("books", EntitySourceType.Table, "Book", "Books",
                    new EntityMcpOptions(customToolEnabled: null, dmlToolsEnabled: false)),
                ["BookView"] = CreateEntity("book_view", EntitySourceType.View, "BookView", "BookViews",
                    new EntityMcpOptions(customToolEnabled: null, dmlToolsEnabled: false)),
                ["GetBook"] = CreateEntity("get_book", EntitySourceType.StoredProcedure, "GetBook", "GetBook",
                    new EntityMcpOptions(customToolEnabled: false, dmlToolsEnabled: false))
            };

            return CreateRuntimeConfig(entities);
        }

        /// <summary>
        /// Creates a runtime config with restricted role access.
        /// Only "admin" role has READ permission on Book. 
        /// "guest" role has no permissions on any entity.
        /// Used to test that roles without any entity permissions see no entities.
        /// </summary>
        private static RuntimeConfig CreateConfigWithRestrictedRoleAccess()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["Book"] = new Entity(
                    Source: new("books", EntitySourceType.Table, null, null),
                    GraphQL: new("Book", "Books"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[]
                    {
                        new EntityPermission(Role: "admin", Actions: new[] { new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null) })
                    },
                    Mappings: null,
                    Relationships: null,
                    Mcp: null
                )
            };

            return CreateRuntimeConfig(entities);
        }

        /// <summary>
        /// Creates a runtime config with mixed role access.
        /// "reader" role has READ permission on Book table.
        /// "admin" role has EXECUTE permission on GetBook stored procedure.
        /// Used to test that describe_entities shows only entities a role has permissions for.
        /// </summary>
        private static RuntimeConfig CreateConfigWithMixedRoleAccess()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["Book"] = new Entity(
                    Source: new("books", EntitySourceType.Table, null, null),
                    GraphQL: new("Book", "Books"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[]
                    {
                        new EntityPermission(Role: "reader", Actions: new[] { new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null) }),
                        new EntityPermission(Role: "admin", Actions: new[] { new EntityAction(Action: EntityActionOperation.All, Fields: null, Policy: null) })
                    },
                    Mappings: null,
                    Relationships: null,
                    Mcp: null
                ),
                ["GetBook"] = new Entity(
                    Source: new("get_book", EntitySourceType.StoredProcedure, null, null),
                    GraphQL: new("GetBook", "GetBook"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[]
                    {
                        new EntityPermission(Role: "admin", Actions: new[] { new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null) })
                    },
                    Mappings: null,
                    Relationships: null,
                    Mcp: null
                )
            };

            return CreateRuntimeConfig(entities);
        }

        /// <summary>
        /// Creates a runtime config where "Book" grants READ to anonymous only, with no other roles
        /// declared on the entity. Used to verify role inheritance semantics: authenticated should
        /// inherit anonymous's READ, and any named role should fall through authenticated → anonymous.
        /// </summary>
        private static RuntimeConfig CreateConfigWithAnonymousReadOnlyBook()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["Book"] = new Entity(
                    Source: new("books", EntitySourceType.Table, null, null),
                    GraphQL: new("Book", "Books"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[]
                    {
                        new EntityPermission(
                            Role: AuthorizationResolver.ROLE_ANONYMOUS,
                            Actions: new[] { new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null) })
                    },
                    Mappings: null,
                    Relationships: null,
                    Mcp: null
                )
            };

            return CreateRuntimeConfig(entities);
        }

        /// <summary>
        /// <summary>
        /// Creates a service provider with mocked dependencies for testing DescribeEntitiesTool.
        /// Wires a real <see cref="DefaultHttpContext"/> populated with a <see cref="ClaimsPrincipal"/>
        /// that carries a role claim, and mocks <see cref="IAuthorizationResolver.IsValidRoleContext"/>
        /// with the exact production semantic (<c>User.IsInRole(header)</c>), so the auth boundary
        /// is exercised through real claim/header logic rather than an unconditional test bypass.
        /// </summary>
        private static IServiceProvider CreateServiceProvider(RuntimeConfig config, string? role = "anonymous")
        {
            ServiceCollection services = new();

            // Use shared test helper to create RuntimeConfigProvider
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            services.AddSingleton<RuntimeConfigProvider>(sp => configProvider);

            // Build a real HttpContext with the X-MS-API-ROLE header set and, when a role is provided,
            // a ClaimsPrincipal carrying that role claim. This matches production wiring: the header
            // is what IsValidRoleContext reads, and IsInRole checks the principal's claims.
            DefaultHttpContext httpContext = new();
            if (role != null)
            {
                httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = role;
                ClaimsIdentity identity = new(
                    new[] { new Claim(ClaimTypes.Role, role) },
                    authenticationType: "TestAuth");
                httpContext.User = new ClaimsPrincipal(identity);
            }

            // Mock IAuthorizationResolver
            Mock<IAuthorizationResolver> mockAuthResolver = new();

            // Exact production semantic: exactly-one non-empty header value + User.IsInRole(header).
            // Aaron flagged the previous "return role != null" mock as bypassing the auth boundary;
            // this replicates the production check so a comma-in-header case (case #3 in the review)
            // would not silently split into two roles.
            mockAuthResolver
                .Setup(x => x.IsValidRoleContext(It.IsAny<HttpContext>()))
                .Returns((HttpContext ctx) =>
                {
                    string headerValue = ctx.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].ToString();
                    return !string.IsNullOrWhiteSpace(headerValue)
                        && ctx.User is not null
                        && ctx.User.IsInRole(headerValue);
                });

            // Model production AreRoleAndOperationDefinedForEntity semantics over the test config:
            //   * wildcard EntityActionOperation.All expands to CRUD (table/view) or Execute (SP);
            //   * anonymous permissions are inherited by authenticated (setup-time copy);
            //   * a named role not configured on the entity falls back to authenticated (which itself
            //     may have inherited from anonymous). Mirrors AuthorizationResolver.GetEffectiveRoleName.
            mockAuthResolver
                .Setup(x => x.AreRoleAndOperationDefinedForEntity(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EntityActionOperation>()))
                .Returns((string entityName, string requestedRole, EntityActionOperation op) =>
                    IsRoleAndOperationDefinedForEntity(config, entityName, requestedRole, op));

            services.AddSingleton(mockAuthResolver.Object);

            Mock<IHttpContextAccessor> mockHttpContextAccessor = new();
            mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
            services.AddSingleton(mockHttpContextAccessor.Object);

            // Register a stub IMetadataProviderFactory that returns a populated DatabaseStoredProcedure
            // for every stored-procedure entity in the config. DescribeEntitiesTool requires DB metadata
            // for SP entities (an init invariant); these filtering tests only exercise the filter logic,
            // so an empty-parameter populated entry is enough to satisfy the invariant without affecting
            // what is being tested.
            RegisterStubMetadataProvider(services, config);

            // Add logging
            services.AddLogging();

            return services.BuildServiceProvider();
        }

        /// <summary>
        /// Resolves whether (<paramref name="requestedRole"/>, <paramref name="op"/>) has a permission
        /// defined for <paramref name="entityName"/> in <paramref name="config"/>, mirroring the
        /// production <c>AuthorizationResolver</c>: wildcard actions are expanded, anonymous permissions
        /// are inherited by authenticated, and a named role not configured on the entity falls back to
        /// authenticated.
        /// </summary>
        private static bool IsRoleAndOperationDefinedForEntity(
            RuntimeConfig config,
            string entityName,
            string requestedRole,
            EntityActionOperation op)
        {
            if (!config.Entities.TryGetValue(entityName, out Entity? entity) || entity.Permissions == null)
            {
                return false;
            }

            HashSet<EntityActionOperation> validOperations = entity.Source.Type == EntitySourceType.StoredProcedure
                ? EntityAction.ValidStoredProcedurePermissionOperations
                : EntityAction.ValidPermissionOperations;

            // Only ask about operations that are valid for this entity's source type;
            // e.g. CRUD ops on a stored procedure entity never resolve to true.
            if (!validOperations.Contains(op))
            {
                return false;
            }

            static bool RoleHasOp(Entity entity, string role, EntityActionOperation op, HashSet<EntityActionOperation> validOps)
            {
                foreach (EntityPermission permission in entity.Permissions)
                {
                    if (!string.Equals(permission.Role, role, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (permission.Actions == null)
                    {
                        continue;
                    }

                    foreach (EntityAction action in permission.Actions)
                    {
                        if (action.Action == EntityActionOperation.All && validOps.Contains(op))
                        {
                            return true;
                        }

                        if (action.Action == op)
                        {
                            return true;
                        }
                    }
                }

                return false;
            }

            bool anonymousHasOp = RoleHasOp(entity, AuthorizationResolver.ROLE_ANONYMOUS, op, validOperations);

            if (string.Equals(requestedRole, AuthorizationResolver.ROLE_ANONYMOUS, StringComparison.OrdinalIgnoreCase))
            {
                return anonymousHasOp;
            }

            bool authenticatedHasOp = RoleHasOp(entity, AuthorizationResolver.ROLE_AUTHENTICATED, op, validOperations) || anonymousHasOp;

            if (string.Equals(requestedRole, AuthorizationResolver.ROLE_AUTHENTICATED, StringComparison.OrdinalIgnoreCase))
            {
                return authenticatedHasOp;
            }

            // Named role: use its own permissions if configured on the entity, else fall through to authenticated.
            bool namedRoleConfigured = entity.Permissions.Any(p =>
                string.Equals(p.Role, requestedRole, StringComparison.OrdinalIgnoreCase));

            return namedRoleConfigured
                ? RoleHasOp(entity, requestedRole, op, validOperations)
                : authenticatedHasOp;
        }

        /// <summary>
        /// Registers a stub <see cref="IMetadataProviderFactory"/> that exposes a populated
        /// <see cref="DatabaseStoredProcedure"/> (with an empty <see cref="StoredProcedureDefinition.Parameters"/>
        /// dictionary) for every stored-procedure entity in <paramref name="config"/>. Mirrors the production
        /// invariant that init populates DB metadata for every SP entity before describe_entities runs.
        /// </summary>
        private static void RegisterStubMetadataProvider(ServiceCollection services, RuntimeConfig config)
        {
            Dictionary<string, DatabaseObject> entityMap = new();
            foreach (KeyValuePair<string, Entity> entry in config.Entities)
            {
                if (entry.Value.Source.Type == EntitySourceType.StoredProcedure)
                {
                    entityMap[entry.Key] = new DatabaseStoredProcedure(schemaName: "dbo", tableName: entry.Value.Source.Object)
                    {
                        SourceType = EntitySourceType.StoredProcedure,
                        StoredProcedureDefinition = new StoredProcedureDefinition
                        {
                            Parameters = new Dictionary<string, ParameterDefinition>()
                        }
                    };
                }
            }

            Mock<ISqlMetadataProvider> mockSqlMetadataProvider = new();
            mockSqlMetadataProvider.Setup(x => x.EntityToDatabaseObject).Returns(entityMap);
            mockSqlMetadataProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.MSSQL);

            Mock<IMetadataProviderFactory> mockMetadataProviderFactory = new();
            mockMetadataProviderFactory
                .Setup(x => x.GetMetadataProvider(It.IsAny<string>()))
                .Returns(mockSqlMetadataProvider.Object);
            services.AddSingleton(mockMetadataProviderFactory.Object);
        }

        /// <summary>
        /// Extracts and parses the JSON content from an MCP tool call result.
        /// Returns the root JsonElement for assertion purposes.
        /// </summary>
        private static JsonElement GetContentFromResult(CallToolResult result)
        {
            Assert.IsNotNull(result.Content);
            Assert.IsTrue(result.Content.Count > 0);

            // Verify the content block is the expected type before casting
            Assert.IsInstanceOfType(result.Content[0], typeof(TextContentBlock),
                "Expected first content block to be TextContentBlock");

            TextContentBlock firstContent = (TextContentBlock)result.Content[0];
            Assert.IsNotNull(firstContent.Text);

            return JsonDocument.Parse(firstContent.Text).RootElement;
        }

        /// <summary>
        /// Variant of <see cref="CreateServiceProvider"/> that also mocks
        /// <see cref="IAuthorizationResolver.GetAllowedExposedColumns"/> so column-level
        /// filtering tests can control which field names are projected for a given role.
        /// </summary>
        private static IServiceProvider CreateServiceProviderWithColumnAccess(
            RuntimeConfig config,
            string? role,
            HashSet<string> allowedColumns)
        {
            ServiceCollection services = new();

            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            services.AddSingleton<RuntimeConfigProvider>(sp => configProvider);

            DefaultHttpContext httpContext = new();
            if (role != null)
            {
                httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = role;
                ClaimsIdentity identity = new(
                    new[] { new Claim(ClaimTypes.Role, role) },
                    authenticationType: "TestAuth");
                httpContext.User = new ClaimsPrincipal(identity);
            }

            Mock<IAuthorizationResolver> mockAuthResolver = new();
            mockAuthResolver
                .Setup(x => x.IsValidRoleContext(It.IsAny<HttpContext>()))
                .Returns((HttpContext ctx) =>
                {
                    string headerValue = ctx.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER].ToString();
                    return !string.IsNullOrWhiteSpace(headerValue)
                        && ctx.User is not null
                        && ctx.User.IsInRole(headerValue);
                });
            mockAuthResolver
                .Setup(x => x.AreRoleAndOperationDefinedForEntity(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EntityActionOperation>()))
                .Returns((string entityName, string requestedRole, EntityActionOperation op) =>
                    IsRoleAndOperationDefinedForEntity(config, entityName, requestedRole, op));
            // Return the caller-supplied set for every entity/role/operation combination so the
            // test fully controls which column names pass through ComputeAllowedFieldNames.
            mockAuthResolver
                .Setup(x => x.GetAllowedExposedColumns(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EntityActionOperation>()))
                .Returns(allowedColumns);

            services.AddSingleton(mockAuthResolver.Object);

            Mock<IHttpContextAccessor> mockHttpContextAccessor = new();
            mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);
            services.AddSingleton(mockHttpContextAccessor.Object);

            RegisterStubMetadataProvider(services, config);
            services.AddLogging();

            return services.BuildServiceProvider();
        }

        #endregion
    }
}
