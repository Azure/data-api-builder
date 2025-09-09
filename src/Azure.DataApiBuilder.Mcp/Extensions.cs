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

            // Register domain tools
            services.AddDmlTools(_mcpOptions);

            // Register MCP server with dynamic tool handlers
            services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "MyServer", Version = "1.0.0" };

                options.Capabilities = new ServerCapabilities
                {
                    Tools = new ToolsCapability
                    {
                        ListToolsHandler = (request, ct) =>
                            ValueTask.FromResult(new ListToolsResult
                            {
                                Tools =
                                [
                                    new Tool
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
                                    new Tool
                                    {
                                        Name = "list_entities",
                                        Description = "Lists all entities in the database."
                                    }
                                ]
                            }),

                        CallToolHandler = (request, ct) =>
                        {
                            if (request.Params?.Name == "echonew" &&
                                request.Params.Arguments?.TryGetValue("message", out JsonElement messageEl) == true)
                            {
                                string? msg = messageEl.ValueKind == JsonValueKind.String
                                    ? messageEl.GetString()
                                    : messageEl.ToString();

                                return ValueTask.FromResult(new CallToolResult
                                {
                                    Content = [new TextContentBlock { Type = "text", Text = $"Echo: {msg}" }]
                                });
                            }
                            else if (request.Params?.Name == "list_entities")
                            {
                                // Call the ListEntities tool method from DmlTools
                                Task<string> listEntitiesTask = DmlTools.ListEntities();
                                listEntitiesTask.Wait(); // Wait for the async method to complete
                                string entitiesJson = listEntitiesTask.Result;
                                return ValueTask.FromResult(new CallToolResult
                                {
                                    Content = [new TextContentBlock { Type = "application/json", Text = entitiesJson }]
                                });
                            }

                            throw new McpException($"Unknown tool: '{request.Params?.Name}'");
                        }
                    }
                };
            })
            .WithHttpTransport();

            return services;
        }

        public static IEndpointRouteBuilder MapDabMcp(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern = "")
        {
            endpoints.MapMcp();
            endpoints.MapMcpHealthEndpoint(pattern);
            return endpoints;
        }
    }
}
