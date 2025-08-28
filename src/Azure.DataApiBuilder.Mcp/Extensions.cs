// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Health;
using Azure.DataApiBuilder.Mcp.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.DataApiBuilder.Mcp
{
    public static class Extensions
    {
        private static McpOptions _mcpOptions = default!;

        public static IServiceCollection AddDabMcpServer(this IServiceCollection services, RuntimeConfigProvider runtimeConfigProvider)
        {
            if (runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                _mcpOptions = runtimeConfig?.Ai?.Mcp ?? throw new NullReferenceException("Configuration is required.");
            }

            services.AddDmlTools(_mcpOptions);

            services
                .AddMcpServer()
                .WithHttpTransport();

            return services;
        }

        public static IEndpointRouteBuilder MapDabMcp(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern = "")
        {
            endpoints.MapMcp();
            endpoints.MapDabHealthChecks("/jerry");
            return endpoints;
        }
    }
}
