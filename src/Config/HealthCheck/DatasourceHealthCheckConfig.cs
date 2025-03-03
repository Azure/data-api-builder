// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record DatasourceHealthCheckConfig : HealthCheckConfig
{
    // The identifier or simple name of the data source to be checked.
    // Required to identify data sources in case of multiple.
    public string? Name { get; set; }

    // The expected milliseconds the query took to be considered healthy.
    // (Default: 10000ms)
    [JsonPropertyName("threshold-ms")]
    public int ThresholdMs { get; set; } = 1000;
}
