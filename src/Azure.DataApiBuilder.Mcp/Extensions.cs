// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Health;
using Azure.DataApiBuilder.Mcp.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Mcp
{
    public static class Extensions
    {
        private static McpOptions _mcpOptions = default!;

        public static IServiceCollection AddDabMcpServer(this IServiceCollection services, RuntimeConfigProvider runtimeConfigProvider)
        {
            if (runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                _mcpOptions = runtimeConfig?.Mcp ?? throw new NullReferenceException("Configuration is required.");
            }

            _ = services
                .AddDmlTools(_mcpOptions)
                .AddMcpServer()
                .AddMcpHealthChecks()
                .WithHttpTransport();

            return services;
        }

        public static IEndpointRouteBuilder MapDabMcp(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string? pattern = null)
        {
            endpoints.MapMcp(pattern);
            endpoints.MapMcpHealthEndpoint(pattern & "/health");
            return endpoints;
        }
    }
}
