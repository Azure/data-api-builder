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
using Azure.DataApiBuilder.Mcp.Model;
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
        /// Verifies that DML tools respect entity-level DmlToolEnabled=false.
        /// When an entity has DmlToolEnabled explicitly set to false, the tool should
        /// return a ToolDisabled error even if the runtime-level tool is enabled.
        /// </summary>
        /// <param name="toolType">The type of tool to test (ReadRecords, CreateRecord, UpdateRecord, DeleteRecord, ExecuteEntity).</param>
        /// <param name="jsonArguments">The JSON arguments for the tool.</param>
        /// <param name="isStoredProcedure">Whether the entity is a stored procedure (uses different config).</param>
        [DataTestMethod]
        [DataRow("ReadRecords", "{\"entity\": \"Book\"}", false, DisplayName = "ReadRecords respects entity-level DmlToolEnabled=false")]
        [DataRow("CreateRecord", "{\"entity\": \"Book\", \"data\": {\"id\": 1, \"title\": \"Test\"}}", false, DisplayName = "CreateRecord respects entity-level DmlToolEnabled=false")]
        [DataRow("UpdateRecord", "{\"entity\": \"Book\", \"keys\": {\"id\": 1}, \"fields\": {\"title\": \"Updated\"}}", false, DisplayName = "UpdateRecord respects entity-level DmlToolEnabled=false")]
        [DataRow("DeleteRecord", "{\"entity\": \"Book\", \"keys\": {\"id\": 1}}", false, DisplayName = "DeleteRecord respects entity-level DmlToolEnabled=false")]
        [DataRow("ExecuteEntity", "{\"entity\": \"GetBook\"}", true, DisplayName = "ExecuteEntity respects entity-level DmlToolEnabled=false")]
        [DataRow("AggregateRecords", "{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\"}", false, DisplayName = "AggregateRecords respects entity-level DmlToolEnabled=false")]
        public async Task DmlTool_RespectsEntityLevelDmlToolDisabled(string toolType, string jsonArguments, bool isStoredProcedure)
        {
            // Arrange
            RuntimeConfig config = isStoredProcedure
                ? CreateConfig(
                    entityName: "GetBook", sourceObject: "get_book",
                    sourceType: EntitySourceType.StoredProcedure,
                    mcpOptions: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: false),
                    actions: new[] { EntityActionOperation.Execute })
                : CreateConfig(
                    mcpOptions: new EntityMcpOptions(customToolEnabled: false, dmlToolsEnabled: false),
                    actions: new[] { EntityActionOperation.Read, EntityActionOperation.Create,
                                     EntityActionOperation.Update, EntityActionOperation.Delete });
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            IMcpTool tool = CreateTool(toolType);

            JsonDocument arguments = JsonDocument.Parse(jsonArguments);

            // Act
            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsError == true, "Expected error when entity has DmlToolEnabled=false");

            JsonElement content = await RunToolAsync(tool, arguments, serviceProvider);
            AssertToolDisabledError(content);
        }

        /// <summary>
        /// Verifies that DML tools work normally when entity-level DmlToolEnabled is not set to false.
        /// This test ensures the entity-level check doesn't break the normal flow when either:
        /// - DmlToolEnabled=true (explicitly enabled)
        /// - entity.Mcp is null (defaults to enabled)
        /// </summary>
        /// <param name="scenario">The test scenario description.</param>
        /// <param name="useMcpConfig">Whether to include MCP config with DmlToolEnabled=true (false means no MCP config).</param>
        [DataTestMethod]
        [DataRow("DmlToolEnabled=true", true, DisplayName = "ReadRecords works when entity has DmlToolEnabled=true")]
        [DataRow("No MCP config", false, DisplayName = "ReadRecords works when entity has no MCP config")]
        public async Task ReadRecords_WorksWhenNotDisabledAtEntityLevel(string scenario, bool useMcpConfig)
        {
            // Arrange
            RuntimeConfig config = useMcpConfig
                ? CreateConfig(mcpOptions: new EntityMcpOptions(customToolEnabled: false, dmlToolsEnabled: true))
                : CreateConfig();
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
                JsonElement content = await RunToolAsync(tool, arguments, serviceProvider);

                if (content.TryGetProperty("error", out JsonElement error) &&
                    error.TryGetProperty("type", out JsonElement errorType))
                {
                    string errorTypeValue = errorType.GetString();
                    Assert.AreNotEqual("ToolDisabled", errorTypeValue,
                        $"Should not get ToolDisabled error for scenario: {scenario}");
                }
            }
        }

        /// <summary>
        /// Verifies the precedence of runtime-level vs entity-level configuration.
        /// When runtime-level tool is disabled, entity-level DmlToolEnabled=true should NOT override it.
        /// This validates that runtime-level acts as a global gate that takes precedence.
        /// </summary>
        [TestMethod]
        public async Task ReadRecords_RuntimeDisabledTakesPrecedenceOverEntityEnabled()
        {
            // Arrange - Runtime has readRecords=false, but entity has DmlToolEnabled=true
            RuntimeConfig config = CreateConfig(
                mcpOptions: new EntityMcpOptions(customToolEnabled: false, dmlToolsEnabled: true),
                readRecordsEnabled: false);
            IServiceProvider serviceProvider = CreateServiceProvider(config);
            ReadRecordsTool tool = new();

            JsonDocument arguments = JsonDocument.Parse("{\"entity\": \"Book\"}");

            // Act
            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);

            // Assert
            Assert.IsTrue(result.IsError == true, "Expected error when runtime-level tool is disabled");

            JsonElement content = await RunToolAsync(tool, arguments, serviceProvider);
            AssertToolDisabledError(content);

            // Verify the error is due to runtime-level, not entity-level
            // (The error message should NOT mention entity-specific disabling)
            if (content.TryGetProperty("error", out JsonElement error) &&
                error.TryGetProperty("message", out JsonElement errorMessage))
            {
                string message = errorMessage.GetString() ?? string.Empty;
                Assert.IsFalse(message.Contains("entity"),
                    "Error should be from runtime-level check, not entity-level check");
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
            RuntimeConfig config = CreateConfig(
                entityName: "GetBook", sourceObject: "get_book",
                sourceType: EntitySourceType.StoredProcedure,
                mcpOptions: new EntityMcpOptions(customToolEnabled: false, dmlToolsEnabled: true),
                actions: new[] { EntityActionOperation.Execute });
            IServiceProvider serviceProvider = CreateServiceProvider(config);

            // Create the DynamicCustomTool with the entity that has CustomToolEnabled initially true
            // (simulating tool created at startup, then config changed)
            Entity initialEntity = new(
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

            JsonElement content = await RunToolAsync(tool, arguments, serviceProvider);
            AssertToolDisabledError(content, "Custom tool is disabled for entity 'GetBook'");
        }

        #region Helper Methods

        /// <summary>
        /// Helper method to execute an MCP tool and return the parsed JsonElement from the result.
        /// </summary>
        /// <param name="tool">The MCP tool to execute.</param>
        /// <param name="arguments">The JSON arguments for the tool.</param>
        /// <param name="serviceProvider">The service provider with dependencies.</param>
        /// <returns>The parsed JsonElement from the tool's response.</returns>
        private static async Task<JsonElement> RunToolAsync(IMcpTool tool, JsonDocument arguments, IServiceProvider serviceProvider)
        {
            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);
            TextContentBlock firstContent = (TextContentBlock)result.Content[0];
            return JsonDocument.Parse(firstContent.Text).RootElement;
        }

        /// <summary>
        /// Helper method to assert that a JsonElement contains a ToolDisabled error.
        /// </summary>
        /// <param name="content">The JSON content to check for error.</param>
        /// <param name="expectedMessageFragment">Optional message fragment that should be present in the error message.</param>
        private static void AssertToolDisabledError(JsonElement content, string expectedMessageFragment = null)
        {
            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            Assert.IsTrue(error.TryGetProperty("type", out JsonElement errorType));
            Assert.AreEqual("ToolDisabled", errorType.GetString());

            if (expectedMessageFragment != null)
            {
                Assert.IsTrue(error.TryGetProperty("message", out JsonElement errorMessage));
                string message = errorMessage.GetString() ?? string.Empty;
                Assert.IsTrue(message.Contains(expectedMessageFragment),
                    $"Expected error message to contain '{expectedMessageFragment}', but got: {message}");
            }
        }

        /// <summary>
        /// Helper method to create an MCP tool instance based on the tool type.
        /// </summary>
        /// <param name="toolType">The type of tool to create (ReadRecords, CreateRecord, UpdateRecord, DeleteRecord, ExecuteEntity).</param>
        /// <returns>An instance of the requested tool.</returns>
        private static IMcpTool CreateTool(string toolType)
        {
            return toolType switch
            {
                "ReadRecords" => new ReadRecordsTool(),
                "CreateRecord" => new CreateRecordTool(),
                "UpdateRecord" => new UpdateRecordTool(),
                "DeleteRecord" => new DeleteRecordTool(),
                "ExecuteEntity" => new ExecuteEntityTool(),
                "AggregateRecords" => new AggregateRecordsTool(),
                _ => throw new ArgumentException($"Unknown tool type: {toolType}", nameof(toolType))
            };
        }

        /// <summary>
        /// Unified config factory. Creates a RuntimeConfig with a single entity.
        /// Callers specify only the parameters that differ from their test scenario.
        /// </summary>
        /// <param name="entityName">Entity key name (default: "Book").</param>
        /// <param name="sourceObject">Database object (default: "books").</param>
        /// <param name="sourceType">Table or StoredProcedure (default: Table).</param>
        /// <param name="mcpOptions">Entity-level MCP options, or null for no MCP config.</param>
        /// <param name="actions">Entity permissions. Defaults to Read-only.</param>
        /// <param name="readRecordsEnabled">Runtime-level readRecords flag (default: true).</param>
        private static RuntimeConfig CreateConfig(
            string entityName = "Book",
            string sourceObject = "books",
            EntitySourceType sourceType = EntitySourceType.Table,
            EntityMcpOptions mcpOptions = null,
            EntityActionOperation[] actions = null,
            bool readRecordsEnabled = true)
        {
            actions ??= new[] { EntityActionOperation.Read };

            Dictionary<string, Entity> entities = new()
            {
                [entityName] = new Entity(
                    Source: new(sourceObject, sourceType, null, null),
                    GraphQL: new(entityName, entityName == "Book" ? "Books" : entityName),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous",
                        Actions: Array.ConvertAll(actions, a => new EntityAction(Action: a, Fields: null, Policy: null))) },
                    Mappings: null,
                    Relationships: null,
                    Mcp: mcpOptions
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
                            readRecords: readRecordsEnabled,
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
