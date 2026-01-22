// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
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
    /// Tests for DescribeEntitiesTool filtering logic, ensuring custom-tool stored procedures
    /// are excluded from describe_entities results while regular entities remain visible.
    /// </summary>
    [TestClass]
    public class DescribeEntitiesFilteringTests
    {
        /// <summary>
        /// Test that custom-tool stored procedures are excluded from describe_entities results.
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
            // When all entities are custom-tool SPs, they're all filtered out, so we get an error
            Assert.IsTrue(result.IsError == true);
            JsonElement content = GetContentFromResult(result);
            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            Assert.IsTrue(error.TryGetProperty("type", out JsonElement errorType));
            Assert.AreEqual("NoEntitiesConfigured", errorType.GetString());
        }

        /// <summary>
        /// Test that regular stored procedures (without custom-tool) still appear in describe_entities.
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
        /// Test that tables and views are unaffected by custom-tool filtering.
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
        /// Test that entity count reflects filtered list (excludes custom-tool SPs).
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
        }

        /// <summary>
        /// Test that nameOnly parameter works correctly with custom-tool filtering.
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

        #region Helper Methods

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
                    Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: null)
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
                    Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: null)
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
                    Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: null)
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

        private static IServiceProvider CreateServiceProvider(RuntimeConfig config)
        {
            ServiceCollection services = new();

            // Create RuntimeConfigProvider with a test loader
            MockFileSystem fileSystem = new();
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            TestRuntimeConfigProvider configProvider = new(config, loader);
            services.AddSingleton<RuntimeConfigProvider>(configProvider);

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

        private static JsonElement GetContentFromResult(CallToolResult result)
        {
            Assert.IsNotNull(result.Content);
            Assert.IsTrue(result.Content.Count > 0);

            ModelContextProtocol.Protocol.TextContentBlock firstContent = (ModelContextProtocol.Protocol.TextContentBlock)result.Content[0];
            Assert.IsNotNull(firstContent.Text);

            return JsonDocument.Parse(firstContent.Text).RootElement;
        }

        #endregion
    }

    /// <summary>
    /// Test implementation of RuntimeConfigProvider that returns a fixed config.
    /// </summary>
    internal class TestRuntimeConfigProvider : RuntimeConfigProvider
    {
        private readonly RuntimeConfig _config;

        public TestRuntimeConfigProvider(RuntimeConfig config, FileSystemRuntimeConfigLoader loader)
            : base(loader)
        {
            _config = config;
        }

        public override RuntimeConfig GetConfig() => _config;
    }
}
