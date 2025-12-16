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

/// <summary>
/// Interface for customized log collector.
/// </summary>
public interface ICustomLogCollector
{
    Task LogAsync(string message, LogLevel loggingLevel, string? source = null);
    Task<List<AzureLogAnalyticsLogs>> DequeueAllAsync(string dabIdentifier, int flushIntervalSeconds);
}

/// <summary>
/// Log collector customized to retrieve and send all of the logs created by DAB.
/// </summary>
public class AzureLogAnalyticsCustomLogCollector : ICustomLogCollector
{
    private readonly Channel<AzureLogAnalyticsLogs> _logs = Channel.CreateUnbounded<AzureLogAnalyticsLogs>();

    /// <summary>
    /// Adds one log to the channel asynchronously, and saves the time at which it was created.
    /// </summary>
    /// <param name="message">Structured log message.</param>
    /// <param name="logLevel">Severity of log event.</param>
    /// <param name="source">Class from which log event originated.</param>
    public async Task LogAsync(string message, LogLevel logLevel, string? source = null)
    {
        DateTime dateTime = DateTime.UtcNow;
        await _logs.Writer.WriteAsync(
            new AzureLogAnalyticsLogs(
                dateTime.ToString("o"),
                logLevel.ToString(),
                message,
                source));
    }

    /// <summary>
    /// Creates a list periodically from the logs that are currently saved.
    /// </summary>
    /// <param name="dabIdentifier">Custom name to distinguish the logs sent from DAB to Azure Log Analytics.</param>
    /// <param name="flushIntervalSeconds">Period of time between each list of logs is sent.</param>
    /// <returns>List of logs structured to be sent to Azure Log Analytics.</returns>
    public async Task<List<AzureLogAnalyticsLogs>> DequeueAllAsync(string dabIdentifier, int flushIntervalSeconds)
    {
        List<AzureLogAnalyticsLogs> list = new();

        if (await _logs.Reader.WaitToReadAsync())
        {
            Stopwatch time = Stopwatch.StartNew();

            while (true)
            {
                if (_logs.Reader.TryRead(out AzureLogAnalyticsLogs? item))
                {
                    item.Identifier = dabIdentifier;
                    list.Add(item);
                }

                if (time.Elapsed >= TimeSpan.FromSeconds(flushIntervalSeconds))
                {
                    break;
                }
            }
        }

        return list;
    }
}
