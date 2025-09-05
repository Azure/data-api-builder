// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
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
            if (!Debugger.IsAttached)
            {
                Debugger.Launch(); // Forces Visual Studio/VS Code to attach
            }

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
                            ValueTask.FromResult(new ListToolsResult
                            {
                                Tools =
                                [
                                    new()
                                    {
                                        Name = "echonew",
                                        Description = "Echoes the input back to the client.",
                                        InputSchema = JsonSerializer.Deserialize<JsonElement>(
                                            @"{
                                                ""type"": ""object"",
                                                ""properties"": { ""message"": { ""type"": ""string"" } },
                                                ""required"": [""message""]
                                            }"
                                        )
                                    },
                                    new()
                                    {
                                        Name = "list_entities",
                                        Description = "Lists all entities in the database."
                                    }
                                ]
                            }),
                        CallToolHandler = async (request, ct) =>
                        {
                            if (request.Params?.Name == "echonew" &&
                                request.Params.Arguments?.TryGetValue("message", out JsonElement messageEl) == true)
                            {
                                string? msg = messageEl.ValueKind == JsonValueKind.String
                                    ? messageEl.GetString()
                                    : messageEl.ToString();

                                return new CallToolResult
                                {
                                    Content = [new TextContentBlock { Type = "text", Text = $"Echo: {msg}" }]
                                };
                            }
                            else if (request.Params?.Name == "list_entities")
                            {
                                // Get the service provider from the MCP context
                                IServiceProvider? serviceProvider = request.Services;
                                if (serviceProvider == null)
                                {
                                    throw new InvalidOperationException("Service provider is not available in the request context.");
                                }

                                // Create a scope to resolve scoped services
                                using IServiceScope scope = serviceProvider.CreateScope();
                                IServiceProvider scopedProvider = scope.ServiceProvider;

                                // Set the service provider for DmlTools
                                Azure.DataApiBuilder.Mcp.Tools.Extensions.ServiceProvider = scopedProvider;

                                // Call the ListEntities tool method
                                string entitiesJson = await DmlTools.ListEntities();
                                
                                return new CallToolResult
                                {
                                    Content = [new TextContentBlock { Type = "application/json", Text = entitiesJson }]
                                };
                            }

                            throw new McpException($"Unknown tool: '{request.Params?.Name}'");
                        }
                    }
                };
            })
            .WithHttpTransport();

            return services;
        }

        public static IEndpointRouteBuilder MapDabMcp(this IEndpointRouteBuilder endpoints, RuntimeConfigProvider runtimeConfigProvider, [StringSyntax("Route")] string pattern = "")
        {
            if (!Debugger.IsAttached)
            {
                Debugger.Launch(); // Forces Visual Studio/VS Code to attach
            }

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
