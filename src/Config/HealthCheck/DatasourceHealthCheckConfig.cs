// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.HealthCheck;

namespace Azure.DataApiBuilder.Config.ObjectModel;

[JsonConverter(typeof(DataSourceHealthOptionsConvertorFactory))]
public record DatasourceHealthCheckConfig : HealthCheckConfig
{
    // The identifier or simple name of the data source to be checked.
    // Required to identify data-sources in case of multiple config files.
    public string? Name { get; set; }

    // The expected milliseconds the query took to be considered healthy.
    // If the query takes equal or longer than this value, the health check will be considered unhealthy.
    // (Default: 1000ms)
    [JsonPropertyName("threshold-ms")]
    public int ThresholdMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    public bool UserProvidedThresholdMs { get; set; } = false;

    public DatasourceHealthCheckConfig() : base()
    {
        ThresholdMs = HealthCheckConstants.DEFAULT_THRESHOLD_RESPONSE_TIME_MS;
    }

    public DatasourceHealthCheckConfig(bool? enabled, string? name = null, int? thresholdMs = null) : base(enabled)
    {
        this.Name = name;

        if (thresholdMs is not null)
        {
            this.ThresholdMs = (int)thresholdMs;
            UserProvidedThresholdMs = true;
        }
        else
        {
            this.ThresholdMs = HealthCheckConstants.DEFAULT_THRESHOLD_RESPONSE_TIME_MS;
        }
    }
}
