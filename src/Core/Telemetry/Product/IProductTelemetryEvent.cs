// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Immutable;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>
    /// Immutable delivery envelope shared by product telemetry producers. Retries retain the
    /// same event identity and occurrence time; producer-specific metadata stays on its record.
    /// </summary>
    internal interface IProductTelemetryEvent
    {
        Guid EventId { get; }
        Guid SessionId { get; }
        long Sequence { get; }
        DateTimeOffset OccurredAt { get; }
        string Name { get; }
        ImmutableDictionary<string, string> Properties { get; }
        bool IsSynthetic { get; }
    }
}
