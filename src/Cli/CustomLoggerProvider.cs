// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

/// <summary>
/// LoggerProvider to generate Custom Log message
/// </summary>
public class CustomLoggerProvider : ILoggerProvider
{
    public void Dispose() { }

    /// <inheritdoc/>
    public ILogger CreateLogger(string categoryName)
    {
        return new CustomConsoleLogger();
    }

    public class CustomConsoleLogger : ILogger
    {
        // Minimum LogLevel. LogLevel below this would be disabled.
        // When CLI specifies --LogLevel, use that value; otherwise default to Information.
        private static LogLevel MinimumLogLevel => Cli.Utils.IsLogLevelOverriddenByCli ? Cli.Utils.CliLogLevel : LogLevel.Information;

        //  Color values based on LogLevel
        //  LogLevel    Foreground      Background
        //  Trace         White           Black
        //  Debug         White           Gray
        //  Information   Green           Black
        //  Warning       Yellow          Black
        //  Error         White           Red
        //  Critical      White           DarkRed

        /// <summary>
        /// Foreground color for Console message based on LogLevel
        /// </summary>
        Dictionary<LogLevel, ConsoleColor> _logLevelToForeGroundConsoleColorMap = new()
        {
            {LogLevel.Trace, ConsoleColor.White},
            {LogLevel.Debug, ConsoleColor.White},
            {LogLevel.Information, ConsoleColor.Green},
            {LogLevel.Warning, ConsoleColor.Yellow},
            {LogLevel.Error, ConsoleColor.White},
            {LogLevel.Critical, ConsoleColor.White}
        };

        /// <summary>
        /// Background color for Console message based on LogLevel
        /// </summary>
        Dictionary<LogLevel, ConsoleColor> _logLevelToBackGroundConsoleColorMap = new()
        {
            {LogLevel.Trace, ConsoleColor.Black},
            {LogLevel.Debug, ConsoleColor.Gray},
            {LogLevel.Information, ConsoleColor.Black},
            {LogLevel.Warning, ConsoleColor.Black},
            {LogLevel.Error, ConsoleColor.Red},
            {LogLevel.Critical, ConsoleColor.DarkRed}
        };

        /// <summary>
        /// Maps LogLevel to abbreviated labels matching ASP.NET Core's default console formatter.
        /// </summary>
        private static readonly Dictionary<LogLevel, string> _logLevelToAbbreviation = new()
        {
            {LogLevel.Trace, "trce"},
            {LogLevel.Debug, "dbug"},
            {LogLevel.Information, "info"},
            {LogLevel.Warning, "warn"},
            {LogLevel.Error, "fail"},
            {LogLevel.Critical, "crit"}
        };

        /// <summary>
        /// Creates Log message by setting console message color based on LogLevel.
        /// In MCP stdio mode:
        /// - If user explicitly set --LogLevel: write to stderr (colored output)
        /// - Otherwise: suppress entirely to keep stdout clean for JSON-RPC protocol.
        /// </summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // In MCP stdio mode, only output logs if user explicitly requested a log level.
            // In that case, write to stderr to keep stdout clean for JSON-RPC.
            if (Cli.Utils.IsMcpStdioMode)
            {
                if (!Cli.Utils.IsLogLevelOverriddenByCli)
                {
                    return; // Suppress entirely when no explicit log level
                }

                // User wants logs in MCP mode - write to stderr
                if (!IsEnabled(logLevel) || logLevel < MinimumLogLevel)
                {
                    return;
                }

                if (!_logLevelToAbbreviation.TryGetValue(logLevel, out string? mcpAbbreviation))
                {
                    return;
                }

                Console.Error.WriteLine($"{mcpAbbreviation}: {formatter(state, exception)}");
                return;
            }

            if (!IsEnabled(logLevel) || logLevel < MinimumLogLevel)
            {
                return;
            }

            if (!_logLevelToAbbreviation.TryGetValue(logLevel, out string? abbreviation))
            {
                return;
            }

            ConsoleColor originalForeGroundColor = Console.ForegroundColor;
            ConsoleColor originalBackGroundColor = Console.BackgroundColor;
            Console.ForegroundColor = _logLevelToForeGroundConsoleColorMap.GetValueOrDefault(logLevel, ConsoleColor.White);
            Console.BackgroundColor = _logLevelToBackGroundConsoleColorMap.GetValueOrDefault(logLevel, ConsoleColor.Black);
            Console.Write($"{abbreviation}:");
            Console.ForegroundColor = originalForeGroundColor;
            Console.BackgroundColor = originalBackGroundColor;
            Console.WriteLine($" {formatter(state, exception)}");
        }

        /// <inheritdoc/>
        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull
        {
            throw new NotImplementedException();
        }
    }
}
