// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Mcp
{
    public class Jerry { }

    public static class StartupExtensions
    {
        public static IServiceCollection AddDabMcpServer(this IServiceCollection services, RuntimeConfigProvider runtimeConfigProvider, ILoggerFactory loggerFactory)
        {
            ILogger<Jerry> logger = loggerFactory.CreateLogger<Jerry>();

            if (!runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                logger.LogError("Can't get config.");
            }

            services
                .AddMcpServer()
                .WithToolsFromAssembly()
                .WithHttpTransport();
            return services;
        }

        public static IEndpointRouteBuilder MapMcp(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern = "")
        {
            endpoints.MapMcp();
            return endpoints;
        }
    }

    [McpServerToolType]
    public static class Tools
    {
        [McpServerTool]
        public static string Echo(string message) => message;
    }
}
