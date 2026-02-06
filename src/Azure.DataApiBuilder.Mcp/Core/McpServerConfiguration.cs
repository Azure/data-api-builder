// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
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
                options.ServerInfo = new() { Name = McpProtocolDefaults.MCP_SERVER_NAME, Version = McpProtocolDefaults.MCP_SERVER_VERSION };
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

                            // Start OpenTelemetry activity for MCP tool execution
                            using Activity? activity = TelemetryTracesHelper.DABActivitySource.StartActivity("mcp.tool.execute");

                            JsonDocument? arguments = null;
                            try
                            {
                                // Extract entity name from arguments for telemetry
                                string? entityName = null;
                                string? operation = null;
                                string? dbProcedure = null;

                                if (request.Params?.Arguments != null)
                                {
                                    // Convert IReadOnlyDictionary<string, JsonElement> to JsonDocument
                                    Dictionary<string, object?> jsonObject = new();
                                    foreach (KeyValuePair<string, JsonElement> kvp in request.Params.Arguments)
                                    {
                                        jsonObject[kvp.Key] = kvp.Value;

                                        // Extract entity name if present
                                        if (kvp.Key == "entity" && kvp.Value.ValueKind == JsonValueKind.String)
                                        {
                                            entityName = kvp.Value.GetString();
                                        }
                                    }

                                    string json = JsonSerializer.Serialize(jsonObject);
                                    arguments = JsonDocument.Parse(json);
                                }

                                // Determine operation based on tool name
                                operation = McpTelemetryHelper.InferOperationFromToolName(toolName);

                                // For custom tools (DynamicCustomTool), extract stored procedure information
                                if (tool is DynamicCustomTool customTool)
                                {
                                    // Get entity name and procedure from the custom tool
                                    (entityName, dbProcedure) = McpTelemetryHelper.ExtractCustomToolMetadata(customTool, request.Services!);
                                }

                                // Track the start of MCP tool execution with telemetry
                                activity?.TrackMcpToolExecutionStarted(
                                    toolName: toolName,
                                    entityName: entityName,
                                    operation: operation,
                                    dbProcedure: dbProcedure);

                                // Execute the tool
                                CallToolResult result = await tool!.ExecuteAsync(arguments, request.Services!, ct);

                                // Track successful completion
                                activity?.TrackMcpToolExecutionFinished();

                                return result;
                            }
                            catch (Exception ex)
                            {
                                // Track exception in telemetry with specific error code based on exception type
                                string errorCode = ex switch
                                {
                                    OperationCanceledException => McpTelemetryErrorCodes.OPERATION_CANCELLED,
                                    UnauthorizedAccessException => McpTelemetryErrorCodes.AUTHENTICATION_FAILED,
                                    System.Data.Common.DbException => McpTelemetryErrorCodes.DATABASE_ERROR,
                                    ArgumentException => McpTelemetryErrorCodes.INVALID_REQUEST,
                                    _ => McpTelemetryErrorCodes.EXECUTION_FAILED
                                };

                                activity?.TrackMcpToolExecutionFinishedWithException(ex, errorCode: errorCode);
                                throw;
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
