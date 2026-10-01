// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>
    /// Captures one request's accepted configuration and timing. Complete records exactly once;
    /// Dispose only restores ambient state because a response can complete after dispatch returns.
    /// </summary>
    internal sealed class EngineTelemetryRequestScope : IDisposable
    {
        private readonly EngineTelemetrySession _session;
        private readonly EngineTelemetryRequestScope? _previous;
        private const int COMPLETED_FLAG = 1 << 16;
        private int _disposed;
        private int _outcome;
        private int _eligible;

        internal EngineTelemetryRequestScope(
            EngineTelemetrySession session,
            EngineTelemetryRequestScope? previous,
            EngineTelemetryConfiguration configuration,
            RuntimeConfig? config,
            EngineTelemetryApi api,
            EngineTelemetryTransport transport,
            EngineTelemetryRole role,
            bool eligible,
            long started)
        {
            _session = session;
            _previous = previous;
            Configuration = configuration;
            Config = config;
            Api = api;
            Transport = transport;
            Role = role;
            _eligible = eligible ? 1 : 0;
            Started = started;
        }

        internal EngineTelemetryConfiguration Configuration { get; }
        internal RuntimeConfig? Config { get; }
        internal EngineTelemetryApi Api { get; }
        internal EngineTelemetryTransport Transport { get; }
        internal EngineTelemetryRole Role { get; private set; }
        internal long Started { get; }
        internal bool IsEligible => Volatile.Read(ref _eligible) != 0;
        internal bool IsCompleted => (Volatile.Read(ref _outcome) & COMPLETED_FLAG) != 0;
        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
        public EngineTelemetryOutcome Outcome => (EngineTelemetryOutcome)(Volatile.Read(ref _outcome) & ~COMPLETED_FLAG);

        public void MarkEligible() => Volatile.Write(ref _eligible, 1);

        public void SetOutcome(EngineTelemetryOutcome outcome)
        {
            int normalized = (int)EngineTelemetryDimensions.Normalize(outcome);
            int observed = Volatile.Read(ref _outcome);
            while ((observed & COMPLETED_FLAG) == 0)
            {
                int previous = Interlocked.CompareExchange(ref _outcome, normalized, observed);
                if (previous == observed)
                {
                    return;
                }

                observed = previous;
            }
        }

        public void SetRole(EngineTelemetryRole role) => Role = EngineTelemetryDimensions.Normalize(role);

        public EngineTelemetryRequestScope ForkForCompletion()
            => new(_session, previous: null, Configuration, Config, Api, Transport, Role, IsEligible, Started);

        public void Complete(EngineTelemetryOutcome outcome, int? httpStatusCode = null)
        {
            EngineTelemetryOutcome normalized = EngineTelemetryDimensions.Normalize(outcome);
            int terminal = (int)normalized | COMPLETED_FLAG;
            int observed = Volatile.Read(ref _outcome);
            while ((observed & COMPLETED_FLAG) == 0)
            {
                int previous = Interlocked.CompareExchange(ref _outcome, terminal, observed);
                if (previous == observed)
                {
                    // Completion and its terminal outcome are one atomic state transition.
                    // A tool result racing an HTTP abort cannot overwrite cancellation.
                    _session.CompleteRequest(this, normalized, httpStatusCode);
                    return;
                }

                observed = previous;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _session.RestoreRequest(this, _previous);
            }
        }
    }
}
