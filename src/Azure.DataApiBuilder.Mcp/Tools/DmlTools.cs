// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using System.Diagnostics;
using HotChocolate.Execution;
using HotChocolate;
using Microsoft.Extensions.DependencyInjection;

namespace Azure.DataApiBuilder.Mcp.Tools;

[McpServerToolType]
public static class DmlTools
{
    private static readonly ILogger _logger;

    static DmlTools()
    {
        _logger = LoggerFactory.Create(builder =>
        {
            builder.AddConsole();
        }).CreateLogger(nameof(DmlTools));
    }

    [McpServerTool]
    public static string Echo(string message)
    {
        _logger.LogInformation("Echo tool called with message: {message}", message);
        using (Activity activity = new("MCP"))
        {
            activity.SetTag("tool", nameof(Echo));
            return message;
        }
    }

    [McpServerTool]
    public static async Task<string> GetGraphQLSchema(IServiceProvider services)
    {
        _logger.LogInformation("GetGraphQLSchema tool called");
        
        using (Activity activity = new("MCP"))
        {
            activity.SetTag("tool", nameof(GetGraphQLSchema));
            
            try
            {
                // Get the GraphQL request executor resolver from services
                IRequestExecutorResolver? requestExecutorResolver = services.GetService(typeof(IRequestExecutorResolver)) as IRequestExecutorResolver;
                
                if (requestExecutorResolver == null)
                {
                    _logger.LogWarning("IRequestExecutorResolver not found in service container");
                    return "IRequestExecutorResolver not available";
                }

                // Get the GraphQL request executor
                IRequestExecutor requestExecutor = await requestExecutorResolver.GetRequestExecutorAsync();
                
                // Get the schema from the request executor
                ISchema schema = requestExecutor.Schema;
                
                // Return the schema as SDL (Schema Definition Language)
                string schemaString = schema.ToString();
                
                _logger.LogInformation("Successfully retrieved GraphQL schema with {length} characters", schemaString.Length);
                
                return schemaString;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to retrieve GraphQL schema");
                return $"Error retrieving GraphQL schema: {ex.Message}";
            }
        }
    }
}
