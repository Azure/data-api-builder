// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Entity specific cache configuration.
/// Properties are nullable to support DAB CLI merge config
/// expected behavior.
/// </summary>
public record EntityCacheOptions
{
    /// <summary>
    /// Default ttl value for an entity.
    /// </summary>
    public const int DEFAULT_TTL_SECONDS = 5;

    /// <summary>
    /// Default cache level for an entity.
    /// </summary>
    public const EntityCacheLevel DEFAULT_LEVEL = EntityCacheLevel.L1L2;

    /// <summary>
    /// The L2 cache provider we support.
    /// </summary>
    public const string L2_CACHE_PROVIDER = "redis";

    /// <summary>
    /// Whether the cache should be used for the entity.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; } = false;

    /// <summary>
    /// The number of seconds a cache entry is valid before eligible for cache eviction.
    /// </summary>
    [JsonPropertyName("ttl-seconds")]
    public int? TtlSeconds { get; init; } = null;

    /// <summary>
    /// The cache levels to use for a cache entry.
    /// </summary>
    [JsonPropertyName("level")]
    public EntityCacheLevel? Level { get; init; } = null;

    [JsonConstructor]
    public EntityCacheOptions(bool? Enabled = null, int? TtlSeconds = null, EntityCacheLevel? Level = null)
    {
        // TODO: shouldn't we apply the same "UserProvidedXyz" logic to Enabled, too?
        this.Enabled = Enabled;

        if (TtlSeconds is not null)
        {
            this.TtlSeconds = TtlSeconds;
            UserProvidedTtlOptions = true;
        }
        else
        {
            this.TtlSeconds = DEFAULT_TTL_SECONDS;
        }

        if (Level is not null)
        {
            this.Level = Level;
            UserProvidedLevelOptions = true;
        }
        else
        {
            this.Level = DEFAULT_LEVEL;
        }
    }

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write ttl-seconds
    /// property and value to the runtime config file.
    /// When user doesn't provide the ttl-seconds property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// This is because the user's intent is to use DAB's default value which could change
    /// and DAB CLI writing the property and value would lose the user's intent.
    /// This is because if the user were to use the CLI created config, a ttl-seconds
    /// property/value specified would be interpreted by DAB as "user explicitly set ttl."
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(TtlSeconds))]
    public bool UserProvidedTtlOptions { get; init; } = false;

    /// <summary>
    /// Flag which informs CLI and JSON serializer whether to write the Level option
    /// property and value to the runtime config file.
    /// When user doesn't provide the level property/value, which signals DAB to use the default,
    /// the DAB CLI should not write the default value to a serialized config.
    /// This is because the user's intent is to use DAB's default value which could change
    /// and DAB CLI writing the property and value would lose the user's intent.
    /// This is because if the user were to use the CLI created config, a level
    /// property/value specified would be interpreted by DAB as "user explicitly set level."
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Level))]
    public bool UserProvidedLevelOptions { get; init; } = false;
}
