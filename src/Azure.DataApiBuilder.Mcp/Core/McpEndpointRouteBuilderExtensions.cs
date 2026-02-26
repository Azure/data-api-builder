// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ModelContextProtocol.HttpServer;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Extension methods for mapping MCP endpoints to an <see cref="IEndpointRouteBuilder"/>.
    /// </summary>
    public static class McpEndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps the MCP endpoint to the specified <see cref="IEndpointRouteBuilder"/> if MCP is enabled in the runtime configuration.
        /// Uses Microsoft MCP endpoint mapping (with auth/rate-limiting) when Entra ID is configured,
        /// otherwise falls back to base MCP endpoint mapping.
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

            // Use Microsoft MCP endpoint mapping when Entra ID is configured, otherwise use base MCP
            IConfiguration configuration = endpoints.ServiceProvider.GetRequiredService<IConfiguration>();
            if (McpServerConfiguration.IsEntraIdConfigured(configuration))
            {
                endpoints.MapMicrosoftMcpServer(mcpPath);
            }
            else
            {
                endpoints.MapMcp(mcpPath);
            }

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
