// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Core.Telemetry;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Mcp.Telemetry;

/// <summary>
/// Writes log messages as MCP `notifications/message` JSON-RPC notifications.
/// This allows MCP clients (like MCP Inspector) to receive log output in real-time.
/// </summary>
/// <remarks>
/// MCP spec: https://modelcontextprotocol.io/specification/2025-11-05/server/utilities/logging
/// The notification format is:
/// <code>
/// {
///   "jsonrpc": "2.0",
///   "method": "notifications/message",
///   "params": {
///     "level": "info",
///     "logger": "CategoryName",
///     "data": "The log message"
///   }
/// }
/// </code>
/// </remarks>
public class McpLogNotificationWriter : IMcpLogNotificationWriter
{
    private readonly object _lock = new();
    private StreamWriter? _writer;
    private bool _isEnabled;

    /// <summary>
    /// Gets or sets whether MCP log notifications are enabled.
    /// When false, no notifications are written (to keep stdout clean before client requests logging).
    /// </summary>
    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            lock (_lock)
            {
                _isEnabled = value;
                if (value && _writer == null)
                {
                    InitializeWriter();
                }
            }
        }
    }

    /// <summary>
    /// Initializes the stdout writer for MCP notifications.
    /// Uses Console.OpenStandardOutput() to get the raw stdout stream,
    /// bypassing any Console.SetOut() redirections.
    /// </summary>
    private void InitializeWriter()
    {
        // Use the same approach as McpStdioServer - get raw stdout
        Stream stdout = Console.OpenStandardOutput();
        _writer = new StreamWriter(stdout, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
        {
            AutoFlush = true
        };
    }

    /// <summary>
    /// Writes a log message as an MCP notification.
    /// </summary>
    /// <param name="logLevel">The .NET log level.</param>
    /// <param name="categoryName">The logger category (typically class name).</param>
    /// <param name="message">The formatted log message.</param>
    public void WriteNotification(LogLevel logLevel, string categoryName, string message)
    {
        if (!_isEnabled || _writer == null)
        {
            return;
        }

        string mcpLevel = McpLogLevelConverter.ConvertToMcp(logLevel);

        var notification = new
        {
            jsonrpc = McpStdioJsonRpcErrorCodes.JSON_RPC_VERSION,
            method = "notifications/message",
            @params = new
            {
                level = mcpLevel,
                logger = categoryName,
                data = message
            }
        };

        string json = JsonSerializer.Serialize(notification);

        lock (_lock)
        {
            _writer?.WriteLine(json);
        }
    }
}

/// <summary>
/// Interface for MCP log notification writing.
/// </summary>
public interface IMcpLogNotificationWriter
{
    /// <summary>
    /// Gets or sets whether MCP log notifications are enabled.
    /// </summary>
    bool IsEnabled { get; set; }

    /// <summary>
    /// Writes a log message as an MCP notification.
    /// </summary>
    void WriteNotification(LogLevel logLevel, string categoryName, string message);
}
