// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Core.Telemetry;
using Azure.DataApiBuilder.Mcp.Core;
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
/// All writes are routed through the shared <see cref="McpStdoutWriter"/> so
/// notifications cannot interleave with JSON-RPC responses written by
/// <see cref="McpStdioServer"/>.
/// </remarks>
public class McpLogNotificationWriter : IMcpLogNotificationWriter
{
    private readonly McpStdoutWriter? _stdoutWriter;

    /// <summary>
    /// Creates a notification writer that writes through the shared stdout
    /// writer. The shared writer serializes notifications with JSON-RPC
    /// responses so concurrent writes do not interleave on the wire.
    /// </summary>
    /// <param name="stdoutWriter">
    /// Shared stdout writer. May be <c>null</c> for unit tests that do not
    /// exercise the write path; in that case <see cref="WriteNotification"/>
    /// becomes a no-op.
    /// </param>
    public McpLogNotificationWriter(McpStdoutWriter? stdoutWriter = null)
    {
        _stdoutWriter = stdoutWriter;
    }

    /// <summary>
    /// Gets or sets whether MCP log notifications are enabled. This is the
    /// single source of truth for whether notifications flow to the client;
    /// it is consulted by <see cref="McpLogger.IsEnabled(LogLevel)"/> so that
    /// the gate is enforced once, at log time, before any formatter work runs.
    /// <see cref="WriteNotification"/> intentionally does not re-check this
    /// flag — callers must gate via <see cref="McpLogger"/>.
    /// </summary>
    public bool IsEnabled { get; set; }

    /// <summary>
    /// Writes a log message as an MCP notification. The caller is responsible
    /// for gating on <see cref="IsEnabled"/>; <see cref="McpLogger"/> already
    /// does this in its <see cref="McpLogger.IsEnabled(LogLevel)"/> override.
    /// </summary>
    /// <param name="logLevel">The .NET log level.</param>
    /// <param name="categoryName">The logger category (typically class name).</param>
    /// <param name="message">The formatted log message.</param>
    public void WriteNotification(LogLevel logLevel, string categoryName, string message)
    {
        // No IsEnabled check here: the gate lives in McpLogger.IsEnabled so
        // that we have a single source of truth and a single check site.
        // The _stdoutWriter null check remains as a defensive guard for unit
        // tests that construct the writer without a backing stdout.
        if (_stdoutWriter is null)
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

        _stdoutWriter.WriteLine(JsonSerializer.Serialize(notification));
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
