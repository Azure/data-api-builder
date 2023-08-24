using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;

public class ApplicationInsightsLoggerProvider : ILoggerProvider
{
    private readonly TelemetryClient _telemetryClient;

    public ApplicationInsightsLoggerProvider(TelemetryClient telemetryClient)
    {
        _telemetryClient = telemetryClient;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new ApplicationInsightsLogger(_telemetryClient, categoryName);
    }

    public void Dispose()
    {
        // Nothing to dispose
    }

    private class ApplicationInsightsLogger : ILogger
    {
        private readonly TelemetryClient _telemetryClient;
        private readonly string _categoryName;

        public ApplicationInsightsLogger(TelemetryClient telemetryClient, string categoryName)
        {
            _telemetryClient = telemetryClient;
            _categoryName = categoryName;
        }

        public IDisposable BeginScope<TState>(TState state)
        {
            return new ApplicationInsightsScope();
        }

        public bool IsEnabled(LogLevel logLevel)
        {
            return true;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Error)
            {
                _telemetryClient.TrackEvent("ErrorCaught", new Dictionary<string, string>
                {
                    { "CategoryName", _categoryName },
                    { "Message", formatter(state, exception) },
                });
            }
        }
    }

    private class ApplicationInsightsScope : IDisposable
    {
        public void Dispose()
        {
            // Nothing to dispose
        }
    }
}
