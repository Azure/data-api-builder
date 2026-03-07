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
    /// Placeholder cache level value used when the entity does not explicitly set a level.
    /// This value is stored on the EntityCacheOptions object but is NOT used at runtime
    /// for resolution — GetEntityCacheEntryLevel() falls through to GlobalCacheEntryLevel()
    /// (which infers the level from the runtime Level2 configuration) when UserProvidedLevelOptions is false.
    /// </summary>
    public const EntityCacheLevel DEFAULT_LEVEL = EntityCacheLevel.L1L2;

    /// <summary>
    /// The L2 cache provider we support.
    /// </summary>
    public const string L2_CACHE_PROVIDER = "redis";

    /// <summary>
    /// Whether the cache should be used for the entity.
    /// When null after deserialization, indicates the user did not explicitly set this property,
    /// and the entity should inherit the runtime-level cache enabled setting.
    /// After ResolveEntityCacheInheritance runs, this will hold the resolved value
    /// (inherited from runtime or explicitly set by user). Use UserProvidedEnabledOptions
    /// to distinguish whether the value was user-provided or inherited.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; }

    /// <summary>
    /// The number of seconds a cache entry is valid before eligible for cache eviction.
    /// </summary>
    [JsonPropertyName("ttl-seconds")]
    public int? TtlSeconds { get; init; }

    /// <summary>
    /// The cache levels to use for a cache entry.
    /// </summary>
    [JsonPropertyName("level")]
    public EntityCacheLevel? Level { get; init; }

    [JsonConstructor]
    public EntityCacheOptions(bool? Enabled = null, int? TtlSeconds = null, EntityCacheLevel? Level = null)
    {
        if (Enabled is not null)
        {
            this.Enabled = Enabled;
            UserProvidedEnabledOptions = true;
        }
        else
        {
            this.Enabled = null;
        }

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
    /// Flag which informs CLI and JSON serializer whether to write the enabled
    /// property and value to the runtime config file.
    /// When the user doesn't provide the enabled property/value, which signals DAB
    /// to inherit from the runtime cache setting, the DAB CLI should not write the
    /// inherited value to a serialized config. This preserves the user's intent to
    /// inherit rather than explicitly set the value.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(Enabled))]
    public bool UserProvidedEnabledOptions { get; init; } = false;

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
