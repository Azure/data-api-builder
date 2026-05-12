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

    /// <summary>
    /// Creates a new <see cref="McpLogger"/>.
    /// </summary>
    /// <remarks>
    /// No level-filter delegate is accepted here. Level filtering is the job
    /// of the logging framework's filter chain (configured via
    /// <c>ILoggingBuilder.AddFilter(...)</c> in Program.cs); by the time the
    /// framework calls <see cref="Log"/>, those filters have already passed.
    /// Re-running the same delegate against the same shared
    /// <c>LogLevelProvider</c> state would produce the same answer and only
    /// add a maintenance trap (a future contributor could mistake the per-
    /// logger filter for an independent gate).
    /// </remarks>
    public McpLogger(string categoryName, IMcpLogNotificationWriter writer)
    {
        _categoryName = categoryName ?? throw new ArgumentNullException(nameof(categoryName));
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Scopes are intentionally not supported in this implementation. The MCP
    /// <c>notifications/message</c> frame has no first-class structured field
    /// for scope state, and we currently emit a plain string in <c>params.data</c>.
    /// 
    /// TODO: Consider implementing <see cref="Microsoft.Extensions.Logging.ISupportExternalScope"/>
    /// on <see cref="McpLoggerProvider"/> so scope state can be flowed through
    /// from the host (e.g. ASP.NET Core request scopes, activity correlation
    /// IDs). When done, this method should return a real scope tied to an
    /// <see cref="Microsoft.Extensions.Logging.IExternalScopeProvider"/>, and
    /// <see cref="Log"/> should walk
    /// <see cref="Microsoft.Extensions.Logging.IExternalScopeProvider.ForEachScope{TState}"/>
    /// to append (or attach as a structured field on the JSON-RPC notification)
    /// the active scope chain. See aaronburtle's review on PR for context.
    /// </remarks>
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull
    {
        // Scopes are not supported for MCP notifications. See remarks above
        // for the path to add ISupportExternalScope support in the future.
        return NullScope.Instance;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns true when the writer is enabled (the MCP client has issued
    /// <c>logging/setLevel</c> with a non-"none" value) and the requested
    /// level is not <see cref="LogLevel.None"/>. Per-level filtering is
    /// applied upstream by the framework's filter chain in Program.cs.
    /// </remarks>
    public bool IsEnabled(LogLevel logLevel)
    {
        return _writer.IsEnabled && logLevel != LogLevel.None;
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

        // Append the full exception details (type, message, stack trace, and
        // any inner exceptions) using Exception.ToString(). This matches the
        // behavior of the built-in console/Serilog formatters and is what MCP
        // clients (e.g. MCP Inspector) render for log notifications. Dropping
        // the stack trace would make production triage from a remote client
        // effectively impossible. ToString() walks InnerException chains and
        // flattens AggregateException, so no manual recursion is needed.
        if (exception != null)
        {
            string separator = string.IsNullOrEmpty(message) ? string.Empty : Environment.NewLine;
            message = $"{message}{separator}{exception}";
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
