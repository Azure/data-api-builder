// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>
    /// Engine-compatible entry point for best-effort, bounded, memory-only product delivery.
    /// </summary>
    internal sealed class EngineTelemetryDelivery : ProductTelemetryDelivery<EngineTelemetryEvent>
    {
        public EngineTelemetryDelivery(
            Func<IEngineTelemetryExporter> factory,
            int capacity = 256,
            int maxAttempts = 3,
            TimeSpan? flushTimeout = null)
            : base(factory, capacity, maxAttempts, flushTimeout)
        {
        }

        // Keep the integration constructor unchanged while allowing deterministic timer tests.
        internal EngineTelemetryDelivery(
            Func<IEngineTelemetryExporter> factory,
            int capacity,
            int maxAttempts,
            TimeSpan? flushTimeout,
            TimeProvider timeProvider)
            : base(factory, capacity, maxAttempts, flushTimeout, timeProvider)
        {
        }
    }
}
