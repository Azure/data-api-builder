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
        private readonly LogLevel _minimumLogLevel = LogLevel.Information;

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
        /// Creates Log message by setting console message color based on LogLevel.
        /// </summary>
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || logLevel < _minimumLogLevel)
            {
                return;
            }

            ConsoleColor originalForeGroundColor = Console.ForegroundColor;
            ConsoleColor originalBackGroundColor = Console.BackgroundColor;
            Console.ForegroundColor = _logLevelToForeGroundConsoleColorMap[logLevel];
            Console.BackgroundColor = _logLevelToBackGroundConsoleColorMap[logLevel];
            Console.Write($"{logLevel}:");
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
