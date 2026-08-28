// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Globalization;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Config.Utilities;

/// <summary>
/// Centralized console logger used for diagnostics which can't be routed through
/// the dependency injection provided ILogger, e.g. messages emitted before the host
/// (and its logging pipeline) is built, or from static helpers which have no injected logger.
/// Output matches the console logging pipeline's format by prefixing every entry with an
/// ISO 8601 UTC timestamp with millisecond precision, e.g.
/// <code>2026-07-07T14:01:01.344Z fail: Unable to launch the Data API builder engine.</code>
/// This is the single place where such timestamps are formatted, so call sites only
/// need to use the <see cref="Microsoft.Extensions.Logging"/> APIs.
/// </summary>
public static class BootstrapLogger
{
    /// <summary>
    /// ISO 8601 UTC timestamp with millisecond precision.
    /// </summary>
    private const string UTC_TIMESTAMP_FORMAT = "yyyy-MM-dd'T'HH:mm:ss.fff'Z'";

    /// <summary>
    /// Maps LogLevel to abbreviated labels matching ASP.NET Core's default console formatter.
    /// </summary>
    private static readonly Dictionary<LogLevel, string> _logLevelToAbbreviation = new()
    {
        { LogLevel.Trace, "trce" },
        { LogLevel.Debug, "dbug" },
        { LogLevel.Information, "info" },
        { LogLevel.Warning, "warn" },
        { LogLevel.Error, "fail" },
        { LogLevel.Critical, "crit" }
    };

    /// <summary>
    /// When true, all entries are written to stderr. Set by hosts which reserve
    /// stdout for a protocol stream, e.g. MCP stdio mode's JSON-RPC messages.
    /// </summary>
    public static bool WriteAllOutputToStandardError { get; set; }

    /// <summary>
    /// Shared logger instance used by all call sites.
    /// </summary>
    public static ILogger Instance { get; } = new ConsoleBootstrapLogger();

    private sealed class ConsoleBootstrapLogger : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || !_logLevelToAbbreviation.TryGetValue(logLevel, out string? abbreviation))
            {
                return;
            }

            string message = formatter(state, exception);
            if (exception is not null)
            {
                message = string.IsNullOrEmpty(message) ? exception.ToString() : $"{message} {exception}";
            }

            // CultureInfo.InvariantCulture guarantees deterministic ISO 8601 output
            // regardless of the machine's locale (digits, calendar).
            string timestamp = DateTime.UtcNow.ToString(UTC_TIMESTAMP_FORMAT, CultureInfo.InvariantCulture);
            TextWriter writer = WriteAllOutputToStandardError || logLevel >= LogLevel.Error
                ? Console.Error
                : Console.Out;
            writer.WriteLine($"{timestamp} {abbreviation}: {message}");
        }
    }
}
