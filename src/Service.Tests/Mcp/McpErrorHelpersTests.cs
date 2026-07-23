// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Unit tests for <see cref="McpErrorHelpers"/> covering the standardized error
    /// responses used across the MCP tools. Pure logic; no database required.
    /// </summary>
    [TestClass]
    public class McpErrorHelpersTests
    {
        private static (string type, string message) ParseError(CallToolResult result)
        {
            TextContentBlock block = (TextContentBlock)result.Content[0];
            JsonElement error = JsonDocument.Parse(block.Text).RootElement.GetProperty("error");
            return (error.GetProperty("type").GetString()!, error.GetProperty("message").GetString()!);
        }

        [TestMethod]
        public void PermissionDenied_ProducesPermissionDeniedError()
        {
            CallToolResult result = McpErrorHelpers.PermissionDenied(
                "read_records", "Book", "read", "No active HTTP request context.", null);

            Assert.IsTrue(result.IsError == true);
            (string type, string message) = ParseError(result);
            Assert.AreEqual(McpErrorCode.PermissionDenied.ToString(), type);
            StringAssert.Contains(message, "read");
            StringAssert.Contains(message, "Book");
            StringAssert.Contains(message, "No active HTTP request context.");
        }

        [TestMethod]
        public void ToolDisabled_DefaultMessage_MentionsToolName()
        {
            CallToolResult result = McpErrorHelpers.ToolDisabled("create_record", null);

            Assert.IsTrue(result.IsError == true);
            (string type, string message) = ParseError(result);
            Assert.AreEqual(McpErrorCode.ToolDisabled.ToString(), type);
            StringAssert.Contains(message, "create_record");
            StringAssert.Contains(message, "disabled");
        }

        [TestMethod]
        public void ToolDisabled_CustomMessage_IsUsed()
        {
            CallToolResult result = McpErrorHelpers.ToolDisabled(
                "create_record", null, "DML tools are disabled for entity 'Book'.");

            (string type, string message) = ParseError(result);
            Assert.AreEqual(McpErrorCode.ToolDisabled.ToString(), type);
            Assert.AreEqual("DML tools are disabled for entity 'Book'.", message);
        }

        [TestMethod]
        public void FieldNotFound_IncludesGuidanceToDescribeEntities()
        {
            CallToolResult result = McpErrorHelpers.FieldNotFound(
                "aggregate_records", "Book", "badField", "field", null);

            Assert.IsTrue(result.IsError == true);
            (string type, string message) = ParseError(result);
            Assert.AreEqual("FieldNotFound", type);
            StringAssert.Contains(message, "badField");
            StringAssert.Contains(message, "Book");
            StringAssert.Contains(message, "field");
            StringAssert.Contains(message, "describe_entities");
        }
    }
}
