// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
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
        /// <param name="writeStdioResponse">For stdio, writes and flushes the response before product request completion.</param>
        /// <param name="sdkResponseItems">SDK-owned nonserialized context for per-message HTTP response completion.</param>
        /// <returns>The result of the tool execution.</returns>
        public static async Task<CallToolResult> ExecuteWithTelemetryAsync(
            IMcpTool tool,
            string toolName,
            JsonDocument? arguments,
            IServiceProvider serviceProvider,
            CancellationToken cancellationToken,
            Func<CallToolResult, Task>? writeStdioResponse = null,
            IDictionary<string, object?>? sdkResponseItems = null)
        {
            using EngineTelemetryRequestScope? request = BeginProductRequest(
                tool, toolName, serviceProvider, writeStdioResponse is not null,
                out EngineTelemetrySession? session, out HttpContext? httpContext, out bool isStdio);
            ProductRequestCompletion? completion = request is null ? null : new(request, sdkResponseItems is null ? httpContext : null);
            McpProductResponseCompletion? sdkCompletion = null;
            if (request is not null && sdkResponseItems is not null)
            {
                sdkCompletion = McpProductResponseCompletion.Attach(sdkResponseItems, request, cancellationToken);
            }

            try
            {
                // Keep the existing customer span and its attributes scoped to tool execution.
                // Product collection does not consume that activity, its errors or its content.
                CallToolResult result;
                using (EngineTelemetryMeasurementScope? operation = request is null ? null : BeginProductOperation(session!, tool, toolName, arguments))
                {
                    try
                    {
                        result = await ExecuteWithCustomerTelemetryAsync(
                            tool, toolName, arguments, serviceProvider, cancellationToken);
                        EngineTelemetryOutcome outcome = ClassifyProductResult(result, cancellationToken);
                        request?.SetOutcome(outcome);
                        operation?.Complete(outcome);
                    }
                    catch (OperationCanceledException)
                    {
                        operation?.Complete(EngineTelemetryOutcome.Canceled);
                        throw;
                    }
                }

                // Logical operation completion precedes transport completion. A failed write
                // must not retroactively turn a successfully executed operation into a failure.

                if (writeStdioResponse is not null)
                {
                    await writeStdioResponse(result);
                    completion?.Complete();
                }
                else if (!isStdio && httpContext is null && sdkResponseItems is null)
                {
                    // An embedded call has no response transport to wait for.
                    completion?.Complete();
                }

                // SDK HTTP completes per message after write/flush; other HTTP callers use
                // OnCompleted. A stdio invocation without its writer must
                // not be counted as served just because tool execution returned a value.
                return result;
            }
            catch (OperationCanceledException)
            {
                sdkCompletion?.Abandon(sdkResponseItems!);
                completion?.Fail(EngineTelemetryOutcome.Canceled);
                throw;
            }
            catch (Exception)
            {
                sdkCompletion?.Abandon(sdkResponseItems!);
                completion?.Fail(cancellationToken.IsCancellationRequested || httpContext?.RequestAborted.IsCancellationRequested == true
                    ? EngineTelemetryOutcome.Canceled
                    : EngineTelemetryOutcome.Failure);
                throw;
            }
        }

        private static async Task<CallToolResult> ExecuteWithCustomerTelemetryAsync(
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

        internal static bool IsProductDataTool(IMcpTool tool, string toolName)
            => ClassifyProductOperation(tool, toolName) != EngineTelemetryOperation.Unknown;

        internal static EngineTelemetryOperation ClassifyProductOperation(IMcpTool tool, string toolName)
        {
            if (tool.ToolType == ToolType.Custom)
            {
                return EngineTelemetryOperation.Execute;
            }

            // Unlike the customer operation label, an unknown built-in is not an execute
            // request. Discovery and JSON-RPC protocol/control messages are not usage.
            if (tool.ToolType != ToolType.BuiltIn)
            {
                return EngineTelemetryOperation.Unknown;
            }

            return toolName.ToLowerInvariant() switch
            {
                "read_records" or "aggregate_records" => EngineTelemetryOperation.Read,
                "create_record" or "update_record" or "delete_record" => EngineTelemetryOperation.Write,
                "execute_entity" => EngineTelemetryOperation.Execute,
                _ => EngineTelemetryOperation.Unknown
            };
        }

        private static EngineTelemetryOutcome ClassifyProductResult(CallToolResult result, CancellationToken cancellationToken)
        {
            if (result.IsError != true)
            {
                return EngineTelemetryOutcome.Success;
            }

            return cancellationToken.IsCancellationRequested
                ? EngineTelemetryOutcome.Canceled
                : EngineTelemetryOutcome.Failure;
        }

        private static EngineTelemetryMeasurementScope? BeginProductOperation(
            EngineTelemetrySession session, IMcpTool tool, string toolName, JsonDocument? arguments)
        {
            try
            {
                // Names are used only by the session's local accepted-config lookup. No name,
                // argument, stored procedure identifier or result is passed to the aggregator.
                string? entityName = null;
                if (tool is DynamicCustomTool customTool)
                {
                    entityName = customTool.EntityName;
                }
                else if (arguments?.RootElement.ValueKind == JsonValueKind.Object)
                {
                    entityName = ExtractEntityNameFromArguments(arguments);
                }

                return session.BeginOperation(entityName, ClassifyProductOperation(tool, toolName));
            }
            catch (Exception)
            {
                // Product enrichment must not change tool validation or execution behavior.
                return null;
            }
        }

        private static EngineTelemetryRequestScope? BeginProductRequest(
            IMcpTool tool,
            string toolName,
            IServiceProvider services,
            bool hasStdioWriter,
            out EngineTelemetrySession? session,
            out HttpContext? httpContext,
            out bool isStdio)
        {
            try
            {
                return BeginProductRequestCore(tool, toolName, services, hasStdioWriter,
                    out session, out httpContext, out isStdio);
            }
            catch (Exception)
            {
                // Optional product dependencies/configuration must not change tool execution
                // or emit failures to the customer's existing telemetry pipeline.
                session = null;
                httpContext = null;
                isStdio = hasStdioWriter;
                return null;
            }
        }

        private static EngineTelemetryRequestScope? BeginProductRequestCore(
            IMcpTool tool,
            string toolName,
            IServiceProvider services,
            bool hasStdioWriter,
            out EngineTelemetrySession? session,
            out HttpContext? httpContext,
            out bool isStdio)
        {
            session = null;
            httpContext = null;
            isStdio = hasStdioWriter;
            if (!IsProductDataTool(tool, toolName))
            {
                return null;
            }

            session = services.GetService<EngineTelemetrySession>()
                ?? services.GetService<RuntimeConfigProvider>()?.ProductTelemetry;
            if (session?.IsEnabled != true)
            {
                return null;
            }

            IConfiguration? configuration = services.GetService<IConfiguration>();
            isStdio |= configuration?.GetValue<bool>("MCP:StdioMode") == true;
            // Stdio's authorization shim is a DefaultHttpContext, NOT a response socket.
            // Do not read its status or register HTTP response callbacks on it.
            if (!isStdio)
            {
                httpContext = services.GetService<IHttpContextAccessor>()?.HttpContext;
            }

            // Preserve HTTP header cardinality: joining multiple values would invent a
            // custom role instead of marking the ambiguous input as unknown.
            StringValues roleHeader = httpContext?.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] ?? StringValues.Empty;
            string? role = isStdio
                ? configuration?.GetValue<string>("MCP:Role")
                : roleHeader.Count == 1 ? roleHeader[0] : null;
            EngineTelemetryRole roleClass = roleHeader.Count > 1
                ? EngineTelemetryRole.Unknown
                : EngineTelemetrySession.ClassifyRole(role, httpContext?.User.Identity?.IsAuthenticated == true);
            EngineTelemetryTransport transport = EngineTelemetryTransport.InProcess;
            if (isStdio)
            {
                transport = EngineTelemetryTransport.Stdio;
            }
            else if (httpContext is not null)
            {
                transport = EngineTelemetryTransport.Http;
            }

            return session.BeginRequest(EngineTelemetryApi.Mcp, transport, roleClass);
        }

        /// <summary>
        /// Owns one request's transport completion, without a per-request event queue or any
        /// names, arguments, error messages or serialized tool results in product telemetry.
        /// </summary>
        private sealed class ProductRequestCompletion
        {
            private readonly EngineTelemetryRequestScope _request;
            private readonly HttpContext? _httpContext;
            private CancellationTokenRegistration _abortRegistration;
            private int _completed;

            internal ProductRequestCompletion(EngineTelemetryRequestScope request, HttpContext? httpContext)
            {
                _request = request;
                _httpContext = httpContext;
                if (httpContext is not null)
                {
                    lock (httpContext)
                    {
                        httpContext.Response.OnCompleted(static state => ((ProductRequestCompletion)state).ResponseCompletedAsync(), this);
                    }

                    _abortRegistration = httpContext.RequestAborted.UnsafeRegister(
                        static state => ((ProductRequestCompletion)state!).Fail(EngineTelemetryOutcome.Canceled), this);
                    if (Volatile.Read(ref _completed) != 0)
                    {
                        _abortRegistration.Unregister();
                    }
                }
            }

            internal void Complete() => Complete(_request.Outcome, httpStatusCode: null);

            internal void Fail(EngineTelemetryOutcome outcome) => Complete(outcome, httpStatusCode: null);

            private Task ResponseCompletedAsync()
            {
                if (Volatile.Read(ref _completed) != 0)
                {
                    return Task.CompletedTask;
                }

                if (_httpContext!.RequestAborted.IsCancellationRequested)
                {
                    Fail(EngineTelemetryOutcome.Canceled);
                }
                else
                {
                    int status = _httpContext.Response.StatusCode;
                    EngineTelemetryOutcome outcome = _request.Outcome;
                    if (outcome is EngineTelemetryOutcome.Success or EngineTelemetryOutcome.Unknown)
                    {
                        if (status >= StatusCodes.Status400BadRequest)
                        {
                            outcome = EngineTelemetryOutcome.Failure;
                        }
                        else if (status >= StatusCodes.Status300MultipleChoices || status < StatusCodes.Status200OK)
                        {
                            outcome = EngineTelemetryOutcome.Unknown;
                        }
                    }

                    Complete(outcome, status);
                }

                return Task.CompletedTask;
            }

            private void Complete(EngineTelemetryOutcome outcome, int? httpStatusCode)
            {
                if (Interlocked.Exchange(ref _completed, 1) == 0)
                {
                    _abortRegistration.Unregister();
                    try
                    {
                        _request.Complete(outcome, httpStatusCode);
                    }
                    catch (Exception)
                    {
                        // Never affect the tool/response or forward product failures to customer telemetry.
                    }
                }
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
                "aggregate_records" => "aggregate",
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
                TimeoutException => McpTelemetryErrorCodes.OPERATION_TIMEOUT,
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
