using System.Collections;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.AuthenticationHelpers.AuthenticationSimulator;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Telemetry;
using Azure.DataApiBuilder.Mcp.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// MCP stdio server:
    /// - Reads JSON-RPC requests (initialize, listTools, callTool) from STDIN
    /// - Writes ONLY MCP JSON responses to STDOUT
    /// - Writes diagnostics to STDERR (so STDOUT remains “pure MCP”)
    /// </summary>
    public class McpStdioServer : IMcpStdioServer
    {
        private readonly McpToolRegistry _toolRegistry;
        private readonly IServiceProvider _serviceProvider;
        private readonly McpStdoutWriter _stdoutWriter;
        private readonly TextReader? _inputReader;
        private readonly string _protocolVersion;

        private const int MAX_LINE_LENGTH = 1024 * 1024; // 1 MB limit for incoming JSON-RPC requests

        // Omit null-valued properties (e.g. SDK ContentBlock.Annotations, ContentBlock._meta) so
        // strict MCP clients never see explicit JSON nulls for optional metadata fields.
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public McpStdioServer(McpToolRegistry toolRegistry, IServiceProvider serviceProvider, TextReader? inputReader = null)
        {
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _inputReader = inputReader;

            // Resolve the shared stdout writer so JSON-RPC responses and
            // notifications/message frames are serialized through one lock.
            // Falls back to a fresh instance if DI didn't register one (defensive).
            _stdoutWriter = _serviceProvider.GetService<McpStdoutWriter>() ?? new McpStdoutWriter();

            // Allow protocol version to be configured via IConfiguration, using centralized defaults.
            IConfiguration? configuration = _serviceProvider.GetService<IConfiguration>();
            _protocolVersion = McpProtocolDefaults.ResolveProtocolVersion(configuration);
        }

        /// <summary>
        /// Runs the MCP stdio server loop, reading JSON-RPC requests from STDIN and writing MCP JSON responses to STDOUT.
        /// </summary>
        /// <param name="cancellationToken">Token to signal cancellation of the server loop.</param>
        /// <returns>A task representing the asynchronous operation.</returns>
        public async Task RunAsync(CancellationToken cancellationToken)
        {
            // By default read via Console.In so the loop honors the configured
            // Console.InputEncoding in stdio mode.
            TextReader reader = _inputReader ?? Console.In;

            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(cancellationToken);

                // EOF (stdin pipe closed) is a normal shutdown signal for stdio mode.
                if (line is null)
                {
                    return;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                if (line.Length > MAX_LINE_LENGTH)
                {
                    WriteError(id: null, code: McpStdioJsonRpcErrorCodes.INVALID_REQUEST, message: "Request too large");
                    continue;
                }

                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(line);
                }
                catch (JsonException)
                {
                    WriteError(id: null, code: McpStdioJsonRpcErrorCodes.PARSE_ERROR, message: "Parse error");
                    continue;
                }
                catch (Exception)
                {
                    WriteError(id: null, code: McpStdioJsonRpcErrorCodes.INTERNAL_ERROR, message: "Internal error");
                    continue;
                }

                using (doc)
                {
                    JsonElement root = doc.RootElement;

                    JsonElement? id = null;
                    if (root.TryGetProperty("id", out JsonElement idEl))
                    {
                        id = idEl; // preserve original type (string or number)
                    }

                    if (!root.TryGetProperty("method", out JsonElement methodEl))
                    {
                        WriteError(id, McpStdioJsonRpcErrorCodes.INVALID_REQUEST, "Invalid Request");
                        continue;
                    }

                    string method = methodEl.GetString() ?? string.Empty;

                    try
                    {
                        switch (method)
                        {
                            case "initialize":
                                HandleInitialize(id, root);
                                break;

                            case "notifications/initialized":
                                break;

                            case "tools/list":
                                HandleListTools(id);
                                break;

                            case "tools/call":
                                await HandleCallToolAsync(id, root, cancellationToken);
                                break;

                            case "ping":
                                WriteResult(id, new { ok = true });
                                break;

                            case "logging/setLevel":
                                HandleSetLogLevel(id, root);
                                break;

                            case "shutdown":
                                WriteResult(id, new { ok = true });
                                return;

                            default:
                                WriteError(id, McpStdioJsonRpcErrorCodes.METHOD_NOT_FOUND, $"Method not found: {method}");
                                break;
                        }
                    }
                    catch (Exception)
                    {
                        WriteError(id, McpStdioJsonRpcErrorCodes.INTERNAL_ERROR, "Internal error");
                    }
                }
            }
        }

        /// <summary>
        /// Handles the "initialize" JSON-RPC method by sending the MCP protocol version, server capabilities, and server info to the client.
        /// </summary>
        /// <param name="id">
        /// The request identifier extracted from the incoming JSON-RPC request. Used to correlate the response with the request.
        /// </param>
        /// <param name="root">The incoming initialize request payload.</param>
        /// <remarks>
        /// This method constructs and writes the MCP "initialize" response to STDOUT. It negotiates the response protocol version from the
        /// server-supported version and client-requested version, and includes supported capabilities and server information. No notifications
        /// are sent here; the server waits for the client to send "notifications/initialized" before sending any notifications.
        /// </remarks>
        private void HandleInitialize(JsonElement? id, JsonElement root)
        {
            string? clientRequestedProtocolVersion = GetClientProtocolVersion(root);
            string negotiatedProtocolVersion =
                McpProtocolDefaults.ResolveInitializeResponseProtocolVersion(_protocolVersion, clientRequestedProtocolVersion);

            // Get the description from runtime config if available
            string? description = null;
            RuntimeConfigProvider? runtimeConfigProvider = _serviceProvider.GetService<RuntimeConfigProvider>();
            if (runtimeConfigProvider != null)
            {
                try
                {
                    RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();
                    description = runtimeConfig.Runtime?.Mcp?.Description;
                }
                catch (Exception)
                {
                    // Rethrow to avoid masking configuration errors
                    throw;
                }
            }

            bool shouldUseServerInfoDescription = McpProtocolDefaults.ShouldUseServerInfoDescription(negotiatedProtocolVersion);

            // Create the initialize response - only include description/instructions if non-empty
            object result;
            if (!string.IsNullOrWhiteSpace(description) && shouldUseServerInfoDescription)
            {
                result = new
                {
                    protocolVersion = negotiatedProtocolVersion,
                    capabilities = new
                    {
                        tools = new { listChanged = true },
                        logging = new { }
                    },
                    serverInfo = new
                    {
                        name = McpProtocolDefaults.MCP_SERVER_NAME,
                        version = McpProtocolDefaults.MCP_SERVER_VERSION,
                        description = description
                    }
                };
            }
            else if (!string.IsNullOrWhiteSpace(description))
            {
                result = new
                {
                    protocolVersion = negotiatedProtocolVersion,
                    capabilities = new
                    {
                        tools = new { listChanged = true },
                        logging = new { }
                    },
                    serverInfo = new
                    {
                        name = McpProtocolDefaults.MCP_SERVER_NAME,
                        version = McpProtocolDefaults.MCP_SERVER_VERSION
                    },
                    instructions = description
                };
            }
            else
            {
                result = new
                {
                    protocolVersion = negotiatedProtocolVersion,
                    capabilities = new
                    {
                        tools = new { listChanged = true },
                        logging = new { }
                    },
                    serverInfo = new
                    {
                        name = McpProtocolDefaults.MCP_SERVER_NAME,
                        version = McpProtocolDefaults.MCP_SERVER_VERSION
                    }
                };
            }

            WriteResult(id, result);
        }

        private static string? GetClientProtocolVersion(JsonElement root)
        {
            if (!root.TryGetProperty("params", out JsonElement paramsElement) || paramsElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!paramsElement.TryGetProperty("protocolVersion", out JsonElement protocolVersionElement) ||
                protocolVersionElement.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return protocolVersionElement.GetString();
        }

        /// <summary>
        /// Handles the "tools/list" JSON-RPC method by sending the list of available tools to the client.
        /// </summary>
        /// <param name="id">
        /// The request identifier extracted from the incoming JSON-RPC request. Used to correlate the response with the request.
        /// </param>
        private void HandleListTools(JsonElement? id)
        {
            List<object> toolsWire = new();
            int count = 0;

            // Resolve runtime config to filter out disabled tools.
            RuntimeConfigProvider runtimeConfigProvider = _serviceProvider.GetRequiredService<RuntimeConfigProvider>();
            RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();
            IEnumerable<Tool> tools = _toolRegistry.GetEnabledTools(runtimeConfig);

            foreach (Tool tool in tools)
            {
                count++;
                toolsWire.Add(new
                {
                    name = tool.Name,
                    description = tool.Description,
                    inputSchema = tool.InputSchema
                });
            }

            WriteResult(id, new { tools = toolsWire });
        }

        /// <summary>
        /// Handles the "logging/setLevel" JSON-RPC method by updating the runtime log level.
        /// </summary>
        /// <param name="id">The request identifier extracted from the incoming JSON-RPC request.</param>
        /// <param name="root">The root JSON element of the incoming JSON-RPC request.</param>
        /// <remarks>
        /// Log level precedence (highest to lowest):
        /// 1. MCP <c>logging/setLevel</c> (Agent) - always wins, overrides CLI and Config.
        /// 2. CLI <c>--log-level</c> flag.
        /// 3. Config <c>runtime.telemetry.log-level</c>.
        /// 4. Default: <c>None</c> for MCP stdio mode (silent by default to keep stdout clean for JSON-RPC),
        ///    <c>Error</c> in Production, <c>Debug</c> in Development.
        ///
        /// Per MCP spec the response is always success (empty result object) even when the input is
        /// an unrecognized level — in that case no side effect runs and no state changes.
        ///
        /// Side effects performed in order on a valid request:
        /// 1. Toggle <see cref="IMcpLogNotificationWriter.IsEnabled"/> based on the level
        ///    (<c>"none"</c> disables, anything else enables). This is done BEFORE
        ///    <see cref="ILogLevelController.UpdateFromMcp"/> so the audit log line that
        ///    <c>UpdateFromMcp</c> emits is forwarded to the agent rather than dropped.
        /// 2. Call <see cref="ILogLevelController.UpdateFromMcp"/>, which updates the level and
        ///    flips <see cref="ILogLevelController.IsAgentOverriding"/> so subsequent runtime-config
        ///    hot-reloads do not overwrite the agent's choice.
        /// 3. Restore <see cref="Console.Error"/> to the real stderr stream when logging is enabled,
        ///    in case startup redirected it to <see cref="TextWriter.Null"/> (default for
        ///    <c>--mcp-stdio</c> or <c>--log-level none</c>).
        /// </remarks>
        private void HandleSetLogLevel(JsonElement? id, JsonElement root)
        {
            // Extract the level parameter from the request
            string? level = null;
            if (root.TryGetProperty("params", out JsonElement paramsEl) &&
                paramsEl.TryGetProperty("level", out JsonElement levelEl) &&
                levelEl.ValueKind == JsonValueKind.String)
            {
                level = levelEl.GetString();
            }

            if (string.IsNullOrWhiteSpace(level))
            {
                WriteError(id, McpStdioJsonRpcErrorCodes.INVALID_PARAMS, "Missing or invalid 'level' parameter");
                return;
            }

            // Get the ILogLevelController from service provider
            ILogLevelController? logLevelController = _serviceProvider.GetService<ILogLevelController>();
            if (logLevelController is null)
            {
                // Log level controller not available - still accept request per MCP spec
                WriteResult(id, new { });
                return;
            }

            // Validate the level BEFORE touching any side-effect (notification writer, stderr).
            // "none" is the disable signal and is not a recognized MCP level; everything else
            // must round-trip through McpLogLevelConverter so a typo can't silently turn the
            // notification stream on while UpdateFromMcp ignores the bad value.
            bool isDisableRequest = string.Equals(level, "none", StringComparison.OrdinalIgnoreCase);
            bool isValidLevel = isDisableRequest || McpLogLevelConverter.TryConvertFromMcp(level, out _);
            if (!isValidLevel)
            {
                // Unknown level - return success per MCP spec but make no state changes.
                WriteResult(id, new { });
                return;
            }

            bool isLoggingEnabled = !isDisableRequest;

            // Enable or disable MCP log notifications based on the requested level BEFORE updating
            // the level. Doing it in this order means the agent-override Information line emitted
            // by UpdateFromMcp is forwarded to the agent (otherwise it would be dropped because
            // the notification writer was still disabled at the moment of emission).
            IMcpLogNotificationWriter? notificationWriter = _serviceProvider.GetService<IMcpLogNotificationWriter>();
            if (notificationWriter != null)
            {
                notificationWriter.IsEnabled = isLoggingEnabled;
            }

            // Update the log level. Validation above guarantees this returns true for non-"none"
            // values; for "none" it returns false (no LogLevel mapping) and we just keep
            // notifications off without touching the current level.
            bool updated = logLevelController.UpdateFromMcp(level);

            // Restore stderr if the agent successfully turned logging on. When `--mcp-stdio` (or
            // `--log-level none`) was the startup default, stderr was redirected to TextWriter.Null;
            // re-enable it now so subsequent logs flow.
            if (updated && isLoggingEnabled)
            {
                RestoreStderrIfNeeded();
            }

            // Always return success (empty result object) per MCP spec
            WriteResult(id, new { });
        }

        /// <summary>
        /// Restores Console.Error to the real stderr stream if it was redirected to TextWriter.Null.
        /// This enables log output after MCP client sends logging/setLevel with a level other than "none".
        /// </summary>
        private static void RestoreStderrIfNeeded()
        {
            // Always restore stderr to the real stream when MCP enables logging.
            // This is safe to call multiple times - we just re-wrap the standard error stream.
            Stream stderr = Console.OpenStandardError();
            StreamWriter stderrWriter = new(stderr, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true
            };
            Console.SetError(stderrWriter);
        }

        /// <summary>
        /// Handles the "tools/call" JSON-RPC method by executing the specified tool with the provided arguments.
        /// </summary>
        /// <param name="id"> The request identifier extracted from the incoming JSON-RPC request. Used to correlate the response with the request.</param>
        /// <param name="root"> The root JSON element of the incoming JSON-RPC request.</param>
        /// <param name="ct"> Cancellation token to signal operation cancellation.</param>
        private async Task HandleCallToolAsync(JsonElement? id, JsonElement root, CancellationToken ct)
        {
            if (!root.TryGetProperty("params", out JsonElement @params) || @params.ValueKind != JsonValueKind.Object)
            {
                WriteError(id, McpStdioJsonRpcErrorCodes.INVALID_PARAMS, "Missing params");
                return;
            }

            // If neither params.name (the MCP-standard field for the tool identifier)
            // nor the legacy params.tool field is present or non-empty, we cannot tell
            // which tool to execute. In that case we log a debug message to STDERR for
            // diagnostics and return a JSON-RPC error (-32602 "Missing tool name") to
            // the MCP client so it can fix the request payload.
            string? toolName = null;
            if (@params.TryGetProperty("name", out JsonElement nameEl) && nameEl.ValueKind == JsonValueKind.String)
            {
                toolName = nameEl.GetString();
            }
            else if (@params.TryGetProperty("tool", out JsonElement toolEl) && toolEl.ValueKind == JsonValueKind.String)
            {
                toolName = toolEl.GetString();
            }

            if (string.IsNullOrWhiteSpace(toolName))
            {
                WriteError(id, McpStdioJsonRpcErrorCodes.INVALID_PARAMS, "Missing tool name");
                return;
            }

            if (!_toolRegistry.TryGetTool(toolName!, out IMcpTool? tool) || tool is null)
            {
                WriteError(id, McpStdioJsonRpcErrorCodes.INVALID_PARAMS, $"Tool not found: {toolName}");
                return;
            }

            JsonDocument? argsDoc = null;
            try
            {
                if (@params.TryGetProperty("arguments", out JsonElement argsEl) && argsEl.ValueKind == JsonValueKind.Object)
                {
                    argsDoc = JsonDocument.Parse(argsEl.GetRawText());
                }

                // Execute the tool with telemetry.
                // If a MCP stdio role override is set in the environment, create
                // a request HttpContext with the X-MS-API-ROLE header so tools and authorization
                // helpers that read IHttpContextAccessor will see the role. We also ensure the
                // Simulator authentication handler can authenticate the user by flowing the
                // Authorization header commonly used in tests/simulator scenarios.
                CallToolResult callResult;
                IConfiguration? configuration = _serviceProvider.GetService<IConfiguration>();
                string? stdioRole = configuration?.GetValue<string>("MCP:Role");
                if (!string.IsNullOrWhiteSpace(stdioRole))
                {
                    IServiceScopeFactory scopeFactory = _serviceProvider.GetRequiredService<IServiceScopeFactory>();
                    using IServiceScope scope = scopeFactory.CreateScope();
                    IServiceProvider scopedProvider = scope.ServiceProvider;

                    // Create a default HttpContext and set the client role header
                    DefaultHttpContext httpContext = new();
                    httpContext.Request.Headers["X-MS-API-ROLE"] = stdioRole;

                    // Build a simulator-style identity with the given role
                    ClaimsIdentity identity = new(
                        authenticationType: SimulatorAuthenticationDefaults.AUTHENTICATIONSCHEME);
                    identity.AddClaim(new Claim(ClaimTypes.Role, stdioRole));
                    httpContext.User = new ClaimsPrincipal(identity);

                    // If IHttpContextAccessor is registered, populate it for downstream code.
                    IHttpContextAccessor? httpContextAccessor = scopedProvider.GetService<IHttpContextAccessor>();
                    if (httpContextAccessor is not null)
                    {
                        httpContextAccessor.HttpContext = httpContext;
                    }

                    try
                    {
                        // Execute the tool with the scoped service provider so any scoped services resolve correctly.
                        callResult = await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                            tool, toolName!, argsDoc, scopedProvider, ct);
                    }
                    finally
                    {
                        // Clear the accessor's HttpContext to avoid leaking across calls
                        if (httpContextAccessor is not null)
                        {
                            httpContextAccessor.HttpContext = null;
                        }
                    }
                }
                else
                {
                    callResult = await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                        tool, toolName!, argsDoc, _serviceProvider, ct);
                }

                await HandleCallToolAsync(id ?? default, callResult);
            }
            finally
            {
                argsDoc?.Dispose();
            }
        }

        /// <summary>
        /// Writes the JSON-RPC result for a completed tool call, propagating <see cref="CallToolResult.IsError"/>
        /// to the wire so MCP clients can distinguish tool errors from successes.
        /// Extracted as a separate overload so it can be exercised directly in unit tests.
        /// </summary>
        /// <param name="id">The request identifier used to correlate the response.</param>
        /// <param name="callResult">The result returned by the tool execution.</param>
        private Task HandleCallToolAsync(JsonElement id, CallToolResult callResult)
        {
            // Normalize to MCP content blocks (array). We try to pass through if a 'Content' property exists,
            // otherwise we wrap into a single text block.
            object[] content = CoerceToMcpContentBlocks(callResult);

            // Propagate isError so MCP clients can distinguish tool errors from successes.
            // _jsonOptions has WhenWritingNull, so a null isError is omitted from the wire.
            bool? isError = callResult.IsError;
            if (isError == true)
            {
                WriteResult(id, new { content, isError });
            }
            else
            {
                WriteResult(id, new { content });
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Coerces the call result into an array of MCP content blocks.
        /// Tools can either return a custom object with a public "Content" property
        /// or a raw value; this helper normalizes both patterns into the MCP wire format.
        /// </summary>
        /// <param name="callResult">The result object returned from a tool execution.</param>
        /// <returns>An array of content blocks suitable for MCP output.</returns>
        private static object[] CoerceToMcpContentBlocks(object? callResult)
        {
            if (callResult is null)
            {
                return Array.Empty<object>();
            }

            // Prefer a public instance "Content" property if present.
            PropertyInfo? prop = callResult.GetType().GetProperty("Content", BindingFlags.Instance | BindingFlags.Public);

            if (prop is not null)
            {
                object? value = prop.GetValue(callResult);

                if (value is IEnumerable enumerable && value is not string)
                {
                    List<object> list = new();
                    foreach (object item in enumerable)
                    {
                        if (item is string s)
                        {
                            list.Add(new { type = "text", text = s });
                        }
                        else if (item is JsonElement jsonEl)
                        {
                            list.Add(new { type = "application/json", data = jsonEl });
                        }
                        else
                        {
                            list.Add(item);
                        }
                    }

                    return list.ToArray();
                }

                if (value is string sContent)
                {
                    return new object[] { new { type = "text", text = sContent } };
                }

                if (value is JsonElement jsonContent)
                {
                    return new object[] { new { type = "application/json", data = jsonContent } };
                }
            }

            // If callResult itself is a JsonElement, treat it as application/json.
            if (callResult is JsonElement jsonResult)
            {
                return new object[] { new { type = "application/json", data = jsonResult } };
            }

            // Fallback: serialize to text.
            string text = SafeToString(callResult);
            return new object[] { new { type = "text", text } };
        }

        /// <summary>
        /// Safely converts an object to its string representation, preferring JSON serialization for readability.
        /// </summary>
        /// <param name="obj">The object to convert to a string.</param>
        /// <returns>A string representation of the object.</returns>
        private static string SafeToString(object obj)
        {
            try
            {
                // Try JSON first for readability
                string json = JsonSerializer.Serialize(obj);

                // If JSON is extremely large, truncate to avoid flooding MCP output.
                // 32 KB is large enough to show useful JSON detail for diagnostics
                // without flooding MCP output or impacting performance.
                const int MAX_JSON_PREVIEW_CHARS = 32 * 1024; // 32 KB

                if (json.Length > MAX_JSON_PREVIEW_CHARS)
                {
                    return string.Concat(json.AsSpan(0, MAX_JSON_PREVIEW_CHARS), $"... [truncated, total length={json.Length} chars]");
                }

                return json;
            }
            catch
            {
                return obj.ToString() ?? string.Empty;
            }
        }

        /// <summary>
        /// Writes a JSON-RPC result response to the standard output.
        /// Routed through <see cref="McpStdoutWriter"/> so the write is serialized
        /// with notifications/message frames from the logging pipeline.
        /// </summary>
        /// <param name="id">The request identifier extracted from the incoming JSON-RPC request. Used to correlate the response with the request.</param>
        /// <param name="resultObject">The result object to include in the response.</param>
        private void WriteResult(JsonElement? id, object resultObject)
        {
            var response = new
            {
                jsonrpc = McpStdioJsonRpcErrorCodes.JSON_RPC_VERSION,
                id = id.HasValue ? GetIdValue(id.Value) : null,
                result = resultObject
            };

            _stdoutWriter.WriteLine(JsonSerializer.Serialize(response, _jsonOptions));
        }

        /// <summary>
        /// Writes a JSON-RPC error response to the standard output.
        /// Routed through <see cref="McpStdoutWriter"/> so the write is serialized
        /// with notifications/message frames from the logging pipeline.
        /// </summary>
        /// <param name="id">The request identifier extracted from the incoming JSON-RPC request. Used to correlate the response with the request.</param>
        /// <param name="code">The error code.</param>
        /// <param name="message">The error message.</param>
        private void WriteError(JsonElement? id, int code, string message)
        {
            var errorObj = new
            {
                jsonrpc = McpStdioJsonRpcErrorCodes.JSON_RPC_VERSION,
                id = id.HasValue ? GetIdValue(id.Value) : null,
                error = new { code, message }
            };

            _stdoutWriter.WriteLine(JsonSerializer.Serialize(errorObj));
        }

        /// <summary>
        /// Extracts the value of a JSON-RPC request identifier.
        /// </summary>
        /// <param name="id">The JSON element representing the request identifier.</param>
        /// <returns>The extracted identifier value as an object, or null if the identifier is not a primitive type.</returns>
        private static object? GetIdValue(JsonElement id)
        {
            return id.ValueKind switch
            {
                JsonValueKind.String => id.GetString(),
                JsonValueKind.Number => id.TryGetInt64(out long l) ? l :
                                        id.TryGetDouble(out double d) ? d : null,
                _ => null
            };
        }
    }
}
