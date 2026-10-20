// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Channels;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>
    /// Best-effort, bounded, memory-only delivery. Capacity includes the current record throughout
    /// initialization, export, and retry delays. No file spool or customer diagnostic pipeline is used.
    /// Export admission and Disable share a short gate, never a lock held across exporter code.
    /// An attempt admitted before Disable is in flight and receives cancellation; cancellation
    /// cannot retract data already sent. Successful Export is not an end-to-end delivery guarantee.
    /// </summary>
    internal class ProductTelemetryDelivery<T> : IDisposable where T : class, IProductTelemetryEvent
    {
        private static readonly TimeSpan _maximumFlushTimeout = TimeSpan.FromSeconds(2);
        private readonly object _sync = new();
        private readonly Func<IProductTelemetryExporter<T>> _factory;
        private readonly Channel<T> _queue;
        private readonly CancellationTokenSource _cancellation;
        private readonly TimeProvider _timeProvider;
        private readonly int _capacity;
        private readonly int _maxAttempts;
        private readonly TimeSpan _flushTimeout;
        private readonly Task _worker;
        private Task _cancellationCompletion = Task.CompletedTask;
        private Task? _stopTask;
        private IProductTelemetryExporter<T>? _exporter;
        private T? _inFlight;
        private int _accepting = 1;
        private int _outstanding;
        // Keep the engine wrapper's existing diagnostic-counter reflection compatible.
        protected long _droppedEvents;
        private bool _disabled;

        public ProductTelemetryDelivery(
            Func<IProductTelemetryExporter<T>> factory,
            int capacity = 256,
            int maxAttempts = 3,
            TimeSpan? flushTimeout = null)
            : this(factory, capacity, maxAttempts, flushTimeout, TimeProvider.System)
        {
        }

        // Keep the integration constructor unchanged while allowing deterministic timer tests.
        internal ProductTelemetryDelivery(
            Func<IProductTelemetryExporter<T>> factory,
            int capacity,
            int maxAttempts,
            TimeSpan? flushTimeout,
            TimeProvider timeProvider)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentNullException.ThrowIfNull(timeProvider);
            ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
            ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
            TimeSpan timeout = flushTimeout ?? _maximumFlushTimeout;
            if (timeout < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(flushTimeout));
            }

            _factory = factory;
            _capacity = capacity;
            _maxAttempts = maxAttempts;
            _flushTimeout = timeout < _maximumFlushTimeout ? timeout : _maximumFlushTimeout;
            _timeProvider = timeProvider;
            _cancellation = new();
            _queue = Channel.CreateBounded<T>(new BoundedChannelOptions(capacity)
            {
                FullMode = BoundedChannelFullMode.Wait,
                AllowSynchronousContinuations = false,
                SingleReader = false, // Disable also drains queued records.
                SingleWriter = false
            });

            // Start exactly one worker, even before the first enqueue. Do not capture a request's
            // Activity, logger scopes, or other AsyncLocal state. The factory remains lazy.
            if (ExecutionContext.IsFlowSuppressed())
            {
                _worker = Task.Run(RunWorkerAsync);
            }
            else
            {
                using (ExecutionContext.SuppressFlow())
                {
                    _worker = Task.Run(RunWorkerAsync);
                }
            }
        }

        /// <summary>
        /// Saturating count of rejected or abandoned records, not failed attempts. An abandoned
        /// in-flight record may already have reached the receiver before cancellation.
        /// </summary>
        public long DroppedEvents => Interlocked.Read(ref _droppedEvents);

        /// <summary>
        /// Never waits for capacity, exporter initialization, network I/O, or retry work. No task
        /// is created per enqueue. Reservations bound queued plus in-flight records, not just the
        /// channel's buffer. Completion of the writer resolves races with StopAsync and Disable.
        /// </summary>
        public bool TryEnqueue(T record)
        {
            ArgumentNullException.ThrowIfNull(record);
            if (Volatile.Read(ref _accepting) == 0 || !TryReserveSlot())
            {
                AddDroppedEvents(1);
                return false;
            }

            if (_queue.Writer.TryWrite(record))
            {
                return true;
            }

            Interlocked.Decrement(ref _outstanding);
            AddDroppedEvents(1);
            return false;
        }

        /// <summary>
        /// Close admission and share one graceful drain deadline (at most two seconds). Caller
        /// cancellation also disables delivery; it does not surface an application exception.
        /// Expiry does not wait for exporter code or cancellation callbacks to finish.
        /// </summary>
        public Task StopAsync(CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                Disable();
                return Task.CompletedTask;
            }

            Task stopTask;
            lock (_sync)
            {
                Volatile.Write(ref _accepting, 0);
                _queue.Writer.TryComplete();
                stopTask = _stopTask ??= StopCoreAsync();
            }

            return cancellationToken.CanBeCanceled ? WaitForStopAsync(stopTask, cancellationToken) : stopTask;
        }

        /// <summary>
        /// Disable first, cancel the in-flight attempt, and discard owned records without a final
        /// flush. Cancellation callbacks run asynchronously, outside the gate, so a callback may
        /// reenter Dispose safely. Exporter disposal is exclusively the worker's responsibility.
        /// </summary>
        public void Disable()
        {
            lock (_sync)
            {
                if (_disabled)
                {
                    return;
                }

                _disabled = true;
                Volatile.Write(ref _accepting, 0);
                _queue.Writer.TryComplete();

                // CancelAsync marks the token synchronously but does not execute arbitrary
                // exporter callbacks on this thread. Observe faults even if the exporter stalls.
                if (ExecutionContext.IsFlowSuppressed())
                {
                    _cancellationCompletion = ObserveCancellationAsync(_cancellation.CancelAsync());
                }
                else
                {
                    using (ExecutionContext.SuppressFlow())
                    {
                        _cancellationCompletion = ObserveCancellationAsync(_cancellation.CancelAsync());
                    }
                }

                DiscardOwnedRecords();
            }
        }

        public void Dispose() => Disable();

        private bool TryReserveSlot()
        {
            int outstanding = Volatile.Read(ref _outstanding);
            while (outstanding < _capacity)
            {
                int previous = Interlocked.CompareExchange(ref _outstanding, outstanding + 1, outstanding);
                if (previous == outstanding)
                {
                    return true;
                }

                outstanding = previous;
            }

            return false;
        }

        private async Task RunWorkerAsync()
        {
            try
            {
                while (await _queue.Reader.WaitToReadAsync(_cancellation.Token).ConfigureAwait(false))
                {
                    T? record;
                    lock (_sync)
                    {
                        if (_disabled)
                        {
                            return;
                        }

                        if (!_queue.Reader.TryRead(out record))
                        {
                            continue;
                        }

                        _inFlight = record;
                    }

                    bool exported = await ExportWithRetriesAsync(record).ConfigureAwait(false);
                    lock (_sync)
                    {
                        // Disable may already have accounted for this in-flight record.
                        if (_inFlight is not null)
                        {
                            _inFlight = null;
                            if (!exported)
                            {
                                AddDroppedEvents(1);
                            }

                            Interlocked.Decrement(ref _outstanding);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (_cancellation.IsCancellationRequested)
            {
                // The reader or retry delay was canceled; the awaited task is observed.
            }
            catch (Exception)
            {
                // Telemetry failure must not fault an unobserved worker or use customer logs.
                Disable();
            }
            finally
            {
                Task cancellationCompletion;
                lock (_sync)
                {
                    // Prevent a later Disable from canceling a disposed token source.
                    _disabled = true;
                    Volatile.Write(ref _accepting, 0);
                    _queue.Writer.TryComplete();
                    DiscardOwnedRecords();
                    cancellationCompletion = _cancellationCompletion;
                }

                // No caller waits indefinitely for this cleanup. In particular, never join
                // cancellation callbacks from inside Disable or while holding the gate.
                await cancellationCompletion.ConfigureAwait(false);
                try
                {
                    _exporter?.Dispose();
                }
                catch (Exception)
                {
                    // Exporter cleanup is best effort and isolated from application shutdown.
                }
                finally
                {
                    _exporter = null;
                    _cancellation.Dispose();
                }
            }
        }

        private async Task<bool> ExportWithRetriesAsync(T record)
        {
            CancellationToken cancellationToken = _cancellation.Token;
            for (int attempt = 0; attempt < _maxAttempts; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // Initialization failures consume the same finite attempt budget. A null
                    // factory result is also a failed attempt, never an application failure.
                    _exporter ??= _factory();
                    if (_exporter is not null)
                    {
                        lock (_sync)
                        {
                            if (_disabled)
                            {
                                return false;
                            }

                            // This is the export-admission (logical start) boundary shared with
                            // Disable. No new attempt is admitted after it disables delivery.
                            // The adapter must honor cancellation if Disable wins between this
                            // admission and the invocation below; do not lock across network I/O.
                        }

                        cancellationToken.ThrowIfCancellationRequested();
                        if (await _exporter.ExportAsync(record, cancellationToken).ConfigureAwait(false))
                        {
                            return true;
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                catch (Exception)
                {
                    // Factory/export exceptions and false results have identical retry limits.
                }

                if (attempt < _maxAttempts - 1)
                {
                    // 100 ms, 200 ms, 400 ms, 800 ms, then 1 s; cap before shifting to avoid
                    // overflow even when a caller supplies a large (but finite) attempt limit.
                    TimeSpan delay = TimeSpan.FromMilliseconds(Math.Min(100 * (1 << Math.Min(attempt, 4)), 1000));
                    await Task.Delay(delay, _timeProvider, cancellationToken).ConfigureAwait(false);
                }
            }

            return false;
        }

        private async Task StopCoreAsync()
        {
            try
            {
                await _worker.WaitAsync(_flushTimeout, _timeProvider).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Includes deadline expiry. Do not add a second wait after requesting cancel.
                Disable();
            }
        }

        private async Task WaitForStopAsync(Task stopTask, CancellationToken cancellationToken)
        {
            try
            {
                await stopTask.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Disable();
            }
        }

        private static async Task ObserveCancellationAsync(Task cancellation)
        {
            try
            {
                await cancellation.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Cancellation callbacks are exporter code too; their faults are observed.
            }
        }

        // Caller holds _sync. Slots reserved by concurrent producers but not yet written are
        // released by those producers when TryWrite sees the completed writer.
        private void DiscardOwnedRecords()
        {
            int discarded = 0;
            while (_queue.Reader.TryRead(out _))
            {
                Interlocked.Decrement(ref _outstanding);
                discarded++;
            }

            if (_inFlight is not null)
            {
                _inFlight = null;
                Interlocked.Decrement(ref _outstanding);
                discarded++;
            }

            AddDroppedEvents(discarded);
        }

        private void AddDroppedEvents(int count)
        {
            long current = Interlocked.Read(ref _droppedEvents);
            while (count > 0 && current != long.MaxValue)
            {
                long next = current > long.MaxValue - count ? long.MaxValue : current + count;
                long previous = Interlocked.CompareExchange(ref _droppedEvents, next, current);
                if (previous == current)
                {
                    return;
                }

                current = previous;
            }
        }
    }
}
