// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Azure.DataApiBuilder.Core.Configurations;
using Google.Protobuf.WellKnownTypes;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Client;
using ModelContextProtocol.Server;

namespace Azure.DataApiBuilder.Mcp
{
    public static class Extensions
    {
        public static IServiceCollection AddDabMcpServer(this IServiceCollection services, RuntimeConfigProvider runtimeConfigProvider)
        {
            services
                .AddMcpServer()
                .WithToolsFromAssembly()
                .WithHttpTransport();
            return services;
        }
       

        public static IEndpointRouteBuilder MapDabMcp(this IEndpointRouteBuilder endpoints, [StringSyntax("Route")] string pattern = "")
        {
            endpoints.MapMcp();
            endpoints.MapHealthChecks("/jerry", new HealthCheckOptions
            {
                ResponseWriter = WriteHealthCheckResponse
            });
            return endpoints;
        }

        private static async Task WriteHealthCheckResponse(HttpContext context, HealthReport report)
        {
            // Get the MCP server endpoint
            IServiceProvider serviceProvider = context.RequestServices;
            HttpRequest? request = serviceProvider.GetService<IHttpContextAccessor>()?.HttpContext?.Request;
            if (request == null)
            {
                throw new Exception();
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
                clientOptions: new McpClientOptions()
                {
                    Capabilities = new() { }
                },
                loggerFactory: null);

            IList<McpClientTool> mcpTools = await mcpClient.ListToolsAsync();
            string[] toolNames = mcpTools.Select(t => t.Name).OrderBy(t => t).ToArray();

            context.Response.ContentType = "application/json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(toolNames, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = true
            }));
        }
    }

    [McpServerToolType]
    public static class DabMcpTools
    {
        [McpServerTool]
        public static string Echo(string message) => message;
    }
}
