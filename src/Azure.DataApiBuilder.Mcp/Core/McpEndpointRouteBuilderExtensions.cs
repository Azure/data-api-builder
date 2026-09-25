// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Mcp.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// Extension methods for mapping MCP endpoints to an <see cref="IEndpointRouteBuilder"/>.
    /// </summary>
    public static class McpEndpointRouteBuilderExtensions
    {
        /// <summary>
        /// Maps the MCP endpoint to the specified <see cref="IEndpointRouteBuilder"/> if MCP is enabled in the runtime configuration.
        /// </summary>
        public static IEndpointRouteBuilder MapDabMcp(
            this IEndpointRouteBuilder endpoints,
            RuntimeConfigProvider runtimeConfigProvider,
            [StringSyntax("Route")] string pattern = "")
        {
            if (!runtimeConfigProvider.TryGetConfig(out RuntimeConfig? runtimeConfig))
            {
                return endpoints;
            }

            McpRuntimeOptions mcpOptions = runtimeConfig?.Runtime?.Mcp ?? new McpRuntimeOptions();

            if (!mcpOptions.Enabled)
            {
                return endpoints;
            }

            string mcpPath = mcpOptions.Path ?? McpRuntimeOptions.DEFAULT_PATH;

            // Map the MCP endpoint
            endpoints.MapMcp(mcpPath).Add(builder =>
            {
                RequestDelegate? next = builder.RequestDelegate;
                if (next is null)
                {
                    return;
                }

                builder.RequestDelegate = async context =>
                {
                    if (context.RequestServices.GetService<EngineTelemetrySession>()?.IsEnabled != true)
                    {
                        await next(context);
                        return;
                    }

                    Stream original = context.Response.Body;
                    using McpProductResponseStream observer = new(original);
                    context.Response.Body = observer;
                    try
                    {
                        await next(context);
                    }
                    finally
                    {
                        context.Response.Body = original;
                    }
                };
            });

            return endpoints;
        }
    }
}
