// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Mcp.Model;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Registry for managing MCP tools
    /// </summary>
    public class McpToolRegistry
    {
        private readonly Dictionary<string, IMcpTool> _tools = new();

        /// <summary>
        /// Registers a tool in the registry
        /// </summary>
        public void RegisterTool(IMcpTool tool)
        {
            Tool metadata = tool.GetToolMetadata();
            _tools[metadata.Name] = tool;
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
