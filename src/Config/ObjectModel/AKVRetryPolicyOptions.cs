// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record AKVRetryPolicyOptions
{
    public const AKVRetryPolicyMode DEFAULT_MODE = AKVRetryPolicyMode.Exponential;

    public const int DEFAULT_MAX_COUNT = 3;

    public const int DEFAULT_DELAY_SECONDS = 1;

    public const int DEFAULT_MAX_DELAY_SECONDS = 60;

    public const int DEFAULT_NETWORK_TIMEOUT_SECONDS = 60;

    [JsonPropertyName("mode")]
    public AKVRetryPolicyMode? Mode { get; init; } = null;

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Mode))]
    public bool UserProvidedMode { get; init; } = false;

    [JsonPropertyName("max-count")]
    public int? MaxCount { get; init; } = null;

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(MaxCount))]
    public bool UserProvidedMaxCount { get; init; } = false;

    [JsonPropertyName("delay-seconds")]
    public int? DelaySeconds { get; init; } = null;

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(DelaySeconds))]
    public bool UserProvidedDelaySeconds { get; init; } = false;

    [JsonPropertyName("max-delay-seconds")]
    public int? MaxDelaySeconds { get; init; } = null;

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(MaxDelaySeconds))]
    public bool UserProvidedMaxDelaySeconds { get; init; } = false;

    [JsonPropertyName("network-timeout-seconds")]
    public int? NetworkTimeoutSeconds { get; init; } = null;

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(NetworkTimeoutSeconds))]
    public bool UserProvidedNetworkTimeoutSeconds { get; init; } = false;

    public AKVRetryPolicyOptions(
        AKVRetryPolicyMode? mode = null,
        int? maxCount = null,
        int? delaySeconds = null,
        int? maxDelaySeconds = null,
        int? networkTimeoutSeconds = null)
    {
        if (mode is not null)
        {
            this.Mode = mode;
            UserProvidedMode = true;
        }
        else
        {
            this.Mode = DEFAULT_MODE;
        }

        if (maxCount is not null)
        {
            this.MaxCount = maxCount;
            UserProvidedMaxCount = true;
        }
        else
        {
            this.MaxCount = DEFAULT_MAX_COUNT;
        }

        if (delaySeconds is not null)
        {
            this.DelaySeconds = delaySeconds;
            UserProvidedDelaySeconds = true;
        }
        else
        {
            this.DelaySeconds = DEFAULT_DELAY_SECONDS;
        }

        if (maxDelaySeconds is not null)
        {
            this.MaxDelaySeconds = maxDelaySeconds;
            UserProvidedMaxDelaySeconds = true;
        }
        else
        {
            this.MaxDelaySeconds = DEFAULT_MAX_DELAY_SECONDS;
        }

        if (networkTimeoutSeconds is not null)
        {
            this.NetworkTimeoutSeconds = networkTimeoutSeconds;
            UserProvidedNetworkTimeoutSeconds = true;
        }
        else
        {
            this.NetworkTimeoutSeconds = DEFAULT_NETWORK_TIMEOUT_SECONDS;
        }
    }
}
