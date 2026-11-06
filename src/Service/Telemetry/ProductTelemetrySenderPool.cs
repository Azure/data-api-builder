// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Telemetry.Product;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    /// <summary>
    /// Bounded, process-owned senders. Each destination has one adapter, private logger and SDK
    /// transport, with cancellable serialization across all CLI and engine delivery workers.
    /// </summary>
    /// <remarks>
    /// Azure Monitor 1.9.0's TransmitterFactory caches by connection string. Its published source
    /// replaces disposed transmitters, but never evicts destination keys, and acquisition and final
    /// transmitter disposal do not share a lock. Retain at most eight destinations in the default
    /// pool, including idle senders, rather than reconstructing SDK exporters between invocations.
    /// Releasing a lease releases only that owner; it never disposes another owner's transport.
    /// The isolated test pool can be closed; SDK cleanup then waits for owners AND admitted calls.
    /// This does not provide isolation from foreign SDK exporters using the same connection string.
    /// </remarks>
    internal sealed class ProductTelemetrySenderPool : IDisposable
    {
        private const int DEFAULT_CAPACITY = 8;
        private static readonly ProductTelemetrySenderPool _default = new(
            static destination => new EngineTelemetryApplicationInsightsExporter(destination));
        private readonly object _sync = new();
        private readonly Dictionary<string, SenderEntry> _senders = new(StringComparer.Ordinal);
        private readonly Func<ApplicationInsightsTelemetryDestination, IProductTelemetryExporter<IProductTelemetryEvent>> _factory;
        private readonly int _capacity;
        private bool _disposed;

        /// <summary>
        /// Creates an isolated pool. Tests supply an offline exporter or an actual SDK adapter
        /// with a fake HTTP handler and a unique synthetic destination, never the default pool.
        /// </summary>
        internal ProductTelemetrySenderPool(
            Func<ApplicationInsightsTelemetryDestination, IProductTelemetryExporter<IProductTelemetryEvent>> factory,
            int capacity = DEFAULT_CAPACITY)
        {
            ArgumentNullException.ThrowIfNull(factory);
            ArgumentOutOfRangeException.ThrowIfLessThan(capacity, 1);
            _factory = factory;
            _capacity = capacity;
        }

        internal static IProductTelemetryExporter<IProductTelemetryEvent> Acquire(ApplicationInsightsTelemetryDestination destination)
            => _default.AcquireLease(destination);

        /// <summary>
        /// Reserves an owner without SDK initialization or I/O. Destination capacity is a lifetime
        /// bound, not an LRU: evicting idle entries would allow the SDK's own cache to grow unbounded.
        /// </summary>
        internal IProductTelemetryExporter<IProductTelemetryEvent> AcquireLease(ApplicationInsightsTelemetryDestination destination)
        {
            ArgumentNullException.ThrowIfNull(destination);
            lock (_sync)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                if (!_senders.TryGetValue(destination.ConnectionString, out SenderEntry? sender))
                {
                    if (_senders.Count >= _capacity)
                    {
                        // No routing values in exceptions or customer logging. Delivery handles
                        // optional factory failures within its existing finite retry budget.
                        throw new InvalidOperationException("Product telemetry sender capacity has been reached.");
                    }

                    sender = new(destination);
                    _senders.Add(destination.ConnectionString, sender);
                }

                sender.Owners++;
                return new Lease(this, sender);
            }
        }

        private async ValueTask<bool> ExportAsync(Lease lease, IProductTelemetryEvent record, CancellationToken cancellationToken)
        {
            ArgumentNullException.ThrowIfNull(record);
            SenderEntry sender;
            lock (_sync)
            {
                if (_disposed || lease.Sender is null || cancellationToken.IsCancellationRequested || !record.IsSynthetic)
                {
                    return false;
                }

                sender = lease.Sender;
                // Includes semaphore waiters, factory initialization and the entire SDK call.
                // A concurrent lease/pool Dispose must not tear down any of those operations.
                sender.ActiveOperations++;
            }

            bool entered = false;
            try
            {
                await sender.Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                entered = true;
                lock (_sync)
                {
                    if (_disposed || lease.Sender is null)
                    {
                        return false;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                // Export runs on the existing delivery worker, not the enqueueing command thread.
                // No factory code, SDK call or disposal executes while holding the pool's lock.
                sender.Exporter ??= _factory(sender.Destination);
                cancellationToken.ThrowIfCancellationRequested();
                return sender.Exporter is not null &&
                    await sender.Exporter.ExportAsync(record, cancellationToken).ConfigureAwait(false) &&
                    !cancellationToken.IsCancellationRequested;
            }
            catch (Exception)
            {
                // Cancellation and optional factory/SDK failures affect only this attempt.
                return false;
            }
            finally
            {
                if (entered)
                {
                    sender.Gate.Release();
                }

                SenderEntry? cleanup;
                lock (_sync)
                {
                    sender.ActiveOperations--;
                    cleanup = TakeForCleanup(sender);
                }

                DisposeSender(cleanup);
            }
        }

        private void Release(Lease lease)
        {
            SenderEntry? cleanup;
            lock (_sync)
            {
                SenderEntry? sender = lease.Sender;
                if (sender is null)
                {
                    return;
                }

                lease.Sender = null;
                sender.Owners--;
                cleanup = TakeForCleanup(sender);
            }

            DisposeSender(cleanup);
        }

        /// <summary>
        /// Closes an isolated pool without waiting for network work. Existing admitted calls keep
        /// their own cancellation, and the last owner/operation performs cleanup exactly once.
        /// The default pool intentionally lives for the process; lease disposal does not close it.
        /// </summary>
        public void Dispose()
        {
            List<SenderEntry> cleanup = [];
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                foreach (SenderEntry sender in _senders.Values)
                {
                    if (TakeForCleanup(sender) is { } retired)
                    {
                        cleanup.Add(retired);
                    }
                }

                _senders.Clear();
            }

            foreach (SenderEntry sender in cleanup)
            {
                DisposeSender(sender);
            }
        }

        // Caller holds _sync. Idle entries remain process-owned until the pool is closed.
        private SenderEntry? TakeForCleanup(SenderEntry sender)
        {
            if (!_disposed || sender.Owners != 0 || sender.ActiveOperations != 0 || sender.Retired)
            {
                return null;
            }

            sender.Retired = true;
            return sender;
        }

        private static void DisposeSender(SenderEntry? sender)
        {
            if (sender is null)
            {
                return;
            }

            try
            {
                sender.Exporter?.Dispose();
            }
            catch (Exception)
            {
                // Best-effort cleanup must not interfere with another destination or shutdown.
            }
            finally
            {
                sender.Exporter = null;
                sender.Gate.Dispose();
            }
        }

        private sealed class SenderEntry(ApplicationInsightsTelemetryDestination destination)
        {
            internal ApplicationInsightsTelemetryDestination Destination { get; } = destination;
            internal SemaphoreSlim Gate { get; } = new(1, 1);
            internal IProductTelemetryExporter<IProductTelemetryEvent>? Exporter { get; set; }
            internal int Owners { get; set; }
            internal int ActiveOperations { get; set; }
            internal bool Retired { get; set; }
        }

        private sealed class Lease(ProductTelemetrySenderPool pool, SenderEntry sender) : IProductTelemetryExporter<IProductTelemetryEvent>
        {
            // Admission, disposal and owner accounting all use the same short pool lock.
            internal SenderEntry? Sender { get; set; } = sender;

            public ValueTask<bool> ExportAsync(IProductTelemetryEvent record, CancellationToken cancellationToken)
                => pool.ExportAsync(this, record, cancellationToken);

            public void Dispose() => pool.Release(this);
        }
    }
}
