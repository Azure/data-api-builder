// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for configuring file sink telemetry.
/// </summary>
public record FileOptions(
    bool Enabled = false,
    string Path = "/logs/dab-log.txt",
    RollingInterval RollingInterval = RollingInterval.Day,
    int RetainedFileCountLimit = 1,
    int FileSizeLimitBytes = 1048576)
{
    [JsonPropertyName("rolling-interval")]
    public RollingInterval RollingInterval { get; init; } = RollingInterval;

    [JsonPropertyName("retained-file-count-limit")]
    public int RetainedFileCountLimit { get; init; } = RetainedFileCountLimit;

    [JsonPropertyName("file-size-limit-bytes")]
    public int FileSizeLimitBytes { get; init; } = FileSizeLimitBytes;
}

/// <summary>
/// Represents the rolling interval options for file sink.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RollingInterval
{
    Hour,
    Day,
    Week
}