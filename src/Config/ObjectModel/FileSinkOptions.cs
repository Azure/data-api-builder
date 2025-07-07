// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for configuring file sink telemetry.
/// </summary>
public record FileSinkOptions(
    bool Enabled = false,
    string? Path = "/logs/dab-log.txt",
    RollingIntervalMode? RollingInterval = RollingIntervalMode.Day,
    int? RetainedFileCountLimit = 1,
    int? FileSizeLimitBytes = 1048576)
{
    public bool Enabled { get; init; } = Enabled;

    public string? Path { get; init; } = Path;

    public RollingIntervalMode? RollingInterval { get; init; } = RollingInterval;

    public int? RetainedFileCountLimit { get; init; } = RetainedFileCountLimit;

    public int? FileSizeLimitBytes { get; init; } = FileSizeLimitBytes;
}
