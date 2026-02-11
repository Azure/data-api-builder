// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
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
        /// Creates a service provider with mocked dependencies for testing DescribeEntitiesTool.
        /// Configures anonymous role and necessary DAB services.
        /// </summary>
        private static IServiceProvider CreateServiceProvider(RuntimeConfig config)
        {
            ServiceCollection services = new();

            // Use shared test helper to create RuntimeConfigProvider
            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            services.AddSingleton(configProvider);

            // Mock IAuthorizationResolver
            Mock<IAuthorizationResolver> mockAuthResolver = new();
            mockAuthResolver.Setup(x => x.IsValidRoleContext(It.IsAny<HttpContext>())).Returns(true);
            services.AddSingleton(mockAuthResolver.Object);

            // Mock HttpContext with anonymous role
            Mock<HttpContext> mockHttpContext = new();
            Mock<HttpRequest> mockRequest = new();
            mockRequest.Setup(x => x.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns("anonymous");
            mockHttpContext.Setup(x => x.Request).Returns(mockRequest.Object);

            Mock<IHttpContextAccessor> mockHttpContextAccessor = new();
            mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext.Object);
            services.AddSingleton(mockHttpContextAccessor.Object);

            // Add logging
            services.AddLogging();

            return services.BuildServiceProvider();
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

        #endregion
    }
}
