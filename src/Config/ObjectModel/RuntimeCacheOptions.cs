// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

/// <summary>
/// Runtime specific cache configuration.
/// Properties are nullable to support DAB CLI merge config
/// expected behavior.
/// </summary>
public record RuntimeCacheOptions
{
    /// <summary>
    /// Default ttl value for an entity.
    /// </summary>
    public const int DEFAULT_TTL_SECONDS = 5;

    /// <summary>
    /// Whether the level2 cache should be used.
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; init; } = false;

    /// <summary>
    /// The number of seconds a cache entry is valid before eligible for cache eviction.
    /// </summary>
    [JsonPropertyName("ttl-seconds")]
    public int? TtlSeconds { get; init; } = null;

    /// <summary>
    /// The options for the level2 cache (and backplane).
    /// </summary>
    [JsonPropertyName("level-2")]
    public RuntimeCacheLevel2Options? Level2 { get; init; } = null;

    [JsonConstructor]
    public RuntimeCacheOptions(bool? Enabled = null, int? TtlSeconds = null)
    {
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
}
