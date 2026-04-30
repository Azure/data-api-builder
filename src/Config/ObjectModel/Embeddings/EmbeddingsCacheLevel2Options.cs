// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel.Embeddings;

/// <summary>
/// Embeddings-specific level2 (distributed) cache configuration for Redis.
/// Properties are nullable to support DAB CLI merge config expected behavior.
/// </summary>
public record EmbeddingsCacheLevel2Options
{
    /// <summary>
    /// Whether the L2 distributed Redis cache should be used for embeddings.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; } = false;

    /// <summary>
    /// The connection string for Azure Cache for Redis.
    /// Example: "contoso.redis.cache.windows.net:6380,password=...,ssl=True,abortConnect=False"
    /// </summary>
    [JsonPropertyName("connection-string")]
    public string? ConnectionString { get; init; } = null;

    [JsonConstructor]
    public EmbeddingsCacheLevel2Options(
        bool? Enabled = null,
        string? ConnectionString = null)
    {
        this.Enabled = Enabled;
        this.ConnectionString = ConnectionString;
    }
}
