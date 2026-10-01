// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// Identifies the actual mapped embedding data endpoint, which has no MVC controller
    /// descriptor and need not be under the configured REST entity path.
    /// </summary>
    internal sealed class EngineTelemetryEmbeddingEndpointMetadata
    {
        internal static EngineTelemetryEmbeddingEndpointMetadata Instance { get; } = new();

        private EngineTelemetryEmbeddingEndpointMetadata() { }
    }
}
