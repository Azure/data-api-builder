// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>
    /// One scheduled helper's right to observe its actual, preflight-approved engine handoff.
    /// Not a launch context: it retains only the invocation owner and fixed source, creates no
    /// IDs/events/workers, and owns no path, sender, timer or cancellation token. Abandoning one
    /// ticket has no effect on another ticket, the CLI session or an already launched engine.
    /// </summary>
    internal sealed class CliTelemetryLaunchReservation : IDisposable
    {
        private CliTelemetrySession? _owner;
        private readonly CliTelemetryLaunchSource _source;

        internal CliTelemetryLaunchReservation(CliTelemetrySession owner, CliTelemetryLaunchSource source)
        {
            _owner = owner;
            _source = source;
        }

        // Lets the CLI skip even path resolution for consumed, abandoned or revoked tickets.
        internal bool IsAvailable => Volatile.Read(ref _owner)?.CanBeginReservedLaunch == true;

        internal ProductTelemetryLaunchContext? Begin(string? rootPath)
            => Interlocked.Exchange(ref _owner, null)?.BeginReservedEngineLaunch(rootPath, _source);

        public void Dispose() => Interlocked.Exchange(ref _owner, null);
    }
}
