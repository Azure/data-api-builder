// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Mcp.Telemetry;

/// <summary>
/// Logger provider that creates McpLogger instances for sending logs as MCP notifications.
/// </summary>
public class McpLoggerProvider : ILoggerProvider
{
    private readonly IMcpLogNotificationWriter _writer;
    private readonly Func<LogLevel, bool> _levelFilter;
    private readonly ConcurrentDictionary<string, McpLogger> _loggers = new();
    private bool _disposed;

    /// <summary>
    /// Creates a new McpLoggerProvider.
    /// </summary>
    /// <param name="writer">The notification writer to use for sending log messages.</param>
    /// <param name="levelFilter">A function to filter log levels. Returns true if the level should be logged.</param>
    public McpLoggerProvider(IMcpLogNotificationWriter writer, Func<LogLevel, bool> levelFilter)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _levelFilter = levelFilter ?? throw new ArgumentNullException(nameof(levelFilter));
    }

    /// <inheritdoc />
    public ILogger CreateLogger(string categoryName)
    {
        return _loggers.GetOrAdd(categoryName, name => new McpLogger(name, _writer, _levelFilter));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (!_disposed)
        {
            _loggers.Clear();
            _disposed = true;
        }
    }
}
