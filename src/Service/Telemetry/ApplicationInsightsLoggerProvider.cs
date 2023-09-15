// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.ApplicationInsights;
using Microsoft.Extensions.Logging;

/// <summary>
/// Provides a custom logger implementation that sends telemetry events to Azure Application Insights.
/// See <see href="https://learn.microsoft.com/en-us/dotnet/core/extensions/custom-logging-provider">Custom Logging Provider</see> for more information.
/// </summary>
public class ApplicationInsightsLoggerProvider : ILoggerProvider
{
    private readonly TelemetryClient _telemetryClient;

    public static readonly string ERROR_CAUGHT_EVENT_NAME = "ErrorCaught";

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

    /// <summary>
    /// This logger implementation is used to track errors caught by the application.
    /// </summary>
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
                // The state parameter is an object that contains additional information about the log event.
                // The code checks if state is an IEnumerable<KeyValuePair<string, object>> and uses the ToDictionary method to convert it to a dictionary of key-value pairs.
                // If state is not an IEnumerable<KeyValuePair<string, object>>, the code creates an empty dictionary.
                IDictionary<string, string> properties = (state as IEnumerable<KeyValuePair<string, object>>)?
                    .ToDictionary(kv => kv.Key, kv => kv.Value?.ToString() ?? string.Empty)
                    ?? new Dictionary<string, string>();

                properties["CategoryName"] = _categoryName;
                properties["Message"] = formatter(state, exception);
                _telemetryClient.TrackEvent(ERROR_CAUGHT_EVENT_NAME, properties);
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
