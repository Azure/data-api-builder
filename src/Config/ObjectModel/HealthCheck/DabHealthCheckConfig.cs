// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record DabHealthCheckConfig
{
    public bool Enabled { get; set; } // Default value: false
    public string? Moniker { get; set; }
    public string? Query { get; set; } // "query: "SELECT TOP 1 1"
    
    [JsonPropertyName("threshold-ms")]
    public int? ThresholdMs { get; set; } // (Default: 10000ms)
    public string Role { get; set; } = "*"; // "roles": ["anonymous", "authenticated"] (Default: *)
    
    [JsonPropertyName("max-dop")]
    public int MaxDop { get; set; } = 1; // Parallelized streams to run Health Check (Default: 1)
}