// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    /// <summary>
    /// The Health Check Entry Object
    /// </summary>
    public class HealthCheckResultEntry
    {
        [JsonPropertyName("status")]
        public HealthStatus Status { get; init; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("exception")]
        public string? Exception { get; init; }

        [JsonPropertyName("tags")]
        public List<string>? Tags { get; init; }

        [JsonPropertyName("data")]
        public ResponseTimeData? ResponseTimeData { get; init; }
    }

    public class ResponseTimeData
    {
        [JsonPropertyName("duration-ms")]
        public int? DurationMs { get; set; }

        [JsonPropertyName("threshold-ms")]
        public int? ThresholdMs { get; set; }
    }
}
