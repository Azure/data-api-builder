// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record RuntimeHealthCheckConfig : HealthCheckConfig
{
    [JsonPropertyName("cache-ttl-seconds")]
    public int? CacheTtlSeconds { get; set; }

    [JsonPropertyName("roles")]
    public List<string>? Roles { get; set; }

    // TODO: Add support for parallel stream to run the health check query in upcoming PRs
    // public int MaxDop { get; set; } = 1; // Parallelized streams to run Health Check (Default: 1)

    public RuntimeHealthCheckConfig() : base()
    {
    }

    public RuntimeHealthCheckConfig(bool? Enabled, List<string>? Roles = null, int? CacheTtlSeconds = null) : base(Enabled)
    {
        this.Roles = Roles;
        this.CacheTtlSeconds = CacheTtlSeconds;
    }
}
