// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

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
                string? operation = InferOperationFromTool(tool, toolName);
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
                    // Extract error code and message from the result content
                    (string? errorCode, string? errorMessage) = ExtractErrorFromCallToolResult(result);
                    
                    activity?.SetStatus(ActivityStatusCode.Error, errorMessage ?? "Tool returned an error result");
                    activity?.SetTag("mcp.tool.error", true);
                    
                    if (!string.IsNullOrEmpty(errorCode))
                    {
                        activity?.SetTag("error.code", errorCode);
                    }
                    
                    if (!string.IsNullOrEmpty(errorMessage))
                    {
                        activity?.SetTag("error.message", errorMessage);
                    }
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
        /// Infers the operation type from the tool instance and name.
        /// For built-in tools, maps tool name directly to operation.
        /// For custom tools (stored procedures), always returns "execute".
        /// </summary>
        /// <param name="tool">The tool instance.</param>
        /// <param name="toolName">The name of the tool.</param>
        /// <returns>The inferred operation type.</returns>
        public static string InferOperationFromTool(IMcpTool tool, string toolName)
        {
            // Custom tools (stored procedures) are always "execute"
            if (tool.ToolType == ToolType.Custom)
            {
                return "execute";
            }

            // Built-in tools: map tool name to operation
            return toolName.ToLowerInvariant() switch
            {
                "read_records" => "read",
                "create_record" => "create",
                "update_record" => "update",
                "delete_record" => "delete",
                "describe_entities" => "describe",
                "execute_entity" => "execute",
                _ => "execute" // Fallback for any unknown built-in tools
            };
        }

        /// <summary>
        /// Extracts error code and message from a CallToolResult's content.
        /// MCP tools may return errors as JSON with "code" and "message" properties.
        /// </summary>
        /// <param name="result">The tool result to extract error info from.</param>
        /// <returns>A tuple of (errorCode, errorMessage).</returns>
        private static (string? errorCode, string? errorMessage) ExtractErrorFromCallToolResult(CallToolResult result)
        {
            string? errorCode = null;
            string? errorMessage = null;

            if (result.Content != null)
            {
                foreach (ContentBlock block in result.Content)
                {
                    // Check if this is a text block with JSON error information
                    if (block is TextContentBlock textBlock && !string.IsNullOrEmpty(textBlock.Text))
                    {
                        try
                        {
                            using JsonDocument doc = JsonDocument.Parse(textBlock.Text);
                            JsonElement root = doc.RootElement;

                            if (root.TryGetProperty("code", out JsonElement codeEl))
                            {
                                errorCode = codeEl.GetString();
                            }

                            if (root.TryGetProperty("message", out JsonElement msgEl))
                            {
                                errorMessage = msgEl.GetString();
                            }

                            // If we found error info, we can break
                            if (errorCode != null || errorMessage != null)
                            {
                                break;
                            }
                        }
                        catch
                        {
                            // Not JSON or doesn't have expected structure, skip
                        }
                    }
                }
            }

            return (errorCode, errorMessage);
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
                DataApiBuilderException dabEx when dabEx.SubStatusCode == DataApiBuilderException.SubStatusCodes.AuthenticationChallenge
                    => McpTelemetryErrorCodes.AUTHENTICATION_FAILED,
                DataApiBuilderException dabEx when dabEx.SubStatusCode == DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed
                    => McpTelemetryErrorCodes.AUTHORIZATION_FAILED,
                UnauthorizedAccessException => McpTelemetryErrorCodes.AUTHORIZATION_FAILED,
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
        /// Returns best-effort metadata; failures in configuration access must not prevent tool execution.
        /// </summary>
        /// <param name="customTool">The custom tool instance.</param>
        /// <param name="serviceProvider">The service provider.</param>
        /// <returns>A tuple containing the entity name and database procedure name.</returns>
        public static (string? entityName, string? dbProcedure) ExtractCustomToolMetadata(DynamicCustomTool customTool, IServiceProvider serviceProvider)
        {
            // Access public properties instead of reflection
            string? entityName = customTool.EntityName;

            if (entityName == null)
            {
                return (null, null);
            }

            try
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
            catch (Exception)
            {
                // If configuration access fails for any reason (including DataApiBuilderException
                // when runtime config isn't set up), fall back to returning only the entity name.
                // Telemetry metadata extraction is best-effort and must not prevent tool execution.
            }

            return (entityName, null);
        }
    }
}
