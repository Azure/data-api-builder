// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Configuration options for HTTP response compression.
/// </summary>
public record CompressionOptions
{
    /// <summary>
    /// Default compression level is Optimal.
    /// </summary>
    public const CompressionLevel DEFAULT_LEVEL = CompressionLevel.Optimal;

    /// <summary>
    /// The compression level to use for HTTP response compression.
    /// </summary>
    [JsonPropertyName("level")]
    public CompressionLevel Level { get; init; } = DEFAULT_LEVEL;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write Level
    /// property and value to the runtime config file.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedLevel { get; init; } = false;

    [JsonConstructor]
    public CompressionOptions(CompressionLevel Level = DEFAULT_LEVEL)
    {
        this.Level = Level;
        this.UserProvidedLevel = true;
    }

    /// <summary>
    /// Default parameterless constructor for cases where no compression level is specified.
    /// </summary>
    public CompressionOptions()
    {
        this.Level = DEFAULT_LEVEL;
        this.UserProvidedLevel = false;
    }
}
