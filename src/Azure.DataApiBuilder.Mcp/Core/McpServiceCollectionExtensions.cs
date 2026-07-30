// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Extension methods for configuring MCP services in the DI container
    /// </summary>
    public static class McpServiceCollectionExtensions
    {
        /// <summary>
        /// Adds MCP server and related services to the service collection
        /// </summary>
        public static IServiceCollection AddDabMcpServer(this IServiceCollection services, RuntimeConfigProvider runtimeConfigProvider)
        {
            if (!runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                // If config is not available, skip MCP setup
                return services;
            }

            // Only add MCP server if it's enabled in the configuration
            if (!runtimeConfig.IsMcpEnabled)
            {
                return services;
            }

            // Register core MCP services
            services.AddSingleton<McpToolRegistry>();
            services.AddSingleton<McpToolRegistryRefreshService>();
            services.AddSingleton<IMcpToolRegistryRefreshService>(serviceProvider =>
                serviceProvider.GetRequiredService<McpToolRegistryRefreshService>());
            services.AddSingleton<IHostedService>(serviceProvider =>
                serviceProvider.GetRequiredService<McpToolRegistryRefreshService>());

            // Auto-discover and register built-in MCP tools. Custom tools are configuration-
            // generation objects and are created by McpToolRegistryRefreshService.
            RegisterAllMcpTools(services);

            // Configure MCP server and propagate runtime description to MCP initialize instructions.
            services.ConfigureMcpServer(runtimeConfig.Runtime?.Mcp?.Description);

            return services;
        }

        /// <summary>
        /// Automatically discovers and registers all classes implementing IMcpTool
        /// </summary>
        private static void RegisterAllMcpTools(IServiceCollection services)
        {
            Assembly mcpAssembly = typeof(IMcpTool).Assembly;

            IEnumerable<Type> toolTypes = mcpAssembly.GetTypes()
                .Where(t => t.IsClass &&
                           !t.IsAbstract &&
                           typeof(IMcpTool).IsAssignableFrom(t) &&
                           t != typeof(DynamicCustomTool)); // Exclude DynamicCustomTool from auto-registration

            foreach (Type toolType in toolTypes)
            {
                services.AddSingleton(typeof(IMcpTool), toolType);
            }
        }

    }
}
