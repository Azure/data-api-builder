// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Mcp.Model
{
    /// <summary>
    /// Interface for MCP tool implementations
    /// </summary>
    public interface IMcpTool
    {
        /// <summary>
        /// Gets the type of the tool.
        /// </summary>
        ToolType ToolType { get; }

        /// <summary>
        /// Gets the tool metadata
        /// </summary>
        Tool GetToolMetadata();

        /// <summary>
        /// Determines whether this tool is enabled based on the runtime configuration.
        /// Disabled tools should not appear in tools/list responses.
        /// </summary>
        /// <param name="config">The current runtime configuration.</param>
        /// <returns>True if the tool is enabled; false otherwise.</returns>
        bool IsEnabled(RuntimeConfig config);

        /// <summary>
        /// Executes the tool with the provided arguments
        /// </summary>
        /// <param name="arguments">The JSON arguments passed to the tool</param>
        /// <param name="serviceProvider">The service provider for resolving dependencies</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>The tool execution result</returns>
        Task<CallToolResult> ExecuteAsync(
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken = default);
    }
}
