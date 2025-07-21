// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    public class AzureLogAnalyticsLogOptions
    {
        public DateTime Time { get; set; }
        public LogLevel LoggingLevel { get; set; }
        public string? Message { get; set; }
        public string? Component { get; set; }

        public AzureLogAnalyticsLogOptions(DateTime time, LogLevel loggingLevel, string? message, string? component)
        {
            Time = time;
            LoggingLevel = loggingLevel;
            Message = message;
            Component = component;
        }
    }
}
