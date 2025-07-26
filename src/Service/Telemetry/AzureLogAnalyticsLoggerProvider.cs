// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Telemetry;

public class AzureLogAnalyticsLoggerProvider : ILoggerProvider
{
    private readonly ICustomLogCollector _customLogCollector;

    public AzureLogAnalyticsLoggerProvider (ICustomLogCollector customLogCollector)
    {
        _customLogCollector = customLogCollector;
    }

    public ILogger CreateLogger(string categoryName)
    {
        return new AzureLogAnalyticsLogger(categoryName, _customLogCollector);
    }

    public void Dispose() { }
}

public class AzureLogAnalyticsLogger : ILogger
{
    private readonly string _categoryName;
    private readonly ICustomLogCollector _customLogCollector;

    public AzureLogAnalyticsLogger (string categoryName, ICustomLogCollector customLogCollector)
    {
        _categoryName = categoryName;
        _customLogCollector = customLogCollector;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        string message = formatter(state, exception);
        _customLogCollector.Log(message, logLevel, _categoryName);
    }
}
