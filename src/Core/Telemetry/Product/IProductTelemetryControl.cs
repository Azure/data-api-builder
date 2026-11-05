// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>
    /// Host control for DAB-owned product collection. It never enables collection or changes
    /// customer diagnostic providers. Resolve from the engine's service provider when needed.
    /// </summary>
    public interface IProductTelemetryControl
    {
        bool IsEnabled { get; }

        /// <summary>
        /// Permanently disables collection for this engine instance, cancels delivery and
        /// discards unsent data without flushing. Does not erase saved identity or received data.
        /// </summary>
        void Disable();
    }
}
