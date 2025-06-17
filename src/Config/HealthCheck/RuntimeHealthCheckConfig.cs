// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record RuntimeHealthCheckConfig : HealthCheckConfig
{
    [JsonPropertyName("cache-ttl-seconds")]
    public int CacheTtlSeconds { get; set; }

    public HashSet<string>? Roles { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedTtlOptions { get; init; } = false;

    [JsonPropertyName("max-query-parallelism")]
    public int MaxQueryParallelism { get; set; } = EntityCacheOptions.DEFAULT_MAX_QUERY_PARALLELISM;

    public RuntimeHealthCheckConfig() : base()
    {
    }

    public RuntimeHealthCheckConfig(bool? enabled, HashSet<string>? roles = null, int? cacheTtlSeconds = null, int? maxQueryParallelism = null) : base(enabled)
    {
        this.Roles = roles;

        if (cacheTtlSeconds is not null)
        {
            this.CacheTtlSeconds = (int)cacheTtlSeconds;
            UserProvidedTtlOptions = true;
        }
        else
        {
            this.CacheTtlSeconds = EntityCacheOptions.DEFAULT_TTL_SECONDS;
        }

        this.MaxQueryParallelism = maxQueryParallelism ?? EntityCacheOptions.DEFAULT_MAX_QUERY_PARALLELISM;
    }
}
