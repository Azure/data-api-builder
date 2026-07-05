// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Tests for McpToolRegistry to ensure tool name uniqueness validation.
    /// </summary>
    [TestClass]
    public class McpToolRegistryTests
    {
        /// <summary>
        /// Test that registering multiple tools with unique names succeeds.
        /// </summary>
        [TestMethod]
        public void RegisterTool_WithMultipleUniqueNames_Succeeds()
        {
            // Arrange
            McpToolRegistry registry = new();
            IMcpTool tool1 = new MockMcpTool("tool_one", ToolType.BuiltIn);
            IMcpTool tool2 = new MockMcpTool("tool_two", ToolType.Custom);
            IMcpTool tool3 = new MockMcpTool("tool_three", ToolType.BuiltIn);

            // Act & Assert - should not throw
            registry.RegisterTool(tool1);
            registry.RegisterTool(tool2);
            registry.RegisterTool(tool3);

            // Verify all tools were registered
            Assert.IsTrue(registry.TryGetTool("tool_one", out _));
            Assert.IsTrue(registry.TryGetTool("tool_two", out _));
            Assert.IsTrue(registry.TryGetTool("tool_three", out _));
        }

        /// <summary>
        /// Test that registering duplicate tools of the same type throws an exception.
        /// Validates that both built-in and custom tools enforce name uniqueness within their own type.
        /// </summary>
        [DataTestMethod]
        [DataRow(ToolType.BuiltIn, "duplicate_tool", "built-in", DisplayName = "Duplicate Built-In Tools")]
        [DataRow(ToolType.Custom, "my_custom_tool", "custom", DisplayName = "Duplicate Custom Tools")]
        public void RegisterTool_WithDuplicateSameType_ThrowsException(
            ToolType toolType,
            string toolName,
            string expectedToolTypeText)
        {
            // Arrange
            McpToolRegistry registry = new();
            IMcpTool tool1 = new MockMcpTool(toolName, toolType);
            IMcpTool tool2 = new MockMcpTool(toolName, toolType);

            // Act - Register first tool
            registry.RegisterTool(tool1);

            // Assert - Second registration should throw
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => registry.RegisterTool(tool2)
            );

            // Verify exception details
            Assert.IsTrue(exception.Message.Contains($"Duplicate MCP tool name '{toolName}' detected"));
            Assert.IsTrue(exception.Message.Contains($"{expectedToolTypeText} tool with this name is already registered"));
            Assert.IsTrue(exception.Message.Contains($"Cannot register {expectedToolTypeText} tool with the same name"));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, exception.SubStatusCode);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        }

        /// <summary>
        /// Test that registering tools with conflicting names across different types throws an exception.
        /// Validates that tool names must be unique across all tool types (built-in and custom).
        /// </summary>
        [DataTestMethod]
        [DataRow("create_record", ToolType.BuiltIn, ToolType.Custom, "built-in", "custom", DisplayName = "Built-In then Custom conflict")]
        [DataRow("read_records", ToolType.BuiltIn, ToolType.Custom, "built-in", "custom", DisplayName = "Built-In then Custom conflict (read_records)")]
        [DataRow("my_stored_proc", ToolType.Custom, ToolType.BuiltIn, "custom", "built-in", DisplayName = "Custom then Built-In conflict")]
        public void RegisterTool_WithCrossTypeConflict_ThrowsException(
            string toolName,
            ToolType firstToolType,
            ToolType secondToolType,
            string expectedExistingType,
            string expectedNewType)
        {
            // Arrange
            McpToolRegistry registry = new();
            IMcpTool existingTool = new MockMcpTool(toolName, firstToolType);
            IMcpTool conflictingTool = new MockMcpTool(toolName, secondToolType);

            // Act - Register first tool
            registry.RegisterTool(existingTool);

            // Assert - Second tool registration should throw
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => registry.RegisterTool(conflictingTool)
            );

            // Verify exception details
            Assert.IsTrue(exception.Message.Contains($"Duplicate MCP tool name '{toolName}' detected"));
            Assert.IsTrue(exception.Message.Contains($"{expectedExistingType} tool with this name is already registered"));
            Assert.IsTrue(exception.Message.Contains($"Cannot register {expectedNewType} tool with the same name"));
            Assert.IsTrue(exception.Message.Contains("Tool names must be unique across all tool types"));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, exception.SubStatusCode);
            Assert.AreEqual(HttpStatusCode.ServiceUnavailable, exception.StatusCode);
        }

        /// <summary>
        /// Test that tool name comparison is case-sensitive.
        /// Tools with different casing should not be allowed.
        /// </summary>
        [TestMethod]
        public void RegisterTool_WithDifferentCasing_ThrowsException()
        {
            // Arrange
            McpToolRegistry registry = new();
            IMcpTool tool1 = new MockMcpTool("my_tool", ToolType.BuiltIn);
            IMcpTool tool2 = new MockMcpTool("My_Tool", ToolType.Custom);

            // Act - Register first tool
            registry.RegisterTool(tool1);

            // Assert - Case-insensitive duplicate should throw
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => registry.RegisterTool(tool2)
            );

            Assert.IsTrue(exception.Message.Contains("Duplicate MCP tool name"));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, exception.SubStatusCode);
        }

        /// <summary>
        /// Test that registering the same tool instance twice is silently ignored (idempotent).
        /// This supports stdio mode where both McpToolRegistryInitializer and McpStdioHelper may register the same tools.
        /// </summary>
        [TestMethod]
        public void RegisterTool_SameInstanceTwice_IsIdempotent()
        {
            // Arrange
            McpToolRegistry registry = new();
            IMcpTool tool = new MockMcpTool("my_tool", ToolType.BuiltIn);

            // Act - Register the same instance twice
            registry.RegisterTool(tool);
            registry.RegisterTool(tool);

            // Assert - Tool should be registered only once
            Assert.IsTrue(registry.TryGetTool("my_tool", out _));
        }

        /// <summary>
        /// Test that registering a different instance with the same name throws an exception,
        /// even though a same-instance re-registration would be allowed.
        /// </summary>
        [TestMethod]
        public void RegisterTool_DifferentInstanceSameName_ThrowsException()
        {
            // Arrange
            McpToolRegistry registry = new();
            IMcpTool tool1 = new MockMcpTool("my_tool", ToolType.BuiltIn);
            IMcpTool tool2 = new MockMcpTool("my_tool", ToolType.BuiltIn);

            // Act - Register first instance
            registry.RegisterTool(tool1);

            // Assert - Different instance with same name should throw
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => registry.RegisterTool(tool2)
            );

            Assert.IsTrue(exception.Message.Contains("Duplicate MCP tool name 'my_tool' detected"));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, exception.SubStatusCode);
        }

        /// <summary>
        /// Test that TryGetTool returns false for non-existent tool.
        /// </summary>
        [TestMethod]
        public void TryGetTool_WithNonExistentName_ReturnsFalse()
        {
            // Arrange
            McpToolRegistry registry = new();
            registry.RegisterTool(new MockMcpTool("existing_tool", ToolType.BuiltIn));

            // Act
            bool found = registry.TryGetTool("non_existent_tool", out IMcpTool? tool);

            // Assert
            Assert.IsFalse(found);
            Assert.IsNull(tool);
        }

        /// <summary>
        /// Test edge case: empty tool name should throw exception.
        /// </summary>
        [TestMethod]
        public void RegisterTool_WithEmptyToolName_ThrowsException()
        {
            // Arrange
            McpToolRegistry registry = new();
            IMcpTool tool = new MockMcpTool("", ToolType.BuiltIn);

            // Assert - Empty tool names should be rejected
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => registry.RegisterTool(tool)
            );

            Assert.IsTrue(exception.Message.Contains("cannot be null, empty, or whitespace"));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, exception.SubStatusCode);
        }

        /// <summary>
        /// Test realistic scenario with actual built-in tool names.
        /// </summary>
        [TestMethod]
        public void RegisterTool_WithRealisticBuiltInToolNames_DetectsDuplicates()
        {
            // Arrange
            McpToolRegistry registry = new();

            // Simulate registering built-in tools
            registry.RegisterTool(new MockMcpTool("create_record", ToolType.BuiltIn));
            registry.RegisterTool(new MockMcpTool("read_records", ToolType.BuiltIn));
            registry.RegisterTool(new MockMcpTool("update_record", ToolType.BuiltIn));
            registry.RegisterTool(new MockMcpTool("delete_record", ToolType.BuiltIn));
            registry.RegisterTool(new MockMcpTool("describe_entities", ToolType.BuiltIn));

            // Try to register a custom tool with a conflicting name
            IMcpTool customTool = new MockMcpTool("read_records", ToolType.Custom);

            // Assert - Should throw
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => registry.RegisterTool(customTool)
            );

            Assert.IsTrue(exception.Message.Contains("read_records"));
            Assert.IsTrue(exception.Message.Contains("built-in tool"));
        }

        /// <summary>
        /// Test that registering a tool with leading/trailing whitespace in the name is treated as a duplicate of the trimmed name.
        /// Note: during tool registration, the registry should trim whitespace and detect duplicates accordingly.
        /// </summary>
        [TestMethod]
        public void RegisterTool_WithLeadingTrailingWhitespace_DetectsDuplicate()
        {
            // Arrange
            McpToolRegistry registry = new();
            IMcpTool tool1 = new MockMcpTool("my_tool", ToolType.BuiltIn);
            IMcpTool tool2 = new MockMcpTool(" my_tool ", ToolType.Custom);

            // Act
            registry.RegisterTool(tool1);

            // Assert - trimmed name should collide
            Assert.ThrowsException<DataApiBuilderException>(
                () => registry.RegisterTool(tool2)
            );
        }

        /// <summary>
        /// Parameterized test verifying GetEnabledTools returns only enabled tools.
        /// </summary>
        [DataTestMethod]
        [DataRow(1, 1, DisplayName = "Mixed: 1 enabled, 1 disabled → returns 1")]
        [DataRow(3, 0, DisplayName = "All enabled → returns all")]
        [DataRow(0, 2, DisplayName = "All disabled → returns 0")]
        public void GetEnabledTools_ReturnsCorrectCount(int enabledCount, int disabledCount)
        {
            // Arrange
            McpToolRegistry registry = new();
            for (int i = 0; i < enabledCount; i++)
            {
                registry.RegisterTool(new MockMcpTool($"enabled_{i}", ToolType.BuiltIn, isEnabledFunc: _ => true));
            }

            for (int i = 0; i < disabledCount; i++)
            {
                registry.RegisterTool(new MockMcpTool($"disabled_{i}", ToolType.BuiltIn, isEnabledFunc: _ => false));
            }

            RuntimeConfig config = CreateRuntimeConfig();

            // Act
            List<Tool> result = registry.GetEnabledTools(config).ToList();

            // Assert
            Assert.AreEqual(enabledCount, result.Count);
        }

        /// <summary>
        /// Test that GetEnabledTools passes the RuntimeConfig to IsEnabled so tools
        /// can check DmlToolsConfig flags.
        /// </summary>
        [TestMethod]
        public void GetEnabledTools_PassesConfigToIsEnabled()
        {
            // Arrange
            McpToolRegistry registry = new();

            // This tool checks config.McpDmlTools?.CreateRecord
            IMcpTool configAwareTool = new MockMcpTool(
                "create_record", ToolType.BuiltIn,
                isEnabledFunc: config => config.McpDmlTools?.CreateRecord == true);

            registry.RegisterTool(configAwareTool);

            // Config with create-record disabled
            DmlToolsConfig disabledConfig = new(createRecord: false);
            RuntimeConfig configDisabled = CreateRuntimeConfig(disabledConfig);

            // Config with create-record enabled
            DmlToolsConfig enabledConfig = new(createRecord: true);
            RuntimeConfig configEnabled = CreateRuntimeConfig(enabledConfig);

            // Act & Assert - disabled
            List<Tool> disabledTools = registry.GetEnabledTools(configDisabled).ToList();
            Assert.AreEqual(0, disabledTools.Count);

            // Act & Assert - enabled
            List<Tool> enabledTools = registry.GetEnabledTools(configEnabled).ToList();
            Assert.AreEqual(1, enabledTools.Count);
            Assert.AreEqual("create_record", enabledTools[0].Name);
        }

        /// <summary>
        /// Test that GetEnabledTools correctly filters a mix of built-in and custom tools.
        /// Custom tools (always enabled) should remain while disabled built-in tools are excluded.
        /// </summary>
        [TestMethod]
        public void GetEnabledTools_MixedBuiltInAndCustomTools()
        {
            // Arrange
            McpToolRegistry registry = new();
            registry.RegisterTool(new MockMcpTool("describe_entities", ToolType.BuiltIn, isEnabledFunc: _ => true));
            registry.RegisterTool(new MockMcpTool("create_record", ToolType.BuiltIn, isEnabledFunc: _ => false));
            registry.RegisterTool(new MockMcpTool("delete_record", ToolType.BuiltIn, isEnabledFunc: _ => false));
            registry.RegisterTool(new MockMcpTool("read_records", ToolType.BuiltIn, isEnabledFunc: _ => true));
            registry.RegisterTool(new MockMcpTool("get_books", ToolType.Custom, isEnabledFunc: _ => true));

            RuntimeConfig config = CreateRuntimeConfig();

            // Act
            List<Tool> enabledTools = registry.GetEnabledTools(config).ToList();

            // Assert - create_record and delete_record should be filtered out
            Assert.AreEqual(3, enabledTools.Count);
            Assert.IsTrue(enabledTools.Any(t => t.Name == "describe_entities"));
            Assert.IsTrue(enabledTools.Any(t => t.Name == "read_records"));
            Assert.IsTrue(enabledTools.Any(t => t.Name == "get_books"));
            Assert.IsFalse(enabledTools.Any(t => t.Name == "create_record"));
            Assert.IsFalse(enabledTools.Any(t => t.Name == "delete_record"));
        }

        /// <summary>
        /// Validates IsEnabled for each real built-in tool matches the DmlToolsConfig flag value.
        /// </summary>
        [DataTestMethod]
        [DataRow(true, DisplayName = "All DML tools enabled")]
        [DataRow(false, DisplayName = "All DML tools disabled")]
        public void BuiltInTools_IsEnabled_MatchesDmlToolsConfigFlag(bool allEnabled)
        {
            // Arrange
            DmlToolsConfig dmlConfig = DmlToolsConfig.FromBoolean(allEnabled);
            RuntimeConfig config = CreateRuntimeConfig(dmlConfig);

            IMcpTool[] builtInTools = new IMcpTool[]
            {
                new Azure.DataApiBuilder.Mcp.BuiltInTools.CreateRecordTool(),
                new Azure.DataApiBuilder.Mcp.BuiltInTools.DeleteRecordTool(),
                new Azure.DataApiBuilder.Mcp.BuiltInTools.ReadRecordsTool(),
                new Azure.DataApiBuilder.Mcp.BuiltInTools.UpdateRecordTool(),
                new Azure.DataApiBuilder.Mcp.BuiltInTools.DescribeEntitiesTool(),
                new Azure.DataApiBuilder.Mcp.BuiltInTools.AggregateRecordsTool(),
                new Azure.DataApiBuilder.Mcp.BuiltInTools.ExecuteEntityTool()
            };

            // Act & Assert
            foreach (IMcpTool tool in builtInTools)
            {
                Assert.AreEqual(allEnabled, tool.IsEnabled(config),
                    $"{tool.GetType().Name}.IsEnabled should be {allEnabled}");
            }
        }

        /// <summary>
        /// Validates that individual DML tool flags are respected (e.g., only create-record disabled).
        /// </summary>
        [TestMethod]
        public void BuiltInTools_IsEnabled_RespectsIndividualFlags()
        {
            // Arrange - only create-record and delete-record disabled
            DmlToolsConfig selectiveConfig = new(
                createRecord: false,
                deleteRecord: false);
            RuntimeConfig config = CreateRuntimeConfig(selectiveConfig);

            // Act & Assert - disabled tools
            Assert.IsFalse(new Azure.DataApiBuilder.Mcp.BuiltInTools.CreateRecordTool().IsEnabled(config));
            Assert.IsFalse(new Azure.DataApiBuilder.Mcp.BuiltInTools.DeleteRecordTool().IsEnabled(config));

            // Act & Assert - remaining tools should be enabled (default = true)
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.ReadRecordsTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.UpdateRecordTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.DescribeEntitiesTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.AggregateRecordsTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.ExecuteEntityTool().IsEnabled(config));
        }

        /// <summary>
        /// Validates that all built-in tools default to enabled when runtime.mcp is not configured
        /// (McpDmlTools is null because Runtime.Mcp is null).
        /// </summary>
        [TestMethod]
        public void BuiltInTools_IsEnabled_DefaultsToTrueWhenMcpNotConfigured()
        {
            // Arrange - config with no Mcp section at all → McpDmlTools returns null
            RuntimeConfig config = new(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: null,
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(new Dictionary<string, Entity>())
            );

            // Verify precondition: McpDmlTools is null
            Assert.IsNull(config.McpDmlTools);

            // Act & Assert - all built-in tools should default to enabled
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.CreateRecordTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.DeleteRecordTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.ReadRecordsTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.UpdateRecordTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.DescribeEntitiesTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.AggregateRecordsTool().IsEnabled(config));
            Assert.IsTrue(new Azure.DataApiBuilder.Mcp.BuiltInTools.ExecuteEntityTool().IsEnabled(config));
        }

        #region Private helpers

        /// <summary>
        /// Mock implementation of IMcpTool for testing purposes.
        /// </summary>
        private class MockMcpTool : IMcpTool
        {
            private readonly string _toolName;
            private readonly Func<RuntimeConfig, bool>? _isEnabledFunc;

            public MockMcpTool(string toolName, ToolType toolType, Func<RuntimeConfig, bool>? isEnabledFunc = null)
            {
                _toolName = toolName;
                ToolType = toolType;
                _isEnabledFunc = isEnabledFunc;
            }

            public ToolType ToolType { get; }

            public bool IsEnabled(RuntimeConfig config)
            {
                return _isEnabledFunc?.Invoke(config) ?? true;
            }

            public Tool GetToolMetadata()
            {
                // Create a simple JSON object for the input schema
                using JsonDocument doc = JsonDocument.Parse("{\"type\": \"object\"}");
                return new Tool
                {
                    Name = _toolName,
                    Description = $"Mock {ToolType} tool",
                    InputSchema = doc.RootElement.Clone()
                };
            }

            public Task<CallToolResult> ExecuteAsync(
                JsonDocument? arguments,
                IServiceProvider serviceProvider,
                CancellationToken cancellationToken = default)
            {
                // Not used in these tests
                throw new NotImplementedException();
            }
        }

        /// <summary>
        /// Creates a RuntimeConfig with the specified DmlToolsConfig for testing.
        /// </summary>
        private static RuntimeConfig CreateRuntimeConfig(DmlToolsConfig? dmlTools = null)
        {
            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(Enabled: true, Path: "/mcp", DmlTools: dmlTools),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(new Dictionary<string, Entity>())
            );
        }

        #endregion Private helpers
    }
}
