// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for configuring Azure Log Analytics.
/// </summary>
public record AzureLogAnalyticsOptions(
    bool Enabled = false,
    AzureLogAnalyticsAuthOptions? Auth = null,
    [property: JsonPropertyName("log-type")] string LogType = "DabLogs",
    [property: JsonPropertyName("flush-interval-seconds")] int FlushIntervalSeconds = 5)
{ }