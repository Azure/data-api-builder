// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for telemetry.
/// </summary>
/// <param name="ApplicationInsights">Options for configuring Application Insights.</param>
/// <param name="OpenTelemetry">Options for configuring Open Telemetry.</param>
/// <param name="AzureLogAnalytics">Options for configuring Azure Log Analytics.</param>
/// <param name="File">Options for configuring File Sink.</param>
/// <param name="LoggerLevel">Options for configuring the Log Level filters.</param>
public record TelemetryOptions(
    ApplicationInsightsOptions? ApplicationInsights = null,
    OpenTelemetryOptions? OpenTelemetry = null,
    AzureLogAnalyticsOptions? AzureLogAnalytics = null,
    FileSinkOptions? File = null,
    Dictionary<string, LogLevel?>? LoggerLevel = null)
{
    [JsonPropertyName("log-level")]
    public Dictionary<string, LogLevel?>? LoggerLevel { get; init; } = LoggerLevel;
}
