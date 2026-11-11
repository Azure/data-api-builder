// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using OpenTelemetry;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>One worker-owned SDK call; neither an event queue nor a process-wide context.</summary>
    internal sealed class EngineTelemetryExportAttempt(IProductTelemetryEvent record, CancellationToken token)
    {
        private int _requests;
        internal IProductTelemetryEvent Record { get; } = record;
        internal CancellationToken Token { get; } = token;
        internal ExportResult Result { get; set; } = ExportResult.Failure;
        internal bool TryBeginRequest() => Interlocked.Exchange(ref _requests, 1) == 0;
    }
}
