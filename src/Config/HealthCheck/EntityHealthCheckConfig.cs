// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel;

public record EntityHealthCheckConfig : HealthCheckConfig
{
    // This provides the ability to specify the 'x' first rows to be returned by the query.
    // Default is 100
    public int? First { get; set; } = 100;

    // The expected milliseconds the query took to be considered healthy.
    // (Default: 10000ms)
    [JsonPropertyName("threshold-ms")]
    public int? ThresholdMs { get; set; }
}
