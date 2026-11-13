// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>
    /// Immutable delivery envelope. Retries retain this instance, including its event identity
    /// and occurrence time; they do not represent additional observations.
    /// </summary>
    internal sealed record EngineTelemetryEvent(
        Guid EventId,
        Guid SessionId,
        long Sequence,
        DateTimeOffset OccurredAt,
        long ConfigurationEpoch,
        string Name,
        ImmutableDictionary<string, string> Properties)
    {
        // Production collection/export enablement is not part of this implementation.
        public bool IsSynthetic { get; } = true;
    }
}
