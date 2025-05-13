// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record RuntimeHealthCheckConfig : HealthCheckConfig
{
    [JsonPropertyName("cache-ttl-seconds")]
    public int CacheTtlSeconds { get; set; }

    public HashSet<string>? Roles { get; set; }

    // TODO: Add support for parallel stream to run the health check query in upcoming PRs
    // public int MaxDop { get; set; } = 1; // Parallelized streams to run Health Check (Default: 1)

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedTtlOptions { get; init; } = false;

    public RuntimeHealthCheckConfig() : base()
    {
    }

    public RuntimeHealthCheckConfig(bool? enabled, HashSet<string>? roles = null, int? cacheTtlSeconds = null) : base(enabled)
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
    }
}
