// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record RuntimeHealthCheckConfig : HealthCheckConfig
{
    // TODO: Add support for caching in upcoming PRs
    // public int cache-ttl-seconds { get; set; };

    public HashSet<string>? Roles { get; set; }

    // TODO: Add support for parallel stream to run the health check query in upcoming PRs
    // public int MaxDop { get; set; } = 1; // Parallelized streams to run Health Check (Default: 1)

    public RuntimeHealthCheckConfig() : base()
    {
    }

    public RuntimeHealthCheckConfig(bool? Enabled, HashSet<string>? Roles = null) : base(Enabled)
    {
        this.Roles = Roles;
    }
}
