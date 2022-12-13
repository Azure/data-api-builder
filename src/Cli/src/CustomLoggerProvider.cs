using Microsoft.Extensions.Logging;
public class CustomLoggerProvider : ILoggerProvider
{
    public void Dispose() { }

    public ILogger CreateLogger(string categoryName)
    {
        return new CustomConsoleLogger();
    }

    public class CustomConsoleLogger : ILogger
    {
        private readonly LogLevel _minimumLogLevel = LogLevel.Information;

        // Color values are same as the default values.
        Dictionary<LogLevel, ConsoleColor> _logLevelToForeGroundConsoleColorMap = new()
        {
            {LogLevel.Trace, ConsoleColor.White},
            {LogLevel.Debug, ConsoleColor.White},
            {LogLevel.Information, ConsoleColor.Green},
            {LogLevel.Warning, ConsoleColor.Yellow},
            {LogLevel.Error, ConsoleColor.White},
            {LogLevel.Critical, ConsoleColor.White}
        };

        Dictionary<LogLevel, ConsoleColor> _logLevelToBackGroundConsoleColorMap = new()
        {
            {LogLevel.Trace, ConsoleColor.Black},
            {LogLevel.Debug, ConsoleColor.Gray},
            {LogLevel.Information, ConsoleColor.Black},
            {LogLevel.Warning, ConsoleColor.Black},
            {LogLevel.Error, ConsoleColor.Red},
            {LogLevel.Critical, ConsoleColor.DarkRed}
        };

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
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

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return null;
        }
    }
}
