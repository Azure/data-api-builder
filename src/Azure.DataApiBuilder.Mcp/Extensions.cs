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

namespace Azure.DataApiBuilder.Mcp
{
    public static class Extensions
    {
        private static McpRuntimeOptions? _mcpOptions;

        public static IServiceCollection AddDabMcpServer(this IServiceCollection services, RuntimeConfigProvider runtimeConfigProvider)
        {
            if (!runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                // If config is not available, skip MCP setup
                return services;
            }

            _mcpOptions = runtimeConfig?.Runtime?.Mcp;

            // Only add MCP server if it's enabled in the configuration
            if (_mcpOptions == null || !_mcpOptions.Enabled)
            {
                return services;
            }

            // Register the tool registry
            services.AddSingleton<McpToolRegistry>();

            // Register individual tools
            services.AddSingleton<IMcpTool, EchoTool>();
            services.AddSingleton<IMcpTool, DescribeEntitiesTool>();
            services.AddSingleton<IMcpTool, ComplexTool>();

            // Register domain tools
            services.AddDmlTools(_mcpOptions);

            // Register MCP server with dynamic tool handlers
            services.AddMcpServer(options =>
            {
                options.ServerInfo = new() { Name = "Data API Builder MCP Server", Version = "1.0.0" };

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

                            if (!toolRegistry.TryGetTool(toolName, out IMcpTool? tool) || tool == null)
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
                                if (request.Services == null)
                                {
                                    throw new InvalidOperationException("Service provider is not available in the request context.");
                                }

                                return await tool.ExecuteAsync(arguments, request.Services, ct);
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

            // Build the tool registry
            services.AddHostedService<McpToolRegistryInitializer>();

            return services;
        }

        public static IEndpointRouteBuilder MapDabMcp(this IEndpointRouteBuilder endpoints, RuntimeConfigProvider runtimeConfigProvider, [StringSyntax("Route")] string pattern = "")
        {
            if (!runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                // If config is not available, skip MCP mapping
                return endpoints;
            }

            McpRuntimeOptions? mcpOptions = runtimeConfig?.Runtime?.Mcp;

            // Only map MCP endpoints if MCP is enabled
            if (mcpOptions == null || !mcpOptions.Enabled)
            {
                return endpoints;
            }

            // Get the MCP path with proper null handling and default
            string mcpPath = mcpOptions.Path ?? McpRuntimeOptions.DEFAULT_PATH;

            // Map the MCP endpoint
            endpoints.MapMcp(mcpPath);

            // Map health checks relative to the MCP path
            string healthPath = mcpPath.TrimEnd('/') + "/health";
            endpoints.MapDabHealthChecks(healthPath);

            return endpoints;
        }
    }
}
