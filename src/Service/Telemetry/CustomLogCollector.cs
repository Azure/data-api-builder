// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    public interface ICustomLogCollector
    {
        void Log(string message, LogLevel loggingLevel, string? source = null);
        List<AzureLogAnalyticsLogOptions> DequeueAll();
    }

    public class CustomLogCollector : ICustomLogCollector
    {
        private readonly ConcurrentQueue<AzureLogAnalyticsLogOptions> _logs = new();

        public void Log(string message, LogLevel loggingLevel, string? source = null)
        {
            _logs.Enqueue(
                new AzureLogAnalyticsLogOptions(
                    DateTime.UtcNow,
                    loggingLevel,
                    message,
                    source));
        }

        public List<AzureLogAnalyticsLogOptions> DequeueAll()
        {
            List<AzureLogAnalyticsLogOptions> list = new();
            while (_logs.TryDequeue(out AzureLogAnalyticsLogOptions? item))
            {
                list.Add(item);
            }

            return list;
        }
    }
}
