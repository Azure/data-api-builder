// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

#if NET8_0_OR_GREATER
/// <summary>
/// Represents the options for telemetry.
/// </summary>
public record TelemetryOptions(ApplicationInsightsOptions? ApplicationInsights = null, OpenTelemetryOptions? OpenTelemetry = null)
{ }
#else
/// <summary>
/// Represents the options for telemetry.
/// </summary>
public record TelemetryOptions(ApplicationInsightsOptions? ApplicationInsights = null)
{ }
#endif
