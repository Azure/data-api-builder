// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Mcp.Utils
{
    /// <summary>
    /// Utility class for MCP telemetry operations.
    /// </summary>
    internal static class McpTelemetryHelper
    {
        /// <summary>
        /// Executes an MCP tool wrapped in an OpenTelemetry activity span.
        /// Handles telemetry attribute extraction, success/failure tracking,
        /// and exception recording with typed error codes.
        /// </summary>
        /// <param name="tool">The MCP tool to execute.</param>
        /// <param name="toolName">The name of the tool being invoked.</param>
        /// <param name="arguments">The parsed JSON arguments for the tool (may be null).</param>
        /// <param name="serviceProvider">The service provider for resolving dependencies.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The result of the tool execution.</returns>
        public static async Task<CallToolResult> ExecuteWithTelemetryAsync(
            IMcpTool tool,
            string toolName,
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken)
        {
            using Activity? activity = TelemetryTracesHelper.DABActivitySource.StartActivity("mcp.tool.execute");

            try
            {
                // Extract telemetry metadata
                string? entityName = ExtractEntityNameFromArguments(arguments);
                string? operation = InferOperationFromToolName(toolName);
                string? dbProcedure = null;

                // For custom tools (DynamicCustomTool), extract stored procedure information
                if (tool is DynamicCustomTool customTool)
                {
                    (entityName, dbProcedure) = ExtractCustomToolMetadata(customTool, serviceProvider);
                }

                // Track the start of MCP tool execution with telemetry
                activity?.TrackMcpToolExecutionStarted(
                    toolName: toolName,
                    entityName: entityName,
                    operation: operation,
                    dbProcedure: dbProcedure);

                // Execute the tool
                CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, cancellationToken);

                // Check if the tool returned an error result (tools catch exceptions internally
                // and return CallToolResult with IsError=true instead of throwing)
                if (result.IsError == true)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "Tool returned an error result");
                    activity?.SetTag("mcp.tool.error", true);
                }
                else
                {
                    // Track successful completion
                    activity?.TrackMcpToolExecutionFinished();
                }

                return result;
            }
            catch (Exception ex)
            {
                // Track exception in telemetry with specific error code based on exception type
                string errorCode = MapExceptionToErrorCode(ex);
                activity?.TrackMcpToolExecutionFinishedWithException(ex, errorCode: errorCode);
                throw;
            }
        }

        /// <summary>
        /// Infers the operation type from the tool name using keyword matching.
        /// Matching follows a fixed precedence order: read > create > update > delete > execute.
        /// The first matching keyword wins. For example, a tool named "get_deleted_items" will be
        /// inferred as "read" (matches "get" before "delete"). Built-in tool names (read_records,
        /// create_record, update_record, delete_record, describe_entities) are unambiguous.
        /// Custom tool names derived from stored procedures may match heuristically.
        /// If no keyword matches, defaults to "execute".
        /// </summary>
        /// <param name="toolName">The name of the tool.</param>
        /// <returns>The inferred operation type.</returns>
        public static string InferOperationFromToolName(string toolName)
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
        /// Maps an exception to a telemetry error code.
        /// </summary>
        /// <param name="ex">The exception to map.</param>
        /// <returns>The corresponding error code string.</returns>
        public static string MapExceptionToErrorCode(Exception ex)
        {
            return ex switch
            {
                OperationCanceledException => McpTelemetryErrorCodes.OPERATION_CANCELLED,
                UnauthorizedAccessException => McpTelemetryErrorCodes.AUTHENTICATION_FAILED,
                System.Data.Common.DbException => McpTelemetryErrorCodes.DATABASE_ERROR,
                ArgumentException => McpTelemetryErrorCodes.INVALID_REQUEST,
                _ => McpTelemetryErrorCodes.EXECUTION_FAILED
            };
        }

        /// <summary>
        /// Extracts the entity name from parsed tool arguments, if present.
        /// </summary>
        /// <param name="arguments">The parsed JSON arguments.</param>
        /// <returns>The entity name, or null if not present.</returns>
        private static string? ExtractEntityNameFromArguments(JsonDocument? arguments)
        {
            if (arguments != null &&
                arguments.RootElement.TryGetProperty("entity", out JsonElement entityEl) &&
                entityEl.ValueKind == JsonValueKind.String)
            {
                return entityEl.GetString();
            }

            return null;
        }

        /// <summary>
        /// Extracts metadata from a custom tool for telemetry purposes.
        /// </summary>
        /// <param name="customTool">The custom tool instance.</param>
        /// <param name="serviceProvider">The service provider.</param>
        /// <returns>A tuple containing the entity name and database procedure name.</returns>
        public static (string? entityName, string? dbProcedure) ExtractCustomToolMetadata(DynamicCustomTool customTool, IServiceProvider serviceProvider)
        {
            try
            {
                // Access public properties instead of reflection
                string? entityName = customTool.EntityName;

                if (entityName != null)
                {
                    // Try to get the stored procedure name from the runtime configuration
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
            catch (Exception ex) when (ex is InvalidOperationException || ex is ArgumentException)
            {
                // If configuration access fails due to invalid state or arguments, return null values
                // This is expected during startup or configuration changes
                return (null, null);
            }
        }
    }
}
