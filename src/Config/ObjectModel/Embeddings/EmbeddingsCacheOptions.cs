// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel.Embeddings;

/// <summary>
/// Embeddings-specific cache configuration.
/// Properties are nullable to support DAB CLI merge config expected behavior.
/// </summary>
public record EmbeddingsCacheOptions
{
    /// <summary>
    /// Default TTL for embedding cache entries in hours.
    /// Set high since embeddings are deterministic and don't get outdated.
    /// </summary>
    public const int DEFAULT_TTL_HOURS = 24;

    /// <summary>
    /// Whether caching is enabled for embeddings.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; } = true;

    /// <summary>
    /// The time-to-live for cached embeddings in hours.
    /// </summary>
    [JsonPropertyName("ttl-hours")]
    public int? TtlHours { get; init; } = null;

    /// <summary>
    /// The options for the level2 cache (distributed Azure Managed Redis cache).
    /// </summary>
    [JsonPropertyName("level-2")]
    public EmbeddingsCacheLevel2Options? Level2 { get; init; } = null;

    [JsonConstructor]
    public EmbeddingsCacheOptions(bool? Enabled = true, int? TtlHours = null, EmbeddingsCacheLevel2Options? Level2 = null)
    {
        this.Enabled = Enabled;
        this.Level2 = Level2;

        if (TtlHours is not null)
        {
            this.TtlHours = TtlHours;
            UserProvidedTtlHours = true;
        }
        else
        {
            this.TtlHours = DEFAULT_TTL_HOURS;
        }
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write ttl-hours
    /// property and value to the runtime config file.
    /// When user doesn't provide the ttl-hours property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(TtlHours))]
    public bool UserProvidedTtlHours { get; init; } = false;

    /// <summary>
    /// Gets the effective TTL in hours. The constructor guarantees TtlHours is set
    /// (defaults to <see cref="DEFAULT_TTL_HOURS"/> when not user-provided).
    /// </summary>
    [JsonIgnore]
    public int EffectiveTtlHours => TtlHours!.Value;

    /// <summary>
    /// Returns true if L2 (distributed) cache is enabled.
    /// </summary>
    [JsonIgnore]
    public bool IsLevel2Enabled => Level2?.Enabled ?? false;
}
