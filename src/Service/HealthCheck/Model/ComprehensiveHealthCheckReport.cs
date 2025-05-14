// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum HealthStatus
    {
        Healthy,
        Unhealthy
    }

    /// <summary>
    /// The health report of the DAB Engine.
    /// </summary>
    public record ComprehensiveHealthCheckReport
    {
        /// <summary>
        /// The health status of the service.
        /// </summary>
        [JsonPropertyName("status")]
        public HealthStatus Status { get; set; }

        /// <summary>
        /// The version of the service.
        /// </summary>
        [JsonPropertyName("version")]
        public string? Version { get; set; }

        /// <summary>
        /// The application name of the dab service.
        /// </summary>
        [JsonPropertyName("app-name")]
        public string? AppName { get; set; }

        /// <summary>
        /// The timestamp of the response.
        /// </summary>
        [JsonPropertyName("timestamp")]
        public DateTime TimeStamp { get; set; }

        /// <summary>
        /// The configuration details of the dab service.
        /// </summary>
        [JsonPropertyName("configuration")]
        public ConfigurationDetails? ConfigurationDetails { get; set; }

        /// <summary>
        /// The health check results of the dab service for data source and entities.
        /// </summary>
        [JsonPropertyName("checks")]
        public List<HealthCheckResultEntry>? Checks { get; set; }
    }
}
