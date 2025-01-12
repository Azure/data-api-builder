// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Config.ObjectModel
{
    /// <summary>
    /// The health report of the DAB Enigne.
    /// </summary>
    public record DabConfigurationDetails
    {
        public bool Rest { get; init; }
        public bool GraphQL { get; init; }
        public bool Caching { get; init; }
        public bool Telemetry { get; init; }
        public HostMode Mode { get; init; }
        public required string DabSchema { get; init; }
    }
}
