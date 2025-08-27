// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Class used to save the components for the logs that are sent to Azure Log Analytics
/// </summary>
public class AzureLogAnalyticsLogs
{
    public string Time { get; set; }
    public string LogLevel { get; set; }
    public string? Message { get; set; }
    public string? Component { get; set; }
    public string? Identifier { get; set; }

    public AzureLogAnalyticsLogs(string time, string logLevel, string? message, string? component, string? identifier = null)
    {
        Time = time;
        LogLevel = logLevel;
        Message = message;
        Component = component;
        Identifier = identifier;
    }
}
