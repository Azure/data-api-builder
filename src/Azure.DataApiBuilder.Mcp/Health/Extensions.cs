// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.DataApiBuilder.Mcp.Health;

internal static class Extensions
{
    private const string MCP_TAG = "mcp";

    public static IMcpServerBuilder AddMcpHealthChecks(this IMcpServerBuilder builder)
    {
        _ = builder.Services.AddHttpContextAccessor();
        _ = builder.Services.AddHealthChecks()
            .AddCheck<ClientConnectionHealthCheck>("MCP Registration", tags: [MCP_TAG])
            .AddCheck<ListEntitiesHealthCheck>("MCP Tool: list_entities", tags: [MCP_TAG]);
        return builder;
    }

    /// <summary>
    /// Maps a MCP-only health endpoint (default: /mcp/health). Predicate filters to MCP-tagged checks only.
    /// </summary>
    public static IEndpointRouteBuilder MapMcpHealthEndpoint(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string? pattern)
    {
        _ = endpoints.MapHealthChecks(
            pattern: pattern ?? "/mcp/health",
            options: new McpHealthCheckOptions(MCP_TAG));
        return endpoints;
    }
}
