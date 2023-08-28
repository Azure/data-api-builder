// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;

/// <summary>
/// Provides a logger implementation that sends telemetry to Azure Application Insights.
/// </summary>
/// <remarks>
/// This logger implementation is used to track errors caught by the application.
/// </remarks>
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
            // Whenever ILogger.Log is called for Error, we send an event to Application Insights.
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
