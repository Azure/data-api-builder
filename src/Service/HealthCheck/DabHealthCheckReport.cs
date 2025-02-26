// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Azure.DataApiBuilder.Config.ObjectModel
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
    public record DabHealthCheckReport
    {
        /// <summary>
        /// The health status of the service.
        /// </summary>
        [JsonPropertyName("status")]
        public HealthStatus Status { get; init; }

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
        /// The configuration details of the dab service.
        /// </summary>
        [JsonPropertyName("configuration")]
        public DabConfigurationDetails? ConfigurationDetails { get; set; }

        /// <summary>
        /// The health check results of the dab service for data source and entities.
        /// </summary>
        [JsonPropertyName("checks")]
        public List<HealthCheckResultEntry>? Checks { get; set; }
    }
}
