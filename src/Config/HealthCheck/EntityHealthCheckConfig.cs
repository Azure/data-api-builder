// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.HealthCheck;

namespace Azure.DataApiBuilder.Config.ObjectModel;

[JsonConverter(typeof(EntityHealthOptionsConvertorFactory))]
public record EntityHealthCheckConfig : HealthCheckConfig
{
    // Used to specify the 'x' first rows to be returned by the query.
    // Filter for REST and GraphQL queries to fetch only the first 'x' rows.
    // Default is 100
    public int First { get; set; }

    // The expected milliseconds the query took to be considered healthy.
    // If the query takes equal or longer than this value, the health check will be considered unhealthy.
    // (Default: 1000ms)
    [JsonPropertyName("threshold-ms")]
    public int ThresholdMs { get; set; }

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(First))]
    public bool UserProvidedFirst { get; set; } = false;

    [JsonIgnore(Condition = JsonIgnoreCondition.Always)]
    [MemberNotNullWhen(true, nameof(ThresholdMs))]
    public bool UserProvidedThresholdMs { get; set; } = false;

    public EntityHealthCheckConfig() : base()
    {
        First = HealthCheckConstants.DEFAULT_FIRST_VALUE;
        ThresholdMs = HealthCheckConstants.DEFAULT_THRESHOLD_RESPONSE_TIME_MS;
    }

    public EntityHealthCheckConfig(bool? Enabled, int? First = null, int? ThresholdMs = null) : base(Enabled)
    {
        if (First is not null)
        {
            this.First = (int)First;
            UserProvidedFirst = true;
        }
        else
        {
            this.First = HealthCheckConstants.DEFAULT_FIRST_VALUE;
        }

        if (ThresholdMs is not null)
        {
            this.ThresholdMs = (int)ThresholdMs;
            UserProvidedThresholdMs = true;
        }
        else
        {
            this.ThresholdMs = HealthCheckConstants.DEFAULT_THRESHOLD_RESPONSE_TIME_MS;
        }
    }
}
