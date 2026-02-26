// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Reflection;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Extension methods for configuring MCP services in the DI container
    /// </summary>
    public static class McpServiceCollectionExtensions
    {
        /// <summary>
        /// Determines whether Entra ID (AzureAd) is configured for Microsoft MCP authentication.
        /// When configured, the Microsoft MCP server with MISE auth is used.
        /// When not configured, the base MCP server without enterprise auth is used.
        /// </summary>
        public static bool IsEntraIdConfigured(IConfiguration configuration)
        {
            return McpServerConfiguration.IsEntraIdConfigured(configuration);
        }

        /// <summary>
        /// Adds MCP server and related services to the service collection
        /// </summary>
        public static IServiceCollection AddDabMcpServer(this IServiceCollection services, RuntimeConfigProvider runtimeConfigProvider, IConfiguration configuration)
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

            // Register custom tools from configuration
            RegisterCustomTools(services, runtimeConfig);

            // Configure MCP server
            services.ConfigureMcpServer(configuration);

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

        /// <summary>
        /// Registers custom MCP tools generated from stored procedure entity configurations.
        /// </summary>
        private static void RegisterCustomTools(IServiceCollection services, RuntimeConfig config)
        {
            // Create custom tools and register each as a singleton
            foreach (IMcpTool customTool in CustomMcpToolFactory.CreateCustomTools(config))
            {
                services.AddSingleton<IMcpTool>(customTool);
            }
        }
    }
}
