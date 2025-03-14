// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.HealthCheck;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record EntityHealthCheckConfig : HealthCheckConfig
{
    // Used to specify the 'x' first rows to be returned by the query.
    // Filter for REST and GraphQL queries to fetch only the first 'x' rows.
    // Default is 100
    public int First { get; set; } = HealthCheckConstants.DefaultFirstValue;

    // The expected milliseconds the query took to be considered healthy.
    // If the query takes equal or longer than this value, the health check will be considered unhealthy.
    // (Default: 10000ms)
    [JsonPropertyName("threshold-ms")]
    public int ThresholdMs { get; set; } = HealthCheckConstants.DefaultThresholdResponseTimeMs;
}
