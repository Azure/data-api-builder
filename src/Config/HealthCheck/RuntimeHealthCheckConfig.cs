// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record RuntimeHealthCheckConfig : HealthCheckConfig
{
    /// <summary>
    /// Represents the lowest maximum query parallelism for health check.
    /// </summary>
    public const int LOWEST_MAX_QUERY_PARALLELISM = 1;

    /// <summary>
    /// Default maximum query parallelism for health check.
    /// </summary>
    public const int DEFAULT_MAX_QUERY_PARALLELISM = 4;

    /// <summary>
    /// Upper limit of maximum query parallelism for health check.
    /// </summary>
    public const int UPPER_LIMIT_MAX_QUERY_PARALLELISM = 8;

    [JsonPropertyName("cache-ttl-seconds")]
    public int CacheTtlSeconds { get; set; }

    public HashSet<string>? Roles { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedTtlOptions { get; init; } = false;

    /// <summary>
    /// Flag to indicate if the user has provided a value for MaxQueryParallelism.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedMaxQueryParallelism { get; init; } = false;

    /// <summary>
    /// Gets or sets the maximum number of queries that can be executed in parallel.
    /// </summary>
    [JsonPropertyName("max-query-parallelism")]
    public int? MaxQueryParallelism { get; set; }

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

        // Allow user to set values between 1 and 8 (inclusive). If not set, the value will be set to 4 during health check.
        if (maxQueryParallelism is not null)
        {
            this.MaxQueryParallelism = maxQueryParallelism;
            UserProvidedMaxQueryParallelism = true;
        }
        else
        {
            this.MaxQueryParallelism = DEFAULT_MAX_QUERY_PARALLELISM;
        }

    }
}
