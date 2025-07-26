// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Telemetry;

public interface ICustomLogCollector
{
    void Log(string message, LogLevel loggingLevel, string? source = null);
    List<AzureLogAnalyticsLogs> DequeueAll();
}

public class AzureLogAnalyticsCustomLogCollector : ICustomLogCollector
{
    private readonly ConcurrentQueue<AzureLogAnalyticsLogs> _logs = new();

    public void Log(string message, LogLevel loggingLevel, string? source = null)
    {
        DateTime dateTime = DateTime.UtcNow;
        _logs.Enqueue(
            new AzureLogAnalyticsLogs(
                dateTime.ToString("o"),
                loggingLevel,
                message,
                source));
    }

    public List<AzureLogAnalyticsLogs> DequeueAll()
    {
        List<AzureLogAnalyticsLogs> list = new();
        while (_logs.TryDequeue(out AzureLogAnalyticsLogs? item))
        {
            list.Add(item);
        }

        return list;
    }
}
