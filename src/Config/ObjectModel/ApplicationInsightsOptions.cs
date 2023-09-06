// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for configuring Application Insights.
/// </summary>
public record ApplicationInsightsOptions(bool Enabled = false, string? ConnectionString = null)
{ }
