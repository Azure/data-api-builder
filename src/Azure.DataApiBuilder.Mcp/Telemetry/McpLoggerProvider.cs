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
    private readonly ConcurrentDictionary<string, McpLogger> _loggers = new();
    private bool _disposed;

    /// <summary>
    /// Creates a new <see cref="McpLoggerProvider"/>.
    /// </summary>
    /// <param name="writer">The notification writer used to send log messages to the MCP client.</param>
    /// <remarks>
    /// No level-filter delegate is accepted here. Level filtering is owned
    /// by the logging framework's filter chain configured in Program.cs
    /// (<c>logging.AddFilter(logLevel =&gt; LogLevelProvider.ShouldLog(logLevel))</c>),
    /// which runs before any provider's logger is invoked. Threading the
    /// same delegate through this provider would just call the same shared
    /// state twice and obscure where filtering actually happens.
    /// </remarks>
    public McpLoggerProvider(IMcpLogNotificationWriter writer)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    /// <inheritdoc />
    /// <exception cref="ObjectDisposedException">
    /// Thrown when the provider has already been disposed. Returning a fresh
    /// <see cref="McpLogger"/> after disposal would hand the caller a stale
    /// reference to <see cref="_writer"/> and bypass any teardown the host
    /// performed (e.g. flushing the underlying stdout writer). This matches
    /// the behavior of the framework <c>ConsoleLoggerProvider</c>.
    /// </exception>
    public ILogger CreateLogger(string categoryName)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return _loggers.GetOrAdd(categoryName, name => new McpLogger(name, _writer));
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
