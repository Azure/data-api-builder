// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record DabHealthCheckConfig
{
    public bool Enabled { get; set; } // Default value: false

    // The moniker or simple name of the data source to be checked.
    // Required when there is a multiple data source scenario.
    // TODO: Add validity support for when multiple data sources
    public string? Moniker { get; set; }

    // The query to be executed to check the health of the data source.
    // "query: "SELECT TOP 1 1"
    public string? Query { get; set; }

    // This provides the ability to specify the 'x' first rows to be returned by the query.
    // Default is 1
    public int? First { get; set; } = 1;

    // The expected milliseconds the query took to be considered healthy.
    // (Default: 10000ms)
    [JsonPropertyName("threshold-ms")]
    public int? ThresholdMs { get; set; }

    // TODO: Add support for "roles": ["anonymous", "authenticated"]
    // public string[] Roles { get; set; } = new string[] { "*" };
    // TODO: Add support for parallel stream to run the health check query
    // public int MaxDop { get; set; } = 1; // Parallelized streams to run Health Check (Default: 1)
}
