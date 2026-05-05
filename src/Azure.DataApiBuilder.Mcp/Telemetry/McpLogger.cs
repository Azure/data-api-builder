// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Mcp.Telemetry;

/// <summary>
/// ILogger implementation that sends log messages as MCP notifications.
/// </summary>
public class McpLogger : ILogger
{
    private readonly string _categoryName;
    private readonly IMcpLogNotificationWriter _writer;
    private readonly Func<LogLevel, bool> _levelFilter;

    public McpLogger(string categoryName, IMcpLogNotificationWriter writer, Func<LogLevel, bool> levelFilter)
    {
        _categoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _levelFilter = levelFilter ?? throw new ArgumentNullException(nameof(levelFilter));
    }

    /// <inheritdoc />
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        // Scopes are not supported for MCP notifications
        return NullScope.Instance;
    }

    /// <inheritdoc />
    public bool IsEnabled(LogLevel logLevel)
    {
        return _writer.IsEnabled && logLevel != LogLevel.None && _levelFilter(logLevel);
    }

    /// <inheritdoc />
    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
        {
            return;
        }

        if (formatter == null)
        {
            throw new ArgumentNullException(nameof(formatter));
        }

        string message = formatter(state, exception);

        if (string.IsNullOrEmpty(message) && exception == null)
        {
            return;
        }

        // Include exception details if present
        if (exception != null)
        {
            message = $"{message} Exception: {exception.GetType().Name}: {exception.Message}";
        }

        _writer.WriteNotification(logLevel, _categoryName, message);
    }

    /// <summary>
    /// Null scope implementation for when scopes are not supported.
    /// </summary>
    private sealed class NullScope : IDisposable
    {
        public static NullScope Instance { get; } = new NullScope();

        private NullScope()
        {
        }

        public void Dispose()
        {
        }
    }
}
