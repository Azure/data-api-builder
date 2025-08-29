// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Azure.DataApiBuilder.Mcp.Health;

/// <summary>
/// Attempts to connect to local MCP SSE endpoint and list tools.
/// </summary>
public sealed class ClientConnectionHealthCheck : IHealthCheck
{
    private readonly IHttpContextAccessor _http;
    private readonly ILogger<ClientConnectionHealthCheck> _logger;
    private readonly ILoggerFactory _loggerFactory;

    public ClientConnectionHealthCheck(IHttpContextAccessor http, ILogger<ClientConnectionHealthCheck> logger, ILoggerFactory loggerFactory)
    {
        _http = http;
        _logger = logger;
        _loggerFactory = loggerFactory;
    }

    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        HttpRequest? request = _http.HttpContext?.Request;
        if (request == null)
        {
            return new HealthCheckResult(HealthStatus.Degraded, description: "HttpContext not available", data: new Dictionary<string, object> { { "error", "no_http_context" } });
        }

        string endpoint = $"{request.Scheme}://{request.Host}";
        try
        {
            SseClientTransport clientTransport = new(new()
            {
                Endpoint = new Uri(endpoint),
                Name = "HealthCheck"
            });

            McpClientOptions clientOptions = new()
            {
                Capabilities = new() { }
            };

            IMcpClient mcpClient = await McpClientFactory.CreateAsync(
                clientTransport: clientTransport,
                clientOptions: clientOptions,
                loggerFactory: _loggerFactory);

            IList<McpClientTool> tools = await mcpClient.ListToolsAsync(cancellationToken: cancellationToken);

            string[] names = [.. tools.Select(t => t.Name).OrderBy(n => n)];

            if (names.Length == 0)
            {
                return new HealthCheckResult(HealthStatus.Unhealthy, description: "Connected but no tools returned", data: new Dictionary<string, object>
                {
                    { "endpoint", endpoint },
                    { "toolCount", 0 }
                });
            }

            return new HealthCheckResult(HealthStatus.Healthy, data: new Dictionary<string, object>
            {
                { "endpoint", endpoint },
                { "tools", names }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Client connection health check failed");
            return new HealthCheckResult(HealthStatus.Unhealthy, description: ex.Message, data: new Dictionary<string, object> { { "endpoint", endpoint }, { "error", ex.GetType().Name } });
        }
    }
}
