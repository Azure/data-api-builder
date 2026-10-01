// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Events;

namespace Azure.DataApiBuilder.Core.Telemetry.Product
{
    /// <summary>
    /// Observes actual lookups independently at each FusionCache layer. The owner attaches one
    /// observer to its data cache and disposes it before releasing the engine session.
    /// </summary>
    /// <remarks>
    /// FusionCache may dispatch events asynchronously. Recording uses only the eligible request
    /// context flowing into the callback; no context is captured at subscription time and no
    /// request is reconstructed from cache keys. The session skips callbacks without an active
    /// eligible request. Hit/Miss arguments do not identify background work that inherits such a
    /// context, so its suppression belongs to the session/owner. Coverage remains unknown when
    /// context is missing or expired; asynchronous delivery is not proof of complete cache
    /// coverage. Reporting lost attribution requires a separate session completeness contract.
    /// </remarks>
    internal sealed class EngineTelemetryCacheObserver : IDisposable
    {
        private readonly EngineTelemetrySession _session;
        private readonly FusionCacheEventsHub? _events;
        private int _disposed;

        public EngineTelemetryCacheObserver(IFusionCache cache, EngineTelemetrySession session)
        {
            ArgumentNullException.ThrowIfNull(cache);
            ArgumentNullException.ThrowIfNull(session);
            _session = session;

            if (!session.IsEnabled)
            {
                return;
            }

            _events = cache.Events;
            _events.Memory.Hit += OnMemoryHit;
            _events.Memory.Miss += OnMemoryMiss;
            _events.Distributed.Hit += OnDistributedHit;
            _events.Distributed.Miss += OnDistributedMiss;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0 || _events is null)
            {
                return;
            }

            _events.Memory.Hit -= OnMemoryHit;
            _events.Memory.Miss -= OnMemoryMiss;
            _events.Distributed.Hit -= OnDistributedHit;
            _events.Distributed.Miss -= OnDistributedMiss;
        }

        // Deliberately ignore event arguments: keys can contain SQL, parameters and other
        // customer data. Do not subscribe to the aggregate Hit/Miss events as well as these.
        private void OnMemoryHit(object? sender, FusionCacheEntryHitEventArgs args)
            => RecordLookup(EngineTelemetryCacheLayer.Level1, EngineTelemetryCacheResult.Hit);

        private void OnMemoryMiss(object? sender, FusionCacheEntryEventArgs args)
            => RecordLookup(EngineTelemetryCacheLayer.Level1, EngineTelemetryCacheResult.Miss);

        private void OnDistributedHit(object? sender, FusionCacheEntryHitEventArgs args)
            => RecordLookup(EngineTelemetryCacheLayer.Level2, EngineTelemetryCacheResult.Hit);

        private void OnDistributedMiss(object? sender, FusionCacheEntryEventArgs args)
            => RecordLookup(EngineTelemetryCacheLayer.Level2, EngineTelemetryCacheResult.Miss);

        private void RecordLookup(EngineTelemetryCacheLayer layer, EngineTelemetryCacheResult result)
        {
            if (Volatile.Read(ref _disposed) != 0 || !_session.IsEnabled)
            {
                return;
            }

            try
            {
                _session.RecordCacheLookup(layer, result);
            }
            catch (Exception)
            {
                // Never let observer failures affect caching or enter FusionCache diagnostics,
                // which may include the cache key. No request context is fabricated on failure.
            }
        }
    }
}
