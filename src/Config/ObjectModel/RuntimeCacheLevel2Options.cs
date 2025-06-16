// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Runtime specific level2 cache configuration.
/// Properties are nullable to support DAB CLI merge config
/// expected behavior.
/// </summary>
public record RuntimeCacheLevel2Options
{
    /// <summary>
    /// Whether the cache should be used.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; } = false;

    /// <summary>
    /// The provider for the L2 cache. Currently only "redis" is supported.
    /// </summary>
    [JsonPropertyName("provider")]
    public string? Provider { get; init; } = null;

    /// <summary>
    /// The connection string for the level2 cache.
    /// </summary>
    [JsonPropertyName("connection-string")]
    public string? ConnectionString { get; init; } = null;

    /// <summary>
    /// The prefix to use for the cache keys in level2 + backplane: useful in a shared environment (eg: a shared Redis instance) to avoid collisions of cache keys or the backplane channel.
    /// </summary>
    [JsonPropertyName("partition")]
    public string? Partition { get; init; } = null;

    [JsonConstructor]
    public RuntimeCacheLevel2Options(bool? Enabled = null, string? Provider = null, string? ConnectionString = null, string? Partition = null)
    {
        this.Enabled = Enabled;

        this.Provider = Provider;

        this.ConnectionString = ConnectionString;

        this.Partition = Partition;
    }
}
