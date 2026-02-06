// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry;
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
                                operation = InferOperationFromToolName(toolName);

                                // For custom tools (DynamicCustomTool), extract stored procedure information
                                if (tool is DynamicCustomTool customTool)
                                {
                                    // Get entity name and procedure from the custom tool
                                    (entityName, dbProcedure) = ExtractCustomToolMetadata(customTool, request.Services!);
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
                                // Track exception in telemetry
                                activity?.TrackMcpToolExecutionFinishedWithException(ex, errorCode: "ExecutionFailed");
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

        /// <summary>
        /// Infers the operation type from the tool name.
        /// </summary>
        /// <param name="toolName">The name of the tool.</param>
        /// <returns>The inferred operation type.</returns>
        private static string InferOperationFromToolName(string toolName)
        {
            return toolName.ToLowerInvariant() switch
            {
                string s when s.Contains("read") || s.Contains("get") || s.Contains("list") || s.Contains("describe") => "read",
                string s when s.Contains("create") || s.Contains("insert") => "create",
                string s when s.Contains("update") || s.Contains("modify") => "update",
                string s when s.Contains("delete") || s.Contains("remove") => "delete",
                string s when s.Contains("execute") => "execute",
                _ => "execute"
            };
        }

        /// <summary>
        /// Extracts metadata from a custom tool for telemetry purposes.
        /// </summary>
        /// <param name="customTool">The custom tool instance.</param>
        /// <param name="serviceProvider">The service provider.</param>
        /// <returns>A tuple containing the entity name and database procedure name.</returns>
        private static (string? entityName, string? dbProcedure) ExtractCustomToolMetadata(DynamicCustomTool customTool, IServiceProvider serviceProvider)
        {
            try
            {
                // Use reflection to access private fields since DynamicCustomTool doesn't expose these publicly
                Type type = customTool.GetType();
                System.Reflection.FieldInfo? entityNameField = type.GetField("_entityName", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                System.Reflection.FieldInfo? entityField = type.GetField("_entity", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                string? entityName = entityNameField?.GetValue(customTool) as string;
                object? entity = entityField?.GetValue(customTool);

                if (entity != null && entityName != null)
                {
                    // Try to get the stored procedure name from the entity
                    RuntimeConfigProvider? runtimeConfigProvider = serviceProvider.GetService<RuntimeConfigProvider>();
                    if (runtimeConfigProvider != null)
                    {
                        RuntimeConfig config = runtimeConfigProvider.GetConfig();
                        if (config.Entities.TryGetValue(entityName, out Entity? entityConfig))
                        {
                            string? dbProcedure = entityConfig.Source.Object;
                            return (entityName, dbProcedure);
                        }
                    }
                }

                return (entityName, null);
            }
            catch
            {
                // If reflection fails, return null values
                return (null, null);
            }
        }
    }
}
