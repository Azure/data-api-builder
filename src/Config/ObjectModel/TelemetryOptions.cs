// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Extensions.Logging;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for telemetry.
/// </summary>
public record TelemetryOptions(ApplicationInsightsOptions? ApplicationInsights = null, OpenTelemetryOptions? OpenTelemetry = null, SortedList<string, LogLevel?>? LoggerLevel = null)
{
    [JsonPropertyName("log-level")]
    public SortedList<string, LogLevel?>? LoggerLevel { get; init; } = LoggerLevel;
}
