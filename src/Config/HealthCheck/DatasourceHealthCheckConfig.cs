// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.HealthCheck;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record DatasourceHealthCheckConfig : HealthCheckConfig
{
    // The identifier or simple name of the data source to be checked.
    // Required to identify data-sources in case of multiple config files.
    public string? Name { get; set; }

    // The expected milliseconds the query took to be considered healthy.
    // If the query takes equal or longer than this value, the health check will be considered unhealthy.
    // (Default: 10000ms)
    [JsonPropertyName("threshold-ms")]
    public int ThresholdMs { get; set; } = HealthCheckConstants.DefaultThresholdResponseTimeMs;
}
