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
    /// The connection string for the Redis L2 cache.
    /// </summary>
    [JsonPropertyName("connection-string")]
    public string? ConnectionString { get; init; } = null;

    [JsonConstructor]
    public RuntimeCacheLevel2Options(bool? Enabled = null, string? ConnectionString = null)
    {
        this.Enabled = Enabled;

        this.ConnectionString = ConnectionString;
    }
}
