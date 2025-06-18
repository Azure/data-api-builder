// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for configuring Azure Log Analytics.
/// </summary>
public record AzureLogAnalyticsOptions(
    bool Enabled = false,
    AzureLogAnalyticsAuthOptions? Auth = null,
    string LogType = "DabLogs",
    int FlushIntervalSeconds = 5)
{ }
