// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using OpenTelemetry;

namespace Azure.DataApiBuilder.Service.Telemetry
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
