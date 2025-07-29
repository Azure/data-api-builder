// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Telemetry;

public interface ICustomLogCollector
{
    Task LogAsync(string message, LogLevel loggingLevel, string? source = null);
    Task<List<AzureLogAnalyticsLogs>> DequeueAllAsync(string logType, int flushIntervalSeconds);
}

public class AzureLogAnalyticsCustomLogCollector : ICustomLogCollector
{
    private readonly Channel<AzureLogAnalyticsLogs> _logs = Channel.CreateUnbounded<AzureLogAnalyticsLogs>();

    public async Task LogAsync(string message, LogLevel loggingLevel, string? source = null)
    {
        DateTime dateTime = DateTime.UtcNow;
        await _logs.Writer.WriteAsync(
            new AzureLogAnalyticsLogs(
                dateTime.ToString("o"),
                loggingLevel,
                message,
                source));
    }

    public async Task<List<AzureLogAnalyticsLogs>> DequeueAllAsync(string logType, int flushIntervalSeconds)
    {
        List<AzureLogAnalyticsLogs> list = new();
        Stopwatch time = Stopwatch.StartNew();

        await foreach (AzureLogAnalyticsLogs item in _logs.Reader.ReadAllAsync())
        {
            item.LogType = logType;
            list.Add(item);

            if (time.Elapsed >= TimeSpan.FromSeconds(flushIntervalSeconds))
            {
                break;
            }
        }

        return list;
    }
}
