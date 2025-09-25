// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Configuration for MCP server capabilities and handlers
    /// </summary>
    internal static class McpServerConfiguration
    {
        /// <summary>
        /// Configures the MCP server with tool capabilities
        /// </summary>
        internal static IServiceCollection ConfigureMcpServer(this IServiceCollection services)
        {
            services.AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = "Data API builder MCP Server", Version = "1.0.0" };
                options.Capabilities = new()
                {
                    Tools = new()
                    {
                        ListToolsHandler = (request, ct) =>
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
                        },
                        CallToolHandler = async (request, ct) =>
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

                            try
                            {
                                return await tool!.ExecuteAsync(arguments, request.Services!, ct);
                            }
                            finally
                            {
                                arguments?.Dispose();
                            }
                        }
                    }
                };
            })
            .WithHttpTransport();

            return services;
        }
    }
}
