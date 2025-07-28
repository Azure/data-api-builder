// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Class used to save the components for the logs that are sent to Azure Log Analytics
/// </summary>
public class AzureLogAnalyticsLogs
{
    public string Time { get; set; }
    public LogLevel LoggingLevel { get; set; }
    public string? Message { get; set; }
    public string? Component { get; set; }
    public string? LogType { get; set; }

    public AzureLogAnalyticsLogs(string time, LogLevel loggingLevel, string? message, string? component, string? logType = null)
    {
        Time = time;
        LoggingLevel = loggingLevel;
        Message = message;
        Component = component;
        LogType = logType;
    }
}
