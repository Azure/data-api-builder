// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Telemetry;

/// <summary>
/// Logger used to receive all the logs that will be sent to Azure Log Analytics
/// and are created by Data API builder while it is running.
/// </summary>
public class AzureLogAnalyticsLogger : ILogger
{
    private readonly string _className;
    private readonly ICustomLogCollector _customLogCollector;

    public AzureLogAnalyticsLogger(string className, ICustomLogCollector customLogCollector)
    {
        _className = className;
        _customLogCollector = customLogCollector;
    }

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => default!;

    public bool IsEnabled(LogLevel logLevel) => true;

    public async void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        string message = formatter(state, exception);
        await _customLogCollector.LogAsync(message, logLevel, _className);
    }
}
