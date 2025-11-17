using System.Collections;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Mcp.Model;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Mcp.Core
{
    /// <summary>
    /// MCP stdio server:
    /// - Reads JSON-RPC requests (initialize, listTools, callTool) from STDIN
    /// - Writes ONLY MCP JSON responses to STDOUT (always Flush()!)
    /// - Writes diagnostics to STDERR (so STDOUT remains “pure MCP”)
    /// </summary>
    public class McpStdioServer : IMcpStdioServer
    {
        private readonly McpToolRegistry _toolRegistry;
        private readonly IServiceProvider _serviceProvider;

        private const string PROTOCOL_VERSION = "2025-06-18";

        public McpStdioServer(McpToolRegistry toolRegistry, IServiceProvider serviceProvider)
        {
            _toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        }

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
                string? line = await reader.ReadLineAsync();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                Console.Error.WriteLine($"[MCP DEBUG] Received raw: {line}");

                JsonDocument doc;
                try
                {
                    doc = JsonDocument.Parse(line);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[MCP DEBUG] Parse error: {ex.Message}");
                    WriteError(id: null, code: -32700, message: "Parse error");
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
                        Console.Error.WriteLine("[MCP DEBUG] Invalid Request (no method).");
                        WriteError(id, -32600, "Invalid Request");
                        continue;
                    }

                    string method = methodEl.GetString() ?? string.Empty;
                    //Console.Error.WriteLine($"[MCP DEBUG] Method: {method}, Id: {FormatIdForLog(id)}");

                    try
                    {
                        switch (method)
                        {
                            case "initialize":
                                HandleInitialize(id);
                                break;

                            case "notifications/initialized":
                                Console.Error.WriteLine("[MCP DEBUG] notifications/initialized received.");
                                break;

                            case "tools/list":  // ← Changed from "listTools"
                                Console.Error.WriteLine("[MCP DEBUG] tools/list → received.");
                                HandleListTools(id);
                                Console.Error.WriteLine("[MCP DEBUG] tools/list → OK (tool catalog sent).");
                                break;

                            case "tools/call":  // ← Changed from "callTool"
                                await HandleCallToolAsync(id, root, cancellationToken);
                                Console.Error.WriteLine("[MCP DEBUG] tools/call → OK (tool executed).");
                                break;

                            case "prompts/list":  // ← Add this
                                Console.Error.WriteLine("[MCP DEBUG] prompts/list → received.");
                                // Return empty prompts list if you don't have prompts
                                WriteResult(id, new { prompts = new object[] { } });
                                break;

                            case "resources/list":  // ← Add this
                                Console.Error.WriteLine("[MCP DEBUG] resources/list → received.");
                                // Return empty resources list if you don't have resources
                                WriteResult(id, new { resources = new object[] { } });
                                break;

                            case "ping":
                                WriteResult(id, new { ok = true });
                                Console.Error.WriteLine("[MCP DEBUG] ping → ok:true");
                                break;

                            case "shutdown":
                                WriteResult(id, new { ok = true });
                                Console.Error.WriteLine("[MCP DEBUG] shutdown → terminating stdio loop.");
                                return;

                            default:
                                Console.Error.WriteLine($"[MCP DEBUG] Method not found: {method}");
                                WriteError(id, -32601, $"Method not found: {method}");
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[MCP DEBUG] Handler error for '{method}': {ex}");
                        WriteError(id, -32603, "Internal error");
                    }
                }
            }
        }

        // -------------------- Handlers --------------------

        private static void HandleInitialize(JsonElement? id)
        {
            // Extract the actual id value from the request
            int requestId = id.HasValue ? id.Value.GetInt32() : 0;

            // Create the initialize response
            var response = new
            {
                jsonrpc = "2.0",
                id = requestId,  // Use the id from the request, not hardcoded 0
                result = new
                {
                    protocolVersion = PROTOCOL_VERSION,  // Should be "2025-06-18"
                    capabilities = new
                    {
                        tools = new { listChanged = true },
                        resources = new { subscribe = true, listChanged = true },
                        prompts = new { listChanged = true },
                        logging = new { }
                    },
                    serverInfo = new
                    {
                        name = "ExampleServer",
                        version = "1.0.0"
                    }
                    // Remove "instructions" - not part of MCP spec
                }
            };

            string json = JsonSerializer.Serialize(response);
            Console.Out.WriteLine(json);
            Console.Out.Flush();

            // DO NOT send notifications here - wait for client to send notifications/initialized first
        }

        private void HandleListTools(JsonElement? id)
        {
            List<object> toolsWire = new();
            int count = 0;

            foreach (Tool tool in _toolRegistry.GetAllTools())
            {
                count++;
                toolsWire.Add(new
                {
                    name = tool.Name,
                    description = tool.Description,
                    inputSchema = tool.InputSchema // keep raw schema (JsonElement)
                });
            }

            Console.Error.WriteLine($"[MCP DEBUG] listTools → toolCount: {count}");
            WriteResult(id, new { tools = toolsWire });
            Console.Out.Flush();
        }

        private async Task HandleCallToolAsync(JsonElement? id, JsonElement root, CancellationToken ct)
        {
            if (!root.TryGetProperty("params", out JsonElement @params) || @params.ValueKind != JsonValueKind.Object)
            {
                Console.Error.WriteLine("[MCP DEBUG] callTool → missing params.");
                WriteError(id, -32602, "Missing params");
                Console.Out.Flush();
                return;
            }

            // MCP standard: params.name; allow params.tool for compatibility.
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
                WriteError(id, -32602, "Missing tool name");
                Console.Out.Flush();
                return;
            }

            if (!_toolRegistry.TryGetTool(toolName!, out IMcpTool? tool) || tool is null)
            {
                Console.Error.WriteLine($"[MCP DEBUG] callTool → tool not found: {toolName}");
                WriteError(id, -32602, $"Tool not found: {toolName}");
                Console.Out.Flush();
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

                // Execute the tool
                CallToolResult callResult = await tool.ExecuteAsync(argsDoc, _serviceProvider, ct);

                // Normalize to MCP content blocks (array). We try to pass through if a 'Content' property exists,
                // otherwise we wrap into a single text block.
                object[] content = CoerceToMcpContentBlocks(callResult);

                WriteResult(id, new { content });
                Console.Out.Flush();
            }
            finally
            {
                argsDoc?.Dispose();
            }
        }

        // -------------------- Content coercion (no ContentBlock dependency) --------------------

        private static object[] CoerceToMcpContentBlocks(object? callResult)
        {
            if (callResult == null)
            {
                return Array.Empty<object>();
            }

            PropertyInfo? prop = callResult != null
                ? callResult.GetType().GetProperty("Content", BindingFlags.Instance | BindingFlags.Public)
                : null;
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

            // If callResult is a JsonElement, return as application/json
            if (callResult is JsonElement jsonResult)
            {
                return new object[] { new { type = "application/json", data = jsonResult } };
            }

            // Fall back: serialize as text
            string text = callResult is not null ? SafeToString(callResult) : string.Empty;
            return new object[] { new { type = "text", text } };
        }

        private static string SafeToString(object obj)
        {
            try
            {
                // Try JSON first for readability
                return JsonSerializer.Serialize(obj);
            }
            catch
            {
                return obj.ToString() ?? string.Empty;
            }
        }

        // -------------------- JSON-RPC I/O --------------------

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
            Console.Out.Flush();
            Console.Error.WriteLine($"[MCP DEBUG] Sent result for Id={FormatIdForLog(id)}: {Truncate(json, 2000)}");
        }

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
            Console.Out.Flush();
            Console.Error.WriteLine($"[MCP DEBUG] Sent error for Id={FormatIdForLog(id)}: code={code}, message={message}");
        }

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

        private static string FormatIdForLog(JsonElement? id)
        {
            if (!id.HasValue)
            {
                return "null";
            }

            return id.Value.ValueKind switch
            {
                JsonValueKind.String => $"\"{id.Value.GetString()}\"",
                JsonValueKind.Number => id.Value.TryGetInt64(out long l) ? l.ToString() : "<num>",
                _ => "<non-primitive>"
            };
        }

        private static string Truncate(string s, int max)
        {
            if (string.IsNullOrEmpty(s) || s.Length <= max)
            {
                return s;
            }

            return s.Substring(0, max) + "…";
        }
    }
}
