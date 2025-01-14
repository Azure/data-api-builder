// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for telemetry.
/// </summary>
public record TelemetryOptions(ApplicationInsightsOptions? ApplicationInsights = null, OpenTelemetryOptions? OpenTelemetry = null, LogLevelOptions? LoggerLevel = null)
{
    [JsonPropertyName("log-level")]
    public LogLevelOptions? LoggerLevel { get; init; } = LoggerLevel;
}
