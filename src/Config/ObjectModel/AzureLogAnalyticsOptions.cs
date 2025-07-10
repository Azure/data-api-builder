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
    /// Default log type for Azure Log Analytics.
    /// </summary>
    public const string DEFAULT_LOG_TYPE = "DabLogs";

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
    /// Custom log table name in Log Analytics.
    /// </summary>
    public string? LogType { get; init; }

    /// <summary>
    /// Interval between log batch pushes (in seconds).
    /// </summary>
    public int? FlushIntervalSeconds { get; init; }

    [JsonConstructor]
    public AzureLogAnalyticsOptions(bool enabled = false, AzureLogAnalyticsAuthOptions? auth = null, string? logType = null, int? flushIntervalSeconds = null)
    {
        Auth = auth;

        Enabled = enabled;
        if (enabled)
        {
            UserProvidedEnabled = true;
        }

        if (logType is not null)
        {
            LogType = logType;
            UserProvidedLogType = true;
        }
        else
        {
            LogType = DEFAULT_LOG_TYPE;
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
    /// Flag which informs CLI and JSON serializer whether to write log-type
    /// property and value to the runtime config file.
    /// When user doesn't provide the log-type property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(LogType))]
    public bool UserProvidedLogType { get; init; } = false;

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
