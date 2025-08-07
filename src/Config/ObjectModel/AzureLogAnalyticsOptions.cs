// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for configuring Azure Log Analytics.
/// Properties are nullable to support DAB CLI merge config
/// expected behavior.
/// </summary>
public record AzureLogAnalyticsOptions
{
    /// <summary>
    /// Default enabled for Azure Log Analytics.
    /// </summary>
    public const bool DEFAULT_ENABLED = false;

    /// <summary>
    /// Default log type for Azure Log Analytics.
    /// </summary>
    public const string DEFAULT_DAB_IDENTIFIER = "DabLogs";

    /// <summary>
    /// Default flush interval in seconds.
    /// </summary>
    public const int DEFAULT_FLUSH_INTERVAL_SECONDS = 5;

    /// <summary>
    /// Whether Azure Log Analytics is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Authentication options for Azure Log Analytics.
    /// </summary>
    public AzureLogAnalyticsAuthOptions? Auth { get; init; }

    /// <summary>
    /// Custom identifier name to send to Log Analytics.
    /// </summary>
    public string? DabIdentifier { get; init; }

    /// <summary>
    /// Interval between log batch pushes (in seconds).
    /// </summary>
    public int? FlushIntervalSeconds { get; init; }

    [JsonConstructor]
    public AzureLogAnalyticsOptions(bool? enabled = null, AzureLogAnalyticsAuthOptions? auth = null, string? dabIdentifier = null, int? flushIntervalSeconds = null)
    {
        Auth = auth;

        if (enabled is not null)
        {
            Enabled = (bool)enabled;
            UserProvidedEnabled = true;
        }
        else
        {
            Enabled = DEFAULT_ENABLED;
        }

        if (dabIdentifier is not null)
        {
            DabIdentifier = dabIdentifier;
            UserProvidedDabIdentifier = true;
        }
        else
        {
            DabIdentifier = DEFAULT_DAB_IDENTIFIER;
        }

        if (flushIntervalSeconds is not null)
        {
            FlushIntervalSeconds = flushIntervalSeconds;
            UserProvidedFlushIntervalSeconds = true;
        }
        else
        {
            FlushIntervalSeconds = DEFAULT_FLUSH_INTERVAL_SECONDS;
        }
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write enabled
    /// property and value to the runtime config file.
    /// When user doesn't provide the enabled property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Enabled))]
    public bool UserProvidedEnabled { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write dab-identifier
    /// property and value to the runtime config file.
    /// When user doesn't provide the dab-identifier property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(DabIdentifier))]
    public bool UserProvidedDabIdentifier { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write flush-interval-seconds
    /// property and value to the runtime config file.
    /// When user doesn't provide the flush-interval-seconds property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(FlushIntervalSeconds))]
    public bool UserProvidedFlushIntervalSeconds { get; init; } = false;
}
