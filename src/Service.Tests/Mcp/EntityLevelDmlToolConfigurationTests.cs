// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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
    /// Tests for entity-level DML tool configuration (GitHub issue #3017).
    /// Ensures that DML tools respect the entity-level Mcp.DmlToolEnabled property
    /// in addition to the runtime-level configuration.
    /// 
    /// Coverage:
    /// - Entity with DmlToolEnabled=false (tool disabled at entity level)
    /// - Entity with DmlToolEnabled=true (tool enabled at entity level)
    /// - Entity with no MCP configuration (defaults to enabled)
    /// - Custom tool with CustomToolEnabled=false (runtime validation)
    /// </summary>
    [TestClass]
    public class EntityLevelDmlToolConfigurationTests
    {
        /// <summary>
        /// Verifies that ReadRecordsTool respects entity-level DmlToolEnabled=false.
        /// When an entity has DmlToolEnabled explicitly set to false, the tool should
        /// return a ToolDisabled error even if the runtime-level ReadRecords is enabled.
        /// </summary>
        [TestMethod]
        public async Task ReadRecords_RespectsEntityLevelDmlToolDisabled()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithDmlToolDisabledEntity();
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            ReadRecordsTool tool = new();

            JsonDocument arguments = JsonDocument.Parse("{\"entity\": \"Book\"}");

            // Act
            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsError == true, "Expected error when entity has DmlToolEnabled=false");

            TextContentBlock firstContent = (TextContentBlock)result.Content[0];
            JsonElement content = JsonDocument.Parse(firstContent.Text).RootElement;

            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            Assert.IsTrue(error.TryGetProperty("type", out JsonElement errorType));
            Assert.AreEqual("ToolDisabled", errorType.GetString());

            Assert.IsTrue(error.TryGetProperty("message", out JsonElement errorMessage));
            string message = errorMessage.GetString() ?? string.Empty;
            Assert.IsTrue(message.Contains("DML tools are disabled for entity 'Book'"));
        }

        /// <summary>
        /// Verifies that CreateRecordTool respects entity-level DmlToolEnabled=false.
        /// </summary>
        [TestMethod]
        public async Task CreateRecord_RespectsEntityLevelDmlToolDisabled()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithDmlToolDisabledEntity();
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            CreateRecordTool tool = new();

            JsonDocument arguments = JsonDocument.Parse("{\"entity\": \"Book\", \"data\": {\"id\": 1, \"title\": \"Test\"}}");

            // Act
            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsError == true, "Expected error when entity has DmlToolEnabled=false");

            TextContentBlock firstContent = (TextContentBlock)result.Content[0];
            JsonElement content = JsonDocument.Parse(firstContent.Text).RootElement;

            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            Assert.IsTrue(error.TryGetProperty("type", out JsonElement errorType));
            Assert.AreEqual("ToolDisabled", errorType.GetString());
        }

        /// <summary>
        /// Verifies that UpdateRecordTool respects entity-level DmlToolEnabled=false.
        /// </summary>
        [TestMethod]
        public async Task UpdateRecord_RespectsEntityLevelDmlToolDisabled()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithDmlToolDisabledEntity();
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            UpdateRecordTool tool = new();

            JsonDocument arguments = JsonDocument.Parse("{\"entity\": \"Book\", \"keys\": {\"id\": 1}, \"fields\": {\"title\": \"Updated\"}}");

            // Act
            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsError == true, "Expected error when entity has DmlToolEnabled=false");

            TextContentBlock firstContent = (TextContentBlock)result.Content[0];
            JsonElement content = JsonDocument.Parse(firstContent.Text).RootElement;

            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            Assert.IsTrue(error.TryGetProperty("type", out JsonElement errorType));
            Assert.AreEqual("ToolDisabled", errorType.GetString());
        }

        /// <summary>
        /// Verifies that DeleteRecordTool respects entity-level DmlToolEnabled=false.
        /// </summary>
        [TestMethod]
        public async Task DeleteRecord_RespectsEntityLevelDmlToolDisabled()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithDmlToolDisabledEntity();
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            DeleteRecordTool tool = new();

            JsonDocument arguments = JsonDocument.Parse("{\"entity\": \"Book\", \"keys\": {\"id\": 1}}");

            // Act
            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsError == true, "Expected error when entity has DmlToolEnabled=false");

            TextContentBlock firstContent = (TextContentBlock)result.Content[0];
            JsonElement content = JsonDocument.Parse(firstContent.Text).RootElement;

            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            Assert.IsTrue(error.TryGetProperty("type", out JsonElement errorType));
            Assert.AreEqual("ToolDisabled", errorType.GetString());
        }

        /// <summary>
        /// Verifies that ExecuteEntityTool respects entity-level DmlToolEnabled=false.
        /// </summary>
        [TestMethod]
        public async Task ExecuteEntity_RespectsEntityLevelDmlToolDisabled()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithDmlToolDisabledStoredProcedure();
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            ExecuteEntityTool tool = new();

            JsonDocument arguments = JsonDocument.Parse("{\"entity\": \"GetBook\"}");

            // Act
            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsError == true, "Expected error when entity has DmlToolEnabled=false");

            TextContentBlock firstContent = (TextContentBlock)result.Content[0];
            JsonElement content = JsonDocument.Parse(firstContent.Text).RootElement;

            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            Assert.IsTrue(error.TryGetProperty("type", out JsonElement errorType));
            Assert.AreEqual("ToolDisabled", errorType.GetString());
        }

        /// <summary>
        /// Verifies that DML tools work normally when entity has DmlToolEnabled=true (default).
        /// This test ensures the entity-level check doesn't break the normal flow.
        /// </summary>
        [TestMethod]
        public async Task ReadRecords_WorksWhenEntityLevelDmlToolEnabled()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithDmlToolEnabledEntity();
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            ReadRecordsTool tool = new();

            JsonDocument arguments = JsonDocument.Parse("{\"entity\": \"Book\"}");

            // Act
            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);

            // Assert
            // Should not be a ToolDisabled error - might be other errors (e.g., database connection)
            // but that's OK for this test. We just want to ensure it passes the entity-level check.
            if (result.IsError == true)
            {
                TextContentBlock firstContent = (TextContentBlock)result.Content[0];
                JsonElement content = JsonDocument.Parse(firstContent.Text).RootElement;

                if (content.TryGetProperty("error", out JsonElement error) &&
                    error.TryGetProperty("type", out JsonElement errorType))
                {
                    string errorTypeValue = errorType.GetString();
                    Assert.AreNotEqual("ToolDisabled", errorTypeValue,
                        "Should not get ToolDisabled error when DmlToolEnabled=true");
                }
            }
        }

        /// <summary>
        /// Verifies that entity-level check is skipped when entity has no MCP configuration.
        /// When entity.Mcp is null, DmlToolEnabled defaults to true.
        /// </summary>
        [TestMethod]
        public async Task ReadRecords_WorksWhenEntityHasNoMcpConfig()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithEntityWithoutMcpConfig();
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            ReadRecordsTool tool = new();

            JsonDocument arguments = JsonDocument.Parse("{\"entity\": \"Book\"}");

            // Act
            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);

            // Assert
            // Should not be a ToolDisabled error
            if (result.IsError == true)
            {
                TextContentBlock firstContent = (TextContentBlock)result.Content[0];
                JsonElement content = JsonDocument.Parse(firstContent.Text).RootElement;

                if (content.TryGetProperty("error", out JsonElement error) &&
                    error.TryGetProperty("type", out JsonElement errorType))
                {
                    string errorTypeValue = errorType.GetString();
                    Assert.AreNotEqual("ToolDisabled", errorTypeValue,
                        "Should not get ToolDisabled error when entity has no MCP config");
                }
            }
        }

        /// <summary>
        /// Verifies that DynamicCustomTool respects entity-level CustomToolEnabled configuration.
        /// If CustomToolEnabled becomes false (e.g., after config hot-reload), ExecuteAsync should
        /// return a ToolDisabled error. This ensures runtime validation even though tool instances
        /// are created at startup.
        /// </summary>
        [TestMethod]
        public async Task DynamicCustomTool_RespectsCustomToolDisabled()
        {
            // Arrange - Create a stored procedure entity with CustomToolEnabled=false
            RuntimeConfig config = CreateConfigWithCustomToolDisabled();
            IServiceProvider serviceProvider = CreateServiceProvider(config);

            // Create the DynamicCustomTool with the entity that has CustomToolEnabled initially true
            // (simulating tool created at startup, then config changed)
            Entity initialEntity = new Entity(
                Source: new("get_book", EntitySourceType.StoredProcedure, null, null),
                GraphQL: new("GetBook", "GetBook"),
                Fields: null,
                Rest: new(Enabled: true),
                Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] {
                    new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null)
                }) },
                Mappings: null,
                Relationships: null,
                Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: true)
            );

            Azure.DataApiBuilder.Mcp.Core.DynamicCustomTool tool = new("GetBook", initialEntity);

            JsonDocument arguments = JsonDocument.Parse("{}");

            // Act - Execute with config that has CustomToolEnabled=false
            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsError == true, "Expected error when CustomToolEnabled=false in runtime config");

            TextContentBlock firstContent = (TextContentBlock)result.Content[0];
            JsonElement content = JsonDocument.Parse(firstContent.Text).RootElement;

            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            Assert.IsTrue(error.TryGetProperty("type", out JsonElement errorType));
            Assert.AreEqual("ToolDisabled", errorType.GetString());

            Assert.IsTrue(error.TryGetProperty("message", out JsonElement errorMessage));
            string message = errorMessage.GetString() ?? string.Empty;
            Assert.IsTrue(message.Contains("Custom tool is disabled for entity 'GetBook'"));
        }

        #region Helper Methods

        /// <summary>
        /// Creates a runtime config with a table entity that has DmlToolEnabled=false.
        /// </summary>
        private static RuntimeConfig CreateConfigWithDmlToolDisabledEntity()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["Book"] = new Entity(
                    Source: new("books", EntitySourceType.Table, null, null),
                    GraphQL: new("Book", "Books"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] {
                        new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null),
                        new EntityAction(Action: EntityActionOperation.Create, Fields: null, Policy: null),
                        new EntityAction(Action: EntityActionOperation.Update, Fields: null, Policy: null),
                        new EntityAction(Action: EntityActionOperation.Delete, Fields: null, Policy: null)
                    }) },
                    Mappings: null,
                    Relationships: null,
                    Mcp: new EntityMcpOptions(customToolEnabled: false, dmlToolsEnabled: false)
                )
            };

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(
                        Enabled: true,
                        Path: "/mcp",
                        DmlTools: new(
                            describeEntities: true,
                            readRecords: true,
                            createRecord: true,
                            updateRecord: true,
                            deleteRecord: true,
                            executeEntity: true
                        )
                    ),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(entities)
            );
        }

        /// <summary>
        /// Creates a runtime config with a stored procedure that has DmlToolEnabled=false.
        /// </summary>
        private static RuntimeConfig CreateConfigWithDmlToolDisabledStoredProcedure()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["GetBook"] = new Entity(
                    Source: new("get_book", EntitySourceType.StoredProcedure, null, null),
                    GraphQL: new("GetBook", "GetBook"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] {
                        new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null)
                    }) },
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
                    Mcp: new(
                        Enabled: true,
                        Path: "/mcp",
                        DmlTools: new(
                            describeEntities: true,
                            readRecords: true,
                            createRecord: true,
                            updateRecord: true,
                            deleteRecord: true,
                            executeEntity: true
                        )
                    ),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(entities)
            );
        }

        /// <summary>
        /// Creates a runtime config with a table entity that has DmlToolEnabled=true.
        /// </summary>
        private static RuntimeConfig CreateConfigWithDmlToolEnabledEntity()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["Book"] = new Entity(
                    Source: new("books", EntitySourceType.Table, null, null),
                    GraphQL: new("Book", "Books"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] {
                        new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null)
                    }) },
                    Mappings: null,
                    Relationships: null,
                    Mcp: new EntityMcpOptions(customToolEnabled: false, dmlToolsEnabled: true)
                )
            };

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(
                        Enabled: true,
                        Path: "/mcp",
                        DmlTools: new(
                            describeEntities: true,
                            readRecords: true,
                            createRecord: true,
                            updateRecord: true,
                            deleteRecord: true,
                            executeEntity: true
                        )
                    ),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(entities)
            );
        }

        /// <summary>
        /// Creates a runtime config with a table entity that has no MCP configuration.
        /// </summary>
        private static RuntimeConfig CreateConfigWithEntityWithoutMcpConfig()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["Book"] = new Entity(
                    Source: new("books", EntitySourceType.Table, null, null),
                    GraphQL: new("Book", "Books"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] {
                        new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null)
                    }) },
                    Mappings: null,
                    Relationships: null,
                    Mcp: null
                )
            };

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(
                        Enabled: true,
                        Path: "/mcp",
                        DmlTools: new(
                            describeEntities: true,
                            readRecords: true,
                            createRecord: true,
                            updateRecord: true,
                            deleteRecord: true,
                            executeEntity: true
                        )
                    ),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(entities)
            );
        }

        /// <summary>
        /// Creates a runtime config with a stored procedure that has CustomToolEnabled=false.
        /// Used to test DynamicCustomTool runtime validation.
        /// </summary>
        private static RuntimeConfig CreateConfigWithCustomToolDisabled()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["GetBook"] = new Entity(
                    Source: new("get_book", EntitySourceType.StoredProcedure, null, null),
                    GraphQL: new("GetBook", "GetBook"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] {
                        new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null)
                    }) },
                    Mappings: null,
                    Relationships: null,
                    Mcp: new EntityMcpOptions(customToolEnabled: false, dmlToolsEnabled: true)
                )
            };

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(
                        Enabled: true,
                        Path: "/mcp",
                        DmlTools: new(
                            describeEntities: true,
                            readRecords: true,
                            createRecord: true,
                            updateRecord: true,
                            deleteRecord: true,
                            executeEntity: true
                        )
                    ),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(entities)
            );
        }

        /// <summary>
        /// Creates a service provider with mocked dependencies for testing MCP tools.
        /// </summary>
        private static IServiceProvider CreateServiceProvider(RuntimeConfig config)
        {
            ServiceCollection services = new();

            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            services.AddSingleton(configProvider);

            Mock<IAuthorizationResolver> mockAuthResolver = new();
            mockAuthResolver.Setup(x => x.IsValidRoleContext(It.IsAny<HttpContext>())).Returns(true);
            services.AddSingleton(mockAuthResolver.Object);

            Mock<HttpContext> mockHttpContext = new();
            Mock<HttpRequest> mockRequest = new();
            mockRequest.Setup(x => x.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns("anonymous");
            mockHttpContext.Setup(x => x.Request).Returns(mockRequest.Object);

            Mock<IHttpContextAccessor> mockHttpContextAccessor = new();
            mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext.Object);
            services.AddSingleton(mockHttpContextAccessor.Object);

            services.AddLogging();

            return services.BuildServiceProvider();
        }

        #endregion
    }
}
