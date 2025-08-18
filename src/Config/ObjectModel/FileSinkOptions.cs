// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for configuring file sink telemetry.
/// </summary>
public record FileSinkOptions
{
    /// <summary>
    /// Default enabled for File Sink.
    /// </summary>
    public const bool DEFAULT_ENABLED = false;

    /// <summary>
    /// Default path for File Sink.
    /// </summary>
    public const string DEFAULT_PATH = "/logs/dab-log.txt";

    /// <summary>
    /// Default rolling interval for File Sink.
    /// </summary>
    public const string DEFAULT_ROLLING_INTERVAL = nameof(RollingIntervalMode.Day);

    /// <summary>
    /// Default retained file count limit for File Sink.
    /// </summary>
    public const int DEFAULT_RETAINED_FILE_COUNT_LIMIT = 1;

    /// <summary>
    /// Default file size limit bytes for File Sink.
    /// </summary>
    public const int DEFAULT_FILE_SIZE_LIMIT_BYTES = 1048576;

    /// <summary>
    /// Whether File Sink is enabled.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>
    /// Path to the file where logs will be uploaded.
    /// </summary>
    public string? Path { get; init; }

    /// <summary>
    /// Time it takes for files with logs to be discarded.
    /// </summary>
    public string? RollingInterval { get; init; }

    /// <summary>
    /// Amount of files that can exist simultaneously in which logs are saved.
    /// </summary>
    public int? RetainedFileCountLimit { get; init; }

    /// <summary>
    /// File size limit in bytes before a new file needs to be created.
    /// </summary>
    public int? FileSizeLimitBytes { get; init; }

    [JsonConstructor]
    public FileSinkOptions(bool? enabled = null, string? path = null, RollingIntervalMode? rollingInterval = null, int? retainedFileCountLimit = null, int? fileSizeLimitBytes = null)
    {
        if (enabled is not null)
        {
            Enabled = (bool)enabled;
            UserProvidedEnabled = true;
        }
        else
        {
            Enabled = DEFAULT_ENABLED;
        }

        if (path is not null)
        {
            Path = path;
            UserProvidedPath = true;
        }
        else
        {
            Path = DEFAULT_PATH;
        }

        if (rollingInterval is not null)
        {
            RollingInterval = rollingInterval.ToString();
            UserProvidedRollingInterval = true;
        }
        else
        {
            RollingInterval = DEFAULT_ROLLING_INTERVAL;
        }

        if (retainedFileCountLimit is not null)
        {
            RetainedFileCountLimit = retainedFileCountLimit;
            UserProvidedRetainedFileCountLimit = true;
        }
        else
        {
            RetainedFileCountLimit = DEFAULT_RETAINED_FILE_COUNT_LIMIT;
        }

        if (fileSizeLimitBytes is not null)
        {
            FileSizeLimitBytes = fileSizeLimitBytes;
            UserProvidedFileSizeLimitBytes = true;
        }
        else
        {
            FileSizeLimitBytes = DEFAULT_FILE_SIZE_LIMIT_BYTES;
        }
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write enabled
    /// property/value to the runtime config file.
    /// When user doesn't provide the enabled property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Enabled))]
    public bool UserProvidedEnabled { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write path
    /// property/value to the runtime config file.
    /// When user doesn't provide the path property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Path))]
    public bool UserProvidedPath { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write rolling-interval
    /// property/value to the runtime config file.
    /// When user doesn't provide the rolling-interval property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(RollingInterval))]
    public bool UserProvidedRollingInterval { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write retained-file-count-limit
    /// property/value to the runtime config file.
    /// When user doesn't provide the retained-file-count-limit property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(RetainedFileCountLimit))]
    public bool UserProvidedRetainedFileCountLimit { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write file-size-limit-bytes
    /// property/value to the runtime config file.
    /// When user doesn't provide the file-size-limit-bytes property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(FileSizeLimitBytes))]
    public bool UserProvidedFileSizeLimitBytes { get; init; } = false;
}
