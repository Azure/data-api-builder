// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
        /// Test that registering a tool with a unique name succeeds.
        /// </summary>
        [TestMethod]
        public void RegisterTool_WithUniqueName_Succeeds()
        {
            // Arrange
            McpToolRegistry registry = new();
            IMcpTool tool = new MockMcpTool("unique_tool", ToolType.BuiltIn);

            // Act & Assert - should not throw
            registry.RegisterTool(tool);

            // Verify tool was registered
            bool found = registry.TryGetTool("unique_tool", out IMcpTool? retrievedTool);
            Assert.IsTrue(found);
            Assert.IsNotNull(retrievedTool);
            Assert.AreEqual("unique_tool", retrievedTool.GetToolMetadata().Name);
        }

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
            IEnumerable<Tool> allTools = registry.GetAllTools();
            Assert.AreEqual(3, allTools.Count());
        }

        /// <summary>
        /// Test that registering two built-in tools with the same name throws an exception.
        /// </summary>
        [TestMethod]
        public void RegisterTool_WithDuplicateBuiltInToolName_ThrowsException()
        {
            // Arrange
            McpToolRegistry registry = new();
            IMcpTool tool1 = new MockMcpTool("duplicate_tool", ToolType.BuiltIn);
            IMcpTool tool2 = new MockMcpTool("duplicate_tool", ToolType.BuiltIn);

            // Act - Register first tool
            registry.RegisterTool(tool1);

            // Assert - Second registration should throw
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => registry.RegisterTool(tool2)
            );

            // Verify exception details
            Assert.IsTrue(exception.Message.Contains("Duplicate MCP tool name 'duplicate_tool' detected"));
            Assert.IsTrue(exception.Message.Contains("built-in tool with this name is already registered"));
            Assert.IsTrue(exception.Message.Contains("Cannot register built-in tool with the same name"));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, exception.SubStatusCode);
        }

        /// <summary>
        /// Test that registering two custom tools with the same name throws an exception.
        /// </summary>
        [TestMethod]
        public void RegisterTool_WithDuplicateCustomToolName_ThrowsException()
        {
            // Arrange
            McpToolRegistry registry = new();
            IMcpTool tool1 = new MockMcpTool("my_custom_tool", ToolType.Custom);
            IMcpTool tool2 = new MockMcpTool("my_custom_tool", ToolType.Custom);

            // Act - Register first tool
            registry.RegisterTool(tool1);

            // Assert - Second registration should throw
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => registry.RegisterTool(tool2)
            );

            // Verify exception details
            Assert.IsTrue(exception.Message.Contains("Duplicate MCP tool name 'my_custom_tool' detected"));
            Assert.IsTrue(exception.Message.Contains("custom tool with this name is already registered"));
            Assert.IsTrue(exception.Message.Contains("Cannot register custom tool with the same name"));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, exception.SubStatusCode);
        }

        /// <summary>
        /// Test that registering a custom tool with the same name as a built-in tool throws an exception.
        /// </summary>
        [TestMethod]
        public void RegisterTool_CustomToolConflictsWithBuiltIn_ThrowsException()
        {
            // Arrange
            McpToolRegistry registry = new();
            IMcpTool builtInTool = new MockMcpTool("create_record", ToolType.BuiltIn);
            IMcpTool customTool = new MockMcpTool("create_record", ToolType.Custom);

            // Act - Register built-in tool first
            registry.RegisterTool(builtInTool);

            // Assert - Custom tool registration should throw
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => registry.RegisterTool(customTool)
            );

            // Verify exception details
            Assert.IsTrue(exception.Message.Contains("Duplicate MCP tool name 'create_record' detected"));
            Assert.IsTrue(exception.Message.Contains("built-in tool with this name is already registered"));
            Assert.IsTrue(exception.Message.Contains("Cannot register custom tool with the same name"));
            Assert.IsTrue(exception.Message.Contains("Tool names must be unique across all tool types"));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, exception.SubStatusCode);
        }

        /// <summary>
        /// Test that registering a built-in tool with the same name as a custom tool throws an exception.
        /// </summary>
        [TestMethod]
        public void RegisterTool_BuiltInToolConflictsWithCustom_ThrowsException()
        {
            // Arrange
            McpToolRegistry registry = new();
            IMcpTool customTool = new MockMcpTool("my_stored_proc", ToolType.Custom);
            IMcpTool builtInTool = new MockMcpTool("my_stored_proc", ToolType.BuiltIn);

            // Act - Register custom tool first
            registry.RegisterTool(customTool);

            // Assert - Built-in tool registration should throw
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => registry.RegisterTool(builtInTool)
            );

            // Verify exception details
            Assert.IsTrue(exception.Message.Contains("Duplicate MCP tool name 'my_stored_proc' detected"));
            Assert.IsTrue(exception.Message.Contains("custom tool with this name is already registered"));
            Assert.IsTrue(exception.Message.Contains("Cannot register built-in tool with the same name"));
            Assert.IsTrue(exception.Message.Contains("Tool names must be unique across all tool types"));
            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ErrorInInitialization, exception.SubStatusCode);
        }

        /// <summary>
        /// Test that tool name comparison is case-sensitive.
        /// Tools with different casing should be allowed.
        /// </summary>
        [TestMethod]
        public void RegisterTool_WithDifferentCasing_Succeeds()
        {
            // Arrange
            McpToolRegistry registry = new();
            IMcpTool tool1 = new MockMcpTool("my_tool", ToolType.BuiltIn);
            IMcpTool tool2 = new MockMcpTool("My_Tool", ToolType.Custom);
            IMcpTool tool3 = new MockMcpTool("MY_TOOL", ToolType.Custom);

            // Act & Assert - All should register successfully (case-sensitive)
            registry.RegisterTool(tool1);
            registry.RegisterTool(tool2);
            registry.RegisterTool(tool3);

            // Verify all tools were registered
            IEnumerable<Tool> allTools = registry.GetAllTools();
            Assert.AreEqual(3, allTools.Count());
        }

        /// <summary>
        /// Test that GetAllTools returns all registered tools.
        /// </summary>
        [TestMethod]
        public void GetAllTools_ReturnsAllRegisteredTools()
        {
            // Arrange
            McpToolRegistry registry = new();
            registry.RegisterTool(new MockMcpTool("tool_a", ToolType.BuiltIn));
            registry.RegisterTool(new MockMcpTool("tool_b", ToolType.Custom));
            registry.RegisterTool(new MockMcpTool("tool_c", ToolType.BuiltIn));

            // Act
            IEnumerable<Tool> allTools = registry.GetAllTools();

            // Assert
            Assert.AreEqual(3, allTools.Count());
            Assert.IsTrue(allTools.Any(t => t.Name == "tool_a"));
            Assert.IsTrue(allTools.Any(t => t.Name == "tool_b"));
            Assert.IsTrue(allTools.Any(t => t.Name == "tool_c"));
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
        /// Test edge case: empty tool name (if allowed by validation)
        /// </summary>
        [TestMethod]
        public void RegisterTool_WithEmptyToolName_CanRegisterOnce()
        {
            // Arrange
            McpToolRegistry registry = new();
            IMcpTool tool1 = new MockMcpTool("", ToolType.BuiltIn);
            IMcpTool tool2 = new MockMcpTool("", ToolType.Custom);

            // Act - Register first tool with empty name
            registry.RegisterTool(tool1);

            // Assert - Second tool with empty name should throw
            Assert.ThrowsException<DataApiBuilderException>(
                () => registry.RegisterTool(tool2)
            );
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
        /// Test that entity names converted to snake_case that result in same tool name are caught.
        /// This validates the comment from @JerryNixon about entity aliases being unique.
        /// Although entity names must be unique at config level, this ensures tool name
        /// conversion doesn't create conflicts (e.g., GetUser and get_user would both become get_user).
        /// </summary>
        [TestMethod]
        public void RegisterTool_WithConflictingSnakeCaseNames_DetectsDuplicates()
        {
            // Arrange
            McpToolRegistry registry = new();

            // First tool with snake_case name
            IMcpTool tool1 = new MockMcpTool("get_user", ToolType.Custom);

            // Second tool that would convert to the same snake_case
            // In reality, these would come from different entity names that convert to same tool name
            IMcpTool tool2 = new MockMcpTool("get_user", ToolType.Custom);

            // Act - Register first tool
            registry.RegisterTool(tool1);

            // Assert - Second registration should throw
            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(
                () => registry.RegisterTool(tool2)
            );

            // Verify exception details
            Assert.IsTrue(exception.Message.Contains("Duplicate MCP tool name 'get_user' detected"));
        }

        #region Mock Tool Implementation

        /// <summary>
        /// Mock implementation of IMcpTool for testing purposes.
        /// </summary>
        private class MockMcpTool : IMcpTool
        {
            private readonly string _toolName;

            public MockMcpTool(string toolName, ToolType toolType)
            {
                _toolName = toolName;
                ToolType = toolType;
            }

            public ToolType ToolType { get; }

            public Tool GetToolMetadata()
            {
                return new Tool
                {
                    Name = _toolName,
                    Description = $"Mock {ToolType} tool",
                    InputSchema = JsonSerializer.Deserialize<JsonElement>("{\"type\": \"object\"}")
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

        #endregion
    }
}
