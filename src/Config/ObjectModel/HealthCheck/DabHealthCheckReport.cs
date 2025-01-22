// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
    /// The health report of the DAB Enigne.
    /// </summary>
    public record DabHealthCheckReport
    {
        /// <summary>
        /// The health status of the service.
        /// </summary>
        public HealthStatus HealthStatus { get; init; }

        /// <summary>
        /// The version of the service.
        /// </summary>
        public string? Version { get; set; }

        /// <summary>
        /// The name of the dab service.
        /// </summary>
        public string? AppName { get; set; }

        /// <summary>
        /// The configuration details of the dab service.
        /// </summary>
        public DabConfigurationDetails? DabConfigurationDetails { get; set; }

        /// <summary>
        /// The health check results of the dab service.
        /// </summary>
        public DabHealthCheckResults? HealthCheckResults { get; set; }

        /// <summary>
        /// The health check results of the dab service in output format.
        /// </summary>
        public DabHealthCheckResultFormat? Checks { get; set; }
    }

    public class DabHealthCheckResultFormat
    {
        public Dictionary<string, HealthCheckDetailsResultEntry>? DataSourceHealthCheckResults { get; set; }

        public Dictionary<string, Dictionary<string, HealthCheckDetailsResultEntry>>? EntityHealthCheckResults { get; set; }        
    }
}
