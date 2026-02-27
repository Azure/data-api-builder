// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Configuration for MCP server capabilities and handlers
    /// </summary>
    internal static class McpServerConfiguration
    {
        /// <summary>
        /// Configures the MCP server with tool capabilities.
        /// </summary>
        internal static IServiceCollection ConfigureMcpServer(this IServiceCollection services)
        {
            services.AddMcpServer()
            .WithListToolsHandler((RequestContext<ListToolsRequestParams> request, CancellationToken ct) =>
            {
                McpToolRegistry? toolRegistry = request.Services?.GetRequiredService<McpToolRegistry>();
                if (toolRegistry == null)
                {
                    throw new InvalidOperationException("Tool registry is not available.");
                }

                List<Tool> tools = toolRegistry.GetAllTools().ToList();

                return ValueTask.FromResult(new ListToolsResult
                {
                    Tools = tools
                });
            })
            .WithCallToolHandler(async (RequestContext<CallToolRequestParams> request, CancellationToken ct) =>
            {
                McpToolRegistry? toolRegistry = request.Services?.GetRequiredService<McpToolRegistry>();
                if (toolRegistry == null)
                {
                    throw new InvalidOperationException("Tool registry is not available.");
                }

                string? toolName = request.Params?.Name;
                if (string.IsNullOrEmpty(toolName))
                {
                    throw new McpException("Tool name is required.");
                }

                if (!toolRegistry.TryGetTool(toolName, out IMcpTool? tool))
                {
                    throw new McpException($"Unknown tool: '{toolName}'");
                }

                if (tool is null || request.Services is null)
                {
                    throw new InvalidOperationException("Tool or service provider unexpectedly null.");
                }

                JsonDocument? arguments = null;
                try
                {
                    if (request.Params?.Arguments != null)
                    {
                        // Convert IReadOnlyDictionary<string, JsonElement> to JsonDocument
                        Dictionary<string, object?> jsonObject = new();
                        foreach (KeyValuePair<string, JsonElement> kvp in request.Params.Arguments)
                        {
                            jsonObject[kvp.Key] = kvp.Value;
                        }

                        string json = JsonSerializer.Serialize(jsonObject);
                        arguments = JsonDocument.Parse(json);
                    }

                    return await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                        tool, toolName, arguments, request.Services, ct);
                }
                finally
                {
                    arguments?.Dispose();
                }
            })
            .WithHttpTransport();

            // Configure underlying MCP server options defensively to avoid overwriting any defaults
            services.PostConfigure<McpServerOptions>(options =>
            {
                options.ServerInfo ??= new() { Name = McpProtocolDefaults.MCP_SERVER_NAME, Version = McpProtocolDefaults.MCP_SERVER_VERSION };
                options.ServerInfo.Name = McpProtocolDefaults.MCP_SERVER_NAME;
                options.ServerInfo.Version = McpProtocolDefaults.MCP_SERVER_VERSION;
                options.Capabilities ??= new();
                options.Capabilities.Tools ??= new();
            });

            return services;
        }
    }
}
