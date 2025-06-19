// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record RetryPolicyOptions
{
    [JsonPropertyName("mode")]
    public RetryPolicyMode Mode { get; init; } = RetryPolicyMode.Exponential;

    [JsonPropertyName("max-count")]
    public int MaxCount { get; init; } = 3;

    [JsonPropertyName("delay-seconds")]
    public int DelaySeconds { get; init; } = 1;

    [JsonPropertyName("max-delay-seconds")]
    public int MaxDelaySeconds { get; init; } = 60;

    [JsonPropertyName("network-timeout-seconds")]
    public int NetworkTimeoutSeconds { get; init; } = 60;
}