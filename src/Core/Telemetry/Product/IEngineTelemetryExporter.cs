// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>
    /// Owned by one delivery worker. Implementations must honor cancellation before transmission
    /// and during I/O. Export and disposal never run on a request's enqueueing thread. Successful
    /// acknowledgement does not guarantee later backend storage or exactly-once delivery.
    /// </summary>
    internal interface IEngineTelemetryExporter : IProductTelemetryExporter<EngineTelemetryEvent>
    {
    }
}
