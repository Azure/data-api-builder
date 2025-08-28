// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;

namespace Azure.DataApiBuilder.Mcp.Health;

public class McpCheck
{
    private readonly static string _name = "MCP Server Tools";

    public static async Task<CheckResult> CheckAsync(IServiceProvider serviceProvider)
    {
        try
        {
            ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

            // Get the MCP server endpoint
            HttpRequest? request = serviceProvider.GetService<IHttpContextAccessor>()?.HttpContext?.Request;
            if (request == null)
            {
                return new CheckResult(
                    Name: _name,
                    IsHealthy: false,
                    Message: "HttpContext not available",
                    Tags: []
                );
            }

            string scheme = request.Scheme;
            string host = request.Host.Value;
            string endpoint = $"{scheme}://{host}";

            IMcpClient mcpClient = await McpClientFactory.CreateAsync(
                new SseClientTransport(new()
                {
                    Endpoint = new Uri(endpoint),
                    Name = "HealthCheck"
                }),
                clientOptions: new()
                {
                    Capabilities = new() { }
                },
                loggerFactory: loggerFactory);

            IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();
            string[] toolNames = mcpTools.Select(t => t.Name).OrderBy(t => t).ToArray();

            return new CheckResult(
                Name: _name,
                IsHealthy: toolNames.Length != 0,
                Message: toolNames.Length != 0 ? "Okay" : "No tools found",
                Tags: new Dictionary<string, string>
                {
                    { "endpoint", endpoint },
                    { "tools", string.Join(", ", toolNames) }
                });
        }
        catch (Exception ex)
        {
            return new CheckResult(
                Name: _name,
                IsHealthy: false,
                Message: ex.Message,
                Tags: new Dictionary<string, string>()
            );
        }
    }
}
