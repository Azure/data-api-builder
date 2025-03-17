// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for telemetry.
/// </summary>
public record TelemetryOptions(ApplicationInsightsOptions? ApplicationInsights = null, OpenTelemetryOptions? OpenTelemetry = null, SortedList<string, LogLevel?>? LoggerLevel = null)
{
    [JsonPropertyName("log-level")]
    public SortedList<string, LogLevel?>? LoggerLevel { get; init; } = LoggerLevel;
}
