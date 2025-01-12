// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    /// <summary>
    /// The health report of the DAB Enigne.
    /// </summary>
    public record DabHealthCheckResults
    {
        public List<HealthCheckResultEntry>? DataSourceHealthCheckResults { get; init; }
        public List<HealthCheckResultEntry>? EntityHealthCheckResults { get; init; }
    }

    public class HealthCheckResultEntry
    {
        public string? Name { get; init; }
        public HealthStatus HealthStatus { get; init; }
        public string? Description { get; init; }
        public string? Exception { get; init; }
        public ResponseTimeData? ResponseTimeData { get; init; }
    }

    public class ResponseTimeData
    {
        public int? ResponseTimeMs { get; init; }
        public int? MaxAllowedResponseTimeMs { get; init; }
    }
}