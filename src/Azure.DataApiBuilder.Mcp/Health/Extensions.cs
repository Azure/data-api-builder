// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Azure.DataApiBuilder.Mcp.Health;

public static class Extensions
{
    public static IEndpointRouteBuilder MapDabHealthChecks(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern = "")
    {
        endpoints.MapMcp();
        endpoints.MapHealthChecks(pattern, new()
        {
            ResponseWriter = async (context, report) =>
            {
                CheckResult mcpCheck = await McpCheck.CheckAsync(context.RequestServices);

                var response = new
                {
                    Status = mcpCheck.IsHealthy ? "Healthy" : "Unhealthy",
                    Timestamp = DateTime.UtcNow,
                    Checks = new object[] {
                        mcpCheck.ToReport()
                    }
                };

                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(response, new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                    WriteIndented = true
                }));
            }
        });
        return endpoints;
    }
}
