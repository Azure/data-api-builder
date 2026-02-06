// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Represents the configuration options for semantic caching.
/// Properties are nullable to support DAB CLI merge config expected behavior.
/// </summary>
public record SemanticCacheOptions
{
    /// <summary>
    /// Default similarity threshold value.
    /// </summary>
    public const double DEFAULT_SIMILARITY_THRESHOLD = 0.85;

    /// <summary>
    /// Default max results value.
    /// </summary>
    public const int DEFAULT_MAX_RESULTS = 5;

    /// <summary>
    /// Default expire seconds value (1 day).
    /// </summary>
    public const int DEFAULT_EXPIRE_SECONDS = 86400;

    /// <summary>
    /// Global on/off switch for semantic caching.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; } = false;

    /// <summary>
    /// Minimum cosine similarity required to consider a cache hit.
    /// Typical values: 0.80 – 0.90
    /// </summary>
    [JsonPropertyName("similarity-threshold")]
    public double? SimilarityThreshold { get; init; } = null;

    /// <summary>
    /// Number of nearest neighbors to retrieve from Redis vector search.
    /// </summary>
    [JsonPropertyName("max-results")]
    public int? MaxResults { get; init; } = null;

    /// <summary>
    /// Time-to-live for cached responses in seconds.
    /// </summary>
    [JsonPropertyName("expire-seconds")]
    public int? ExpireSeconds { get; init; } = null;

    /// <summary>
    /// Azure Managed Redis-specific settings.
    /// </summary>
    [JsonPropertyName("azure-managed-redis")]
    public AzureManagedRedisOptions? AzureManagedRedis { get; init; } = null;

    /// <summary>
    /// Embedding provider configuration.
    /// </summary>
    [JsonPropertyName("embedding-provider")]
    public EmbeddingProviderOptions? EmbeddingProvider { get; init; } = null;

    [JsonConstructor]
    public SemanticCacheOptions(
        bool? enabled = null,
        double? similarityThreshold = null,
        int? maxResults = null,
        int? expireSeconds = null,
        AzureManagedRedisOptions? azureManagedRedis = null,
        EmbeddingProviderOptions? embeddingProvider = null)
    {
        this.Enabled = enabled;

        // Only set values and flags when explicitly provided (not null)
        if (similarityThreshold is not null)
        {
            this.SimilarityThreshold = similarityThreshold;
            UserProvidedSimilarityThreshold = true;
        }
        else
        {
            this.SimilarityThreshold = null; // Keep null when not provided
            UserProvidedSimilarityThreshold = false;
        }

        if (maxResults is not null)
        {
            this.MaxResults = maxResults;
            UserProvidedMaxResults = true;
        }
        else
        {
            this.MaxResults = null; // Keep null when not provided
            UserProvidedMaxResults = false;
        }

        if (expireSeconds is not null)
        {
            this.ExpireSeconds = expireSeconds;
            UserProvidedExpireSeconds = true;
        }
        else
        {
            this.ExpireSeconds = null; // Keep null when not provided
            UserProvidedExpireSeconds = false;
        }

        this.AzureManagedRedis = azureManagedRedis;
        this.EmbeddingProvider = embeddingProvider;
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write similarity-threshold
    /// property and value to the runtime config file.
    /// When user doesn't provide the similarity-threshold property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(SimilarityThreshold))]
    public bool UserProvidedSimilarityThreshold { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write max-results
    /// property and value to the runtime config file.
    /// When user doesn't provide the max-results property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(MaxResults))]
    public bool UserProvidedMaxResults { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write expire-seconds
    /// property and value to the runtime config file.
    /// When user doesn't provide the expire-seconds property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(ExpireSeconds))]
    public bool UserProvidedExpireSeconds { get; init; } = false;
}
