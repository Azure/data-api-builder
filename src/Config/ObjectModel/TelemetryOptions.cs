// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for telemetry.
/// </summary>
public record TelemetryOptions(ApplicationInsightsOptions? ApplicationInsights)
{ }
