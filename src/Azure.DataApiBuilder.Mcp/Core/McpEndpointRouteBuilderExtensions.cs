// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Extension methods for mapping MCP endpoints to an <see cref="IEndpointRouteBuilder"/>.
    /// </summary>
    public static class McpEndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps the MCP endpoint to the specified <see cref="IEndpointRouteBuilder"/> if MCP is enabled in the runtime configuration.
        /// </summary>
        public static IEndpointRouteBuilder MapDabMcp(
            this IEndpointRouteBuilder endpoints,
            RuntimeConfigProvider runtimeConfigProvider,
            [StringSyntax("Route")] string pattern = "")
        {
            if (!TryGetMcpOptions(runtimeConfigProvider, out McpRuntimeOptions? mcpOptions) || mcpOptions == null || !mcpOptions.Enabled)
            {
                return endpoints;
            }

            string mcpPath = mcpOptions.Path ?? McpRuntimeOptions.DEFAULT_PATH;

            // Map the MCP endpoint
            endpoints.MapMcp(mcpPath);

            return endpoints;
        }

        /// <summary>
        /// Gets MCP options from the runtime configuration
        /// </summary>
        /// <param name="runtimeConfigProvider">Runtime config provider</param>
        /// <param name="mcpOptions">MCP options</param>
        /// <returns>True if MCP options were found, false otherwise</returns>
        private static bool TryGetMcpOptions(RuntimeConfigProvider runtimeConfigProvider, out McpRuntimeOptions? mcpOptions)
        {
            mcpOptions = null;

            if (!runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                return false;
            }

            mcpOptions = runtimeConfig?.Runtime?.Mcp;
            return mcpOptions != null;
        }
    }
}
