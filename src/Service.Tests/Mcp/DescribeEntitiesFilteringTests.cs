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
    /// Ensures stored procedures with custom-tool enabled are excluded from describe_entities results
    /// to prevent duplication (they appear in tools/list instead).
    /// Regular entities (tables, views, non-custom-tool SPs) remain visible in describe_entities.
    /// </summary>
    [TestClass]
    public class DescribeEntitiesFilteringTests
    {
        /// <summary>
        /// Verifies that when ALL entities are stored procedures with custom-tool enabled,
        /// describe_entities returns an AllEntitiesFilteredAsCustomTools error with guidance
        /// to use tools/list instead. This ensures users understand why describe_entities is empty.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_ExcludesCustomToolStoredProcedures()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithCustomToolSP();
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            DescribeEntitiesTool tool = new();

            // Act
            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            // Assert
            // When all entities are custom-tool SPs, they're all filtered out, so we get a specific error
            Assert.IsTrue(result.IsError == true);
            JsonElement content = GetContentFromResult(result);
            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            Assert.IsTrue(error.TryGetProperty("type", out JsonElement errorType));
            Assert.AreEqual("AllEntitiesFilteredAsCustomTools", errorType.GetString());

            // Verify the error message is helpful
            Assert.IsTrue(error.TryGetProperty("message", out JsonElement errorMessage));
            string message = errorMessage.GetString() ?? string.Empty;
            Assert.IsTrue(message.Contains("custom tools"));
            Assert.IsTrue(message.Contains("tools/list"));
        }

        /// <summary>
        /// Verifies that stored procedures WITHOUT custom-tool enabled still appear in describe_entities,
        /// while stored procedures WITH custom-tool enabled are filtered out.
        /// This ensures filtering is selective and only applies to custom-tool SPs.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_IncludesRegularStoredProcedures()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithMixedStoredProcedures();
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            DescribeEntitiesTool tool = new();

            // Act
            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsError == false || result.IsError == null);
            JsonElement content = GetContentFromResult(result);
            Assert.IsTrue(content.TryGetProperty("entities", out JsonElement entities));

            List<string> entityNames = entities.EnumerateArray()
                .Select(e => e.GetProperty("name").GetString()!)
                .ToList();

            // CountBooks has no custom-tool config, should be included
            Assert.IsTrue(entityNames.Contains("CountBooks"));
            // GetBook has custom-tool enabled, should be excluded
            Assert.IsFalse(entityNames.Contains("GetBook"));
        }

        /// <summary>
        /// Verifies that custom-tool filtering ONLY applies to stored procedures.
        /// Tables and views always appear in describe_entities regardless of any custom-tool configuration.
        /// This ensures filtering doesn't accidentally hide non-SP entities.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_TablesAndViewsUnaffectedByFiltering()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithMixedEntityTypes();
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            DescribeEntitiesTool tool = new();

            // Act
            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsError == false || result.IsError == null);
            JsonElement content = GetContentFromResult(result);
            Assert.IsTrue(content.TryGetProperty("entities", out JsonElement entities));

            List<string> entityNames = entities.EnumerateArray()
                .Select(e => e.GetProperty("name").GetString()!)
                .ToList();

            // Tables and views should always appear
            Assert.IsTrue(entityNames.Contains("Book"));
            Assert.IsTrue(entityNames.Contains("BookView"));
            // Custom-tool SP should be excluded
            Assert.IsFalse(entityNames.Contains("GetBook"));
        }

        /// <summary>
        /// Verifies that the 'count' field in describe_entities response accurately reflects
        /// the number of entities AFTER filtering (excludes custom-tool stored procedures).
        /// This ensures count matches the actual entities array length.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_CountReflectsFilteredList()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithMixedEntityTypes();
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            DescribeEntitiesTool tool = new();

            // Act
            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsError == false || result.IsError == null);
            JsonElement content = GetContentFromResult(result);
            Assert.IsTrue(content.TryGetProperty("entities", out JsonElement entities));

            int entityCount = entities.GetArrayLength();

            // Config has 3 entities: Book (table), BookView (view), GetBook (custom-tool SP)
            // Only 2 should be returned (custom-tool SP excluded)
            Assert.AreEqual(2, entityCount);

            // Verify the count field in the response matches the filtered entity array length
            Assert.IsTrue(content.TryGetProperty("count", out JsonElement countElement));
            Assert.AreEqual(entityCount, countElement.GetInt32());
        }

        /// <summary>
        /// Verifies that custom-tool filtering is applied consistently regardless of the nameOnly parameter.
        /// When nameOnly=true (lightweight response), custom-tool SPs are still filtered out.
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
            Assert.IsTrue(result.IsError == false || result.IsError == null);
            JsonElement content = GetContentFromResult(result);
            Assert.IsTrue(content.TryGetProperty("entities", out JsonElement entities));

            List<string> entityNames = entities.EnumerateArray()
                .Select(e => e.GetProperty("name").GetString()!)
                .ToList();

            // Should still exclude custom-tool SP even with nameOnly=true
            Assert.IsTrue(entityNames.Contains("Book"));
            Assert.IsTrue(entityNames.Contains("BookView"));
            Assert.IsFalse(entityNames.Contains("GetBook"));
            Assert.AreEqual(2, entities.GetArrayLength());
        }

        /// <summary>
        /// Test that NoEntitiesConfigured error is returned when runtime config truly has no entities.
        /// This is different from AllEntitiesFilteredAsCustomTools where entities exist but are filtered.
        /// </summary>
        [TestMethod]
        public async Task DescribeEntities_ReturnsNoEntitiesConfigured_WhenConfigHasNoEntities()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithNoEntities();
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            DescribeEntitiesTool tool = new();

            // Act
            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            // Assert - Expect NoEntitiesConfigured (not AllEntitiesFilteredAsCustomTools)
            // because the config truly has NO entities, not filtered entities
            Assert.IsTrue(result.IsError == true);
            JsonElement content = GetContentFromResult(result);
            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            Assert.IsTrue(error.TryGetProperty("type", out JsonElement errorType));
            Assert.AreEqual("NoEntitiesConfigured", errorType.GetString());

            // Verify the error message indicates no entities configured
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
            // Arrange
            RuntimeConfig config = CreateConfigWithCustomToolAndDmlEnabled();
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            DescribeEntitiesTool tool = new();

            // Act
            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsError == false || result.IsError == null);
            JsonElement content = GetContentFromResult(result);
            Assert.IsTrue(content.TryGetProperty("entities", out JsonElement entities));

            List<string> entityNames = entities.EnumerateArray()
                .Select(e => e.GetProperty("name").GetString()!)
                .ToList();

            // GetBook has custom-tool: true AND dml-tools: true, so it should APPEAR in describe_entities
            Assert.IsTrue(entityNames.Contains("GetBook"),
                "SP with custom-tool:true + dml-tools:true should appear in describe_entities");

            // Should have exactly 1 entity
            Assert.AreEqual(1, entities.GetArrayLength());
        }

        #region Helper Methods

        /// <summary>
        /// Creates a runtime config with only custom-tool stored procedures.
        /// Used to test the AllEntitiesFilteredAsCustomTools error scenario.
        /// </summary>
        private static RuntimeConfig CreateConfigWithCustomToolSP()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["GetBook"] = new Entity(
                    Source: new("get_book", EntitySourceType.StoredProcedure, null, null),
                    GraphQL: new("GetBook", "GetBook"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null) }) },
                    Mappings: null,
                    Relationships: null,
                    Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: false)
                )
            };

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
        /// Creates a runtime config with mixed stored procedures:
        /// one regular SP (CountBooks) and one custom-tool SP (GetBook).
        /// Used to test that filtering is selective.
        /// </summary>
        private static RuntimeConfig CreateConfigWithMixedStoredProcedures()
        {
            Dictionary<string, Entity> entities = new()
            {
                // Regular SP without custom-tool config
                ["CountBooks"] = new Entity(
                    Source: new("count_books", EntitySourceType.StoredProcedure, null, null),
                    GraphQL: new("CountBooks", "CountBooks"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null) }) },
                    Mappings: null,
                    Relationships: null
                ),
                // SP with custom-tool enabled
                ["GetBook"] = new Entity(
                    Source: new("get_book", EntitySourceType.StoredProcedure, null, null),
                    GraphQL: new("GetBook", "GetBook"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null) }) },
                    Mappings: null,
                    Relationships: null,
                    Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: false)
                )
            };

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
        /// Creates a runtime config with mixed entity types:
        /// table (Book), view (BookView), and custom-tool SP (GetBook).
        /// Used to test that filtering only affects stored procedures.
        /// </summary>
        private static RuntimeConfig CreateConfigWithMixedEntityTypes()
        {
            Dictionary<string, Entity> entities = new()
            {
                // Table
                ["Book"] = new Entity(
                    Source: new("books", EntitySourceType.Table, null, null),
                    GraphQL: new("Book", "Books"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null) }) },
                    Mappings: null,
                    Relationships: null
                ),
                // View
                ["BookView"] = new Entity(
                    Source: new("book_view", EntitySourceType.View, null, null),
                    GraphQL: new("BookView", "BookViews"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null) }) },
                    Mappings: null,
                    Relationships: null
                ),
                // Custom-tool SP
                ["GetBook"] = new Entity(
                    Source: new("get_book", EntitySourceType.StoredProcedure, null, null),
                    GraphQL: new("GetBook", "GetBook"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null) }) },
                    Mappings: null,
                    Relationships: null,
                    Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: false)
                )
            };

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
        /// Creates a runtime config with an empty entities dictionary.
        /// Used to test the NoEntitiesConfigured error when no entities are configured at all.
        /// </summary>
        private static RuntimeConfig CreateConfigWithNoEntities()
        {
            // Create a config with no entities at all (empty dictionary)
            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(Enabled: true, Path: "/mcp", DmlTools: null),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(new Dictionary<string, Entity>())
            );
        }

        /// <summary>
        /// Creates a runtime config with a stored procedure that has BOTH custom-tool and dml-tools enabled.
        /// Used to test the truth table scenario: custom-tool:true + dml-tools:true → should appear in describe_entities.
        /// </summary>
        private static RuntimeConfig CreateConfigWithCustomToolAndDmlEnabled()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["GetBook"] = new Entity(
                    Source: new("get_book", EntitySourceType.StoredProcedure, null, null),
                    GraphQL: new("GetBook", "GetBook"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null) }) },
                    Mappings: null,
                    Relationships: null,
                    Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: true)
                )
            };

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
