// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.Extensions.DependencyInjection;

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
            services.AddHostedService<McpToolRegistryInitializer>();

            // Auto-discover and register all MCP tools
            RegisterAllMcpTools(services);

            // Configure MCP server
            services.ConfigureMcpServer();

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
                           typeof(IMcpTool).IsAssignableFrom(t));

            foreach (Type toolType in toolTypes)
            {
                services.AddSingleton(typeof(IMcpTool), toolType);
            }
        }
    }
}
