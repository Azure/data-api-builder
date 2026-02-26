// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.ModelContextProtocol.HttpServer;
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
        /// Determines whether Entra ID (AzureAd) is configured for Microsoft MCP authentication.
        /// </summary>
        internal static bool IsEntraIdConfigured(IConfiguration configuration)
        {
            string? clientId = configuration["AzureAd:ClientId"];
            return !string.IsNullOrEmpty(clientId);
        }

        /// <summary>
        /// Configures the MCP server with tool capabilities.
        /// Uses Microsoft MCP server (with MISE/Entra ID auth) when AzureAd is configured,
        /// otherwise falls back to base MCP server without enterprise auth.
        /// </summary>
        internal static IServiceCollection ConfigureMcpServer(this IServiceCollection services, IConfiguration configuration)
        {
            IMcpServerBuilder builder;

            if (IsEntraIdConfigured(configuration))
            {
                // Use Microsoft MCP server with MISE/Entra ID authentication
                builder = services.AddMicrosoftMcpServer(configuration, options =>
                {
                    options.ResourceHost = "https://localhost";
                });
            }
            else
            {
                // Fall back to base MCP server without enterprise auth
                builder = services.AddMcpServer();
            }

            builder
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
                        tool!, toolName, arguments, request.Services!, ct);
                }
                finally
                {
                    arguments?.Dispose();
                }
            })
            .WithHttpTransport();

            // Configure underlying MCP server options
            services.Configure<McpServerOptions>(options =>
            {
                options.ServerInfo = new() { Name = McpProtocolDefaults.MCP_SERVER_NAME, Version = McpProtocolDefaults.MCP_SERVER_VERSION };
                options.Capabilities = new()
                {
                    Tools = new()
                };
            });

            return services;
        }
    }
}
