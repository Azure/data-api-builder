// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Service.HealthCheck
{
    /// <summary>
    /// The runtime configuration details of the DAB Engine.
    /// As taken from the runtime config file. 
    /// </summary>
    public record ConfigurationDetails
    {
        [JsonPropertyName("rest")]
        public bool Rest { get; init; }

        [JsonPropertyName("graphql")]
        public bool GraphQL { get; init; }

        [JsonPropertyName("caching")]
        public bool Caching { get; init; }

        [JsonPropertyName("telemetry")]
        public bool Telemetry { get; init; }

        [JsonPropertyName("mode")]
        public HostMode Mode { get; init; }
    }
}
