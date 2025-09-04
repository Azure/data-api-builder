// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Health;
using Azure.DataApiBuilder.Mcp.Tools;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Mcp
{
    public static class Extensions
    {
        private static McpOptions _mcpOptions = default!;

        public static IServiceCollection AddDabMcpServer(this IServiceCollection services, RuntimeConfigProvider runtimeConfigProvider)
        {
            if (runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                _mcpOptions = runtimeConfig?.Ai?.Mcp ?? throw new NullReferenceException("Configuration is required.");
            }

            // ✅ Register domain tools using the extensible mechanism
            services.AddDmlTools(_mcpOptions);

            // ✅ Register the ToolHandler for centralized tool call handling
            services.AddSingleton<ToolHandler>();

            // ✅ Register MCP server with DYNAMIC tool handlers that use the extensible registration
            services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "MyServer", Version = "1.0.0" };

                options.Capabilities = new ServerCapabilities
                {
                    Tools = new ToolsCapability
                    {
                        // Get tools from registered McpServerTool instances
                        ListToolsHandler = (request, ct) =>
                        {
                            List<Tool> tools = new();

                            // Access registered tools from ServiceProvider if available
                            IServiceProvider? serviceProvider = Tools.Extensions.ServiceProvider;
                            if (serviceProvider != null)
                            {
                                IEnumerable<McpServerTool> registeredTools = serviceProvider.GetServices<McpServerTool>();

                                foreach (McpServerTool tool in registeredTools)
                                {
                                    tools.Add(new Tool
                                    {
                                        Name = tool.ProtocolTool.Name,
                                        Description = tool.ProtocolTool.Description ?? string.Empty,
                                        InputSchema = tool.ProtocolTool.InputSchema
                                    });
                                }
                            }

                            return ValueTask.FromResult(new ListToolsResult { Tools = tools });
                        },

                        // Use ToolHandler to delegate tool call handling - completely modular
                        CallToolHandler = async (request, ct) =>
                        {
                            IServiceProvider? serviceProvider = Tools.Extensions.ServiceProvider;
                            if (serviceProvider == null)
                            {
                                return new CallToolResult
                                {
                                    Content = [new TextContentBlock { Type = "text", Text = "Error: Service provider not available" }],
                                    IsError = true
                                };
                            }

                            // Get the ToolHandler from DI and delegate to it
                            ToolHandler toolHandler = serviceProvider.GetRequiredService<ToolHandler>();
                            return await toolHandler.HandleToolCallAsync(request, ct);
                        }
                    }
                };
            })
            .WithHttpTransport();

            return services;
        }

        public static IEndpointRouteBuilder MapDabMcp(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern = "")
        {
            endpoints.MapMcp("/mcp");
            endpoints.MapDabHealthChecks("/mcp/health");
            return endpoints;
        }
    }
}
