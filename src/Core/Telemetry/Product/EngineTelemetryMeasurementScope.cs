// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>
    /// The owning operation sets its outcome; disposal records it once and restores nesting.
    /// Failure is the default so an exception cannot accidentally become a successful operation.
    /// </summary>
    internal sealed class EngineTelemetryMeasurementScope : IDisposable
    {
        private readonly Action<EngineTelemetryOutcome> _record;
        private readonly Action? _restore;
        private int _disposed;
        private EngineTelemetryOutcome _outcome = EngineTelemetryOutcome.Failure;

        internal EngineTelemetryMeasurementScope(Action<EngineTelemetryOutcome> record, Action? restore = null)
        {
            _record = record;
            _restore = restore;
        }

        public void Complete(EngineTelemetryOutcome outcome)
            => _outcome = EngineTelemetryDimensions.Normalize(outcome);

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                _record(_outcome);
            }
            catch (Exception)
            {
                // Product measurements cannot fail customer work.
            }
            finally
            {
                _restore?.Invoke();
            }
        }
    }
}
