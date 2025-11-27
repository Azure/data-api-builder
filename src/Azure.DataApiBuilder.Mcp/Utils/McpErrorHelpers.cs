// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Mcp.Utils
{
    /// <summary>
    /// Helper utilities for creating standardized MCP error responses.
    /// Only includes helpers currently being centralized.
    /// </summary>
    public static class McpErrorHelpers
    {
        public static CallToolResult PermissionDenied(string toolName, string entityName, string operation, string detail, ILogger? logger)
        {
            string message = $"Permission denied for {operation} on entity '{entityName}'. {detail}";
            return McpResponseBuilder.BuildErrorResult(toolName, Model.McpErrorCode.PermissionDenied.ToString(), message, logger);
        }

        // Centralized language for 'tool disabled' errors. Pass the tool name, e.g. "read_records".
        public static CallToolResult ToolDisabled(string toolName, ILogger? logger)
        {
            string message = $"The {toolName} tool is disabled in the configuration.";
            return McpResponseBuilder.BuildErrorResult(toolName, Model.McpErrorCode.ToolDisabled.ToString(), message, logger);
        }
    }
}
