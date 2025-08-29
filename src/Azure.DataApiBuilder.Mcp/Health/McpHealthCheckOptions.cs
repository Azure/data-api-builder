// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Azure.DataApiBuilder.Mcp.Health;

/// <summary>
/// Writes a simplified MCP health report in a consistent JSON format.
/// </summary>
public class McpHealthCheckOptions : HealthCheckOptions
{
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public McpHealthCheckOptions(string tag)
    {
        ResultStatusCodes.Clear();
        ResultStatusCodes.Add(HealthStatus.Healthy, StatusCodes.Status200OK);
        ResultStatusCodes.Add(HealthStatus.Degraded, StatusCodes.Status200OK);
        ResultStatusCodes.Add(HealthStatus.Unhealthy, StatusCodes.Status503ServiceUnavailable);

        AllowCachingResponses = true;

        ResponseWriter = WriteAsync;

        Predicate = r => r.Tags.Contains(tag);
    }

    private Task WriteAsync(HttpContext context, HealthReport report)
    {
        var response = new
        {
            status = report.Status.ToString(),
            timestamp = DateTime.UtcNow,
            checks = report.Entries.Select(kvp => new
            {
                name = kvp.Key,
                status = kvp.Value.Status.ToString(),
                description = kvp.Value.Description,
                data = kvp.Value.Data
            })
        };

        context.Response.ContentType = "application/json";
        return context.Response.WriteAsync(JsonSerializer.Serialize(response, _jsonOptions));
    }
}
