// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Azure.DataApiBuilder.Mcp.Health;

/// <summary>
/// Placeholder list entities health check; reports healthy if service provider available.
/// TODO: Wire to real metadata service when available within MCP project.
/// </summary>
public sealed class ListEntitiesHealthCheck : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        IServiceProvider? serviceProvider = Tools.Extensions.ServiceProvider;
        if (serviceProvider == null)
        {
            return new HealthCheckResult(
                status: HealthStatus.Unhealthy,
                description: "Service provider not available",
                data: new Dictionary<string, object> { { "error", "no_service_provider" } });
        }

        string json = await new Tools
            .SchemaLogic(serviceProvider)
            .GetEntityMetadataAsJsonAsync();

        return new(
            HealthStatus.Healthy,
            description: "Successfully retrieved entity metadata",
            data: new Dictionary<string, object> { { "entities", JsonDocument.Parse(json) } });
    }
}
