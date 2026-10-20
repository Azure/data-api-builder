// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>
    /// Immutable CLI delivery envelope, independent of engine configuration epochs.
    /// </summary>
    internal sealed record CliTelemetryEvent(
        Guid EventId,
        Guid SessionId,
        long Sequence,
        DateTimeOffset OccurredAt,
        string Name,
        ImmutableDictionary<string, string> Properties) : IProductTelemetryEvent
    {
        // Production collection/export enablement is not part of this implementation.
        public bool IsSynthetic => true;
    }
}
