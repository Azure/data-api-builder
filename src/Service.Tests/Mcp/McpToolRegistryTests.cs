// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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

        #region Private helpers

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

        #endregion Private helpers
    }
}
