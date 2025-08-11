// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the options for configuring file logging.
/// Properties are nullable to support DAB CLI merge config
/// expected behavior.
/// </summary>
public record FileOptions
{
    /// <summary>
    /// Default enabled for file logging.
    /// </summary>
    public const bool DEFAULT_ENABLED = false;

    /// <summary>
    /// Default path for file logging.
    /// </summary>
    public const string DEFAULT_PATH = "/logs/dab-log.txt";

    /// <summary>
    /// Default rolling interval for file logging.
    /// </summary>
    public const string DEFAULT_ROLLING_INTERVAL = "Day";

    /// <summary>
    /// Default retained file count limit.
    /// </summary>
    public const int DEFAULT_RETAINED_FILE_COUNT_LIMIT = 1;

    /// <summary>
    /// Default file size limit in bytes.
    /// </summary>
    public const int DEFAULT_FILE_SIZE_LIMIT_BYTES = 1048576;

    /// <summary>
    /// Whether file logging is enabled.
    /// </summary>
    public bool Enabled { get; init; } = DEFAULT_ENABLED;

    /// <summary>
    /// Path where log files are written.
    /// </summary>
    public string? Path { get; init; } = DEFAULT_PATH;

    /// <summary>
    /// Rolling interval for log files.
    /// </summary>
    public string? RollingInterval { get; init; } = DEFAULT_ROLLING_INTERVAL;

    /// <summary>
    /// Maximum number of retained files.
    /// </summary>
    public int? RetainedFileCountLimit { get; init; } = DEFAULT_RETAINED_FILE_COUNT_LIMIT;

    /// <summary>
    /// Maximum file size limit in bytes.
    /// </summary>
    public int? FileSizeLimitBytes { get; init; } = DEFAULT_FILE_SIZE_LIMIT_BYTES;

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
    /// Flag which informs CLI and JSON serializer whether to write path
    /// property and value to the runtime config file.
    /// When user doesn't provide the path property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Path))]
    public bool UserProvidedPath { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write rolling-interval
    /// property and value to the runtime config file.
    /// When user doesn't provide the rolling-interval property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(RollingInterval))]
    public bool UserProvidedRollingInterval { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write retained-file-count-limit
    /// property and value to the runtime config file.
    /// When user doesn't provide the retained-file-count-limit property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(RetainedFileCountLimit))]
    public bool UserProvidedRetainedFileCountLimit { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write file-size-limit-bytes
    /// property and value to the runtime config file.
    /// When user doesn't provide the file-size-limit-bytes property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(FileSizeLimitBytes))]
    public bool UserProvidedFileSizeLimitBytes { get; init; } = false;
}