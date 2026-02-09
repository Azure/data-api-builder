// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Net;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Service.Exceptions;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Registry for managing MCP tools
    /// </summary>
    public class McpToolRegistry
    {
        private readonly Dictionary<string, IMcpTool> _tools = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Registers a tool in the registry
        /// </summary>
        /// <exception cref="DataApiBuilderException">Thrown when tool name is invalid or duplicate</exception>
        public void RegisterTool(IMcpTool tool)
        {
            Tool metadata = tool.GetToolMetadata();
            string toolName = metadata.Name?.Trim() ?? string.Empty;

            // Reject empty or whitespace-only tool names
            if (string.IsNullOrWhiteSpace(toolName))
            {
                throw new DataApiBuilderException(
                    message: "MCP tool name cannot be null, empty, or whitespace.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            // Check for duplicate tool names (case-insensitive)
            if (_tools.TryGetValue(toolName, out IMcpTool? existingTool))
            {
                string existingToolType = existingTool.ToolType == ToolType.BuiltIn ? "built-in" : "custom";
                string newToolType = tool.ToolType == ToolType.BuiltIn ? "built-in" : "custom";

                throw new DataApiBuilderException(
                    message: $"Duplicate MCP tool name '{toolName}' detected. " +
                            $"A {existingToolType} tool with this name is already registered. " +
                            $"Cannot register {newToolType} tool with the same name. " +
                            $"Tool names must be unique across all tool types.",
                    statusCode: HttpStatusCode.ServiceUnavailable,
                    subStatusCode: DataApiBuilderException.SubStatusCodes.ErrorInInitialization);
            }

            _tools[toolName] = tool;
        }

        /// <summary>
        /// Gets all registered tools
        /// </summary>
        public IEnumerable<Tool> GetAllTools()
        {
            return _tools.Values.Select(t => t.GetToolMetadata());
        }

        /// <summary>
        /// Tries to get a tool by name
        /// </summary>
        public bool TryGetTool(string toolName, out IMcpTool? tool)
        {
            return _tools.TryGetValue(toolName, out tool);
        }
    }
}
