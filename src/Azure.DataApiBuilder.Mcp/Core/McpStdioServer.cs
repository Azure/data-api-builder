using System.Collections;
using System.Reflection;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.AuthenticationHelpers.AuthenticationSimulator;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Model;
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
        private readonly string _protocolVersion;

        private const int MAX_LINE_LENGTH = 1024 * 1024; // 1 MB limit for incoming JSON-RPC requests

        public McpStdioServer(McpToolRegistry toolRegistry, IServiceProvider serviceProvider)
        {
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));

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
            Console.Error.WriteLine("[MCP DEBUG] MCP stdio server started.");

            // Use UTF-8 WITHOUT BOM
            UTF8Encoding utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

            using Stream stdin = Console.OpenStandardInput();
            using Stream stdout = Console.OpenStandardOutput();
            using StreamReader reader = new(stdin, utf8NoBom);
            using StreamWriter writer = new(stdout, utf8NoBom) { AutoFlush = true };

            // Redirect Console.Out to use our writer
            Console.SetOut(writer);
            while (!cancellationToken.IsCancellationRequested)
            {
                string? line = await reader.ReadLineAsync(cancellationToken);
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
                catch (JsonException jsonEx)
                {
                    Console.Error.WriteLine($"[MCP DEBUG] JSON parse error: {jsonEx.Message}");
                    WriteError(id: null, code: McpStdioJsonRpcErrorCodes.PARSE_ERROR, message: "Parse error");
                    continue;
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[MCP DEBUG] Unexpected error parsing request: {ex.Message}");
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
                                HandleInitialize(id);
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
        /// <remarks>
        /// This method constructs and writes the MCP "initialize" response to STDOUT. It uses the protocol version defined by <c>PROTOCOL_VERSION</c>
        /// and includes supported capabilities and server information. No notifications are sent here; the server waits for the client to send
        /// "notifications/initialized" before sending any notifications.
        /// </remarks>
        private void HandleInitialize(JsonElement? id)
        {
            // Extract the actual id value from the request
            object? requestId = id.HasValue ? GetIdValue(id.Value) : null;

            // Get the description from runtime config if available
            string? instructions = null;
            RuntimeConfigProvider? runtimeConfigProvider = _serviceProvider.GetService<RuntimeConfigProvider>();
            if (runtimeConfigProvider != null)
            {
                try
                {
                    RuntimeConfig runtimeConfig = runtimeConfigProvider.GetConfig();
                    instructions = runtimeConfig.Runtime?.Mcp?.Description;
                }
                catch (Exception ex)
                {
                    // Log to stderr for diagnostics and rethrow to avoid masking configuration errors
                    Console.Error.WriteLine($"[MCP WARNING] Failed to retrieve MCP description from config: {ex.Message}");
                    throw;
                }
            }

            // Create the initialize response
            object result = new
            {
                protocolVersion = _protocolVersion,
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
                instructions = !string.IsNullOrWhiteSpace(instructions) ? instructions : null
            };

            WriteResult(id, result);
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

            // Tools are expected to be registered during application startup only.
            // If this ever changes and tools can be added/removed at runtime while
            // requests are being handled, we may need to introduce locking here or
            // have the registry return a thread-safe snapshot.
            foreach (Tool tool in _toolRegistry.GetAllTools())
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
                Console.Error.WriteLine("[MCP DEBUG] callTool → missing tool name.");
                WriteError(id, McpStdioJsonRpcErrorCodes.INVALID_PARAMS, "Missing tool name");
                return;
            }

            if (!_toolRegistry.TryGetTool(toolName!, out IMcpTool? tool) || tool is null)
            {
                Console.Error.WriteLine($"[MCP DEBUG] callTool → tool not found: {toolName}");
                WriteError(id, McpStdioJsonRpcErrorCodes.INVALID_PARAMS, $"Tool not found: {toolName}");
                return;
            }

            JsonDocument? argsDoc = null;
            try
            {
                if (@params.TryGetProperty("arguments", out JsonElement argsEl) && argsEl.ValueKind == JsonValueKind.Object)
                {
                    string rawArgs = argsEl.GetRawText();
                    Console.Error.WriteLine($"[MCP DEBUG] callTool → tool: {toolName}, args: {rawArgs}");
                    argsDoc = JsonDocument.Parse(rawArgs);
                }
                else
                {
                    Console.Error.WriteLine($"[MCP DEBUG] callTool → tool: {toolName}, args: <none>");
                }

                // Execute the tool.
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
                        callResult = await tool.ExecuteAsync(argsDoc, scopedProvider, ct);
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
                    callResult = await tool.ExecuteAsync(argsDoc, _serviceProvider, ct);
                }

                // Normalize to MCP content blocks (array). We try to pass through if a 'Content' property exists,
                // otherwise we wrap into a single text block.
                object[] content = CoerceToMcpContentBlocks(callResult);

                WriteResult(id, new { content });
            }
            finally
            {
                argsDoc?.Dispose();
            }
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
        /// </summary>
        /// <param name="id">The request identifier extracted from the incoming JSON-RPC request. Used to correlate the response with the request.</param>
        /// <param name="resultObject">The result object to include in the response.</param>
        private static void WriteResult(JsonElement? id, object resultObject)
        {
            var response = new
            {
                jsonrpc = "2.0",
                id = id.HasValue ? GetIdValue(id.Value) : null,
                result = resultObject
            };

            string json = JsonSerializer.Serialize(response);
            Console.Out.WriteLine(json);
        }

        /// <summary>
        /// Writes a JSON-RPC error response to the standard output.
        /// </summary>
        /// <param name="id">The request identifier extracted from the incoming JSON-RPC request. Used to correlate the response with the request.</param>
        /// <param name="code">The error code.</param>
        /// <param name="message">The error message.</param>
        private static void WriteError(JsonElement? id, int code, string message)
        {
            var errorObj = new
            {
                jsonrpc = "2.0",
                id = id.HasValue ? GetIdValue(id.Value) : null,
                error = new { code, message }
            };

            string json = JsonSerializer.Serialize(errorObj);
            Console.Out.WriteLine(json);
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
