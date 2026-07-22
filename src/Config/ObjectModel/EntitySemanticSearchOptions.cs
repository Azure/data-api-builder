// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Entity specific semantic search configuration.
/// </summary>
public record EntitySemanticSearchOptions
{
    public const int DEFAULT_REDIS_INDEX_MULTIPLIER = 2;
    public const double DEFAULT_SIMILARITY_THRESHOLD = 0.8;
    public const string DEFAULT_REDIS_INDEX_TYPE = "hash";
    public const string DEFAULT_INPUT_DESCRIPTION = "Natural language value used for semantic search.";
    public const string DEFAULT_OUTPUT_DESCRIPTION = "Semantic distance score returned by semantic search.";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = false;

    [JsonPropertyName("redis-index-name")]
    public string? RedisIndexName { get; init; }

    [JsonPropertyName("redis-index-type")]
    public string RedisIndexType { get; init; } = DEFAULT_REDIS_INDEX_TYPE;

    [JsonPropertyName("redis-index-multiplier")]
    public int RedisIndexMultiplier { get; init; } = DEFAULT_REDIS_INDEX_MULTIPLIER;

    [JsonPropertyName("similarity-threshold")]
    public double SimilarityThreshold { get; init; } = DEFAULT_SIMILARITY_THRESHOLD;

    [JsonPropertyName("input-description")]
    public string InputDescription { get; init; } = DEFAULT_INPUT_DESCRIPTION;

    [JsonPropertyName("output-description")]
    public string OutputDescription { get; init; } = DEFAULT_OUTPUT_DESCRIPTION;
}
