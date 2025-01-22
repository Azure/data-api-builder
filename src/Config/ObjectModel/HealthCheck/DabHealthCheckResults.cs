// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    /// <summary>
    /// The health report of the DAB Engine.
    /// </summary>
    public record DabHealthCheckResults
    {
        public List<HealthCheckDetailsResultEntry>? DataSourceHealthCheckResults { get; init; }
        public List<HealthCheckEntityResultEntry>? EntityHealthCheckResults { get; init; }
    }

    public class HealthCheckResultEntry
    {
        public string? Name { get; set; }
        public string? Description { get; init; }
    }

    public class HealthCheckDetailsResultEntry : HealthCheckResultEntry
    {
        public HealthStatus HealthStatus { get; init; }
        public string? Exception { get; init; }
        public ResponseTimeData? ResponseTimeData { get; init; }
    }

    public class HealthCheckEntityResultEntry : HealthCheckResultEntry
    {
        public required Dictionary<string, HealthCheckDetailsResultEntry> EntityHealthCheckResults { get; init; }
    }

    public class ResponseTimeData
    {
        public int? ResponseTimeMs { get; init; }
        public int? MaxAllowedResponseTimeMs { get; init; }
    }
}
