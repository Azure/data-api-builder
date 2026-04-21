// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel.Embeddings;

/// <summary>
/// Represents the chunking options for text processing before embedding.
/// Used to split large text inputs into smaller chunks for embedding.
/// </summary>
public record EmbeddingsChunkingOptions
{
    /// <summary>
    /// Default chunk size in characters.
    /// </summary>
    public const int DEFAULT_SIZE_CHARS = 1000;

    /// <summary>
    /// Default overlap size in characters between consecutive chunks.
    /// </summary>
    public const int DEFAULT_OVERLAP_CHARS = 250;

    /// <summary>
    /// Whether chunking is enabled. Defaults to false.
    /// When enabled, text inputs will be split into smaller chunks before embedding.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = false;

    /// <summary>
    /// The size of each chunk in characters.
    /// Defaults to 1000 characters.
    /// </summary>
    [JsonPropertyName("size-chars")]
    public int SizeChars { get; init; }

    /// <summary>
    /// The number of characters to overlap between consecutive chunks.
    /// Defaults to 250 characters.
    /// Overlap helps maintain context across chunk boundaries.
    /// </summary>
    [JsonPropertyName("overlap-chars")]
    public int OverlapChars { get; init; }

    [JsonConstructor]
    public EmbeddingsChunkingOptions(
        bool? Enabled = null,
        int? SizeChars = null,
        int? OverlapChars = null)
    {
        this.Enabled = Enabled ?? false;
        this.SizeChars = SizeChars ?? DEFAULT_SIZE_CHARS;
        this.OverlapChars = Math.Max(0, OverlapChars ?? DEFAULT_OVERLAP_CHARS);
    }

    /// <summary>
    /// Gets the effective chunk size, ensuring it's at least as large as the overlap.
    /// </summary>
    [JsonIgnore]
    public int EffectiveSizeChars => Math.Max(SizeChars, OverlapChars + 1);
}
