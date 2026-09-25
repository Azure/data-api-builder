// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZiggyCreatures.Caching.Fusion;

namespace Azure.DataApiBuilder.Service.Telemetry
{
    internal sealed class EngineTelemetryHosting : IHostedService, IDisposable
    {
        internal const string TEST_MODE_VARIABLE = ProductTelemetryPolicy.TEST_MODE_ENV_VAR;
        internal const string CONNECTION_STRING_VARIABLE = "DAB_PRODUCT_TELEMETRY_CONNECTION_STRING";
        private readonly EngineTelemetrySession _session;
        private readonly IServiceProvider _services;
        private readonly IHostApplicationLifetime _lifetime;
        private IDisposable? _started;
        private EngineTelemetryCacheObserver? _cache;
        private EngineTelemetryCacheObserver? _embeddingCache;

        public EngineTelemetryHosting(EngineTelemetrySession session, IServiceProvider services, IHostApplicationLifetime lifetime)
        {
            _session = session;
            _services = services;
            _lifetime = lifetime;
        }

        internal static EngineTelemetrySession CreateStandalone(bool stdio, ProductTelemetryLaunchContext? launchContext = null,
            string? configPath = null)
        {
            try
            {
                return CreateStandaloneCore(stdio, launchContext, configPath);
            }
            catch (Exception)
            {
                // Environment/SDK configuration must never prevent the engine from starting.
                return EngineTelemetrySession.Create();
            }
        }

        private static EngineTelemetrySession CreateStandaloneCore(bool stdio, ProductTelemetryLaunchContext? launchContext,
            string? configPath)
        {
            // Setting the destination alone never enables collection. These switches are only
            // for deliberately synthetic validation before the production privacy gate opens.
            if (!ProductTelemetryBootstrap.TryGetDestination(out ApplicationInsightsTelemetryDestination? destination))
            {
                return EngineTelemetrySession.Create();
            }

            return EngineTelemetrySession.Create(() => new EngineSenderLease(ProductTelemetrySenderPool.Acquire(destination)),
                enableSyntheticCollection: true, executionMode: stdio ? "mcp_stdio" : "web",
                configPath: configPath, launchContext: launchContext);
        }

        private sealed class EngineSenderLease(IProductTelemetryExporter<IProductTelemetryEvent> lease) : IEngineTelemetryExporter
        {
            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
                => lease.ExportAsync(record, cancellationToken);

            public void Dispose() => lease.Dispose();
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                if (_session.IsEnabled)
                {
                    _started ??= _lifetime.ApplicationStarted.Register(_session.MarkHostReady);
                    IFusionCache? cache = _services.GetService<IFusionCache>();
                    if (cache is not null)
                    {
                        _cache ??= new(cache, _session);
                    }

                    // EmbeddingService uses a distinct named cache only when caching is enabled.
                    // Its default-cache fallback is already observed above, never subscribed twice.
                    if (_services.GetService<EmbeddingsOptions>() is { Enabled: true, IsCachingEnabled: true })
                    {
                        IFusionCache? embeddingCache = _services.GetService<IFusionCacheProvider>()?.GetCache("EmbeddingsCache");
                        if (embeddingCache is not null && !ReferenceEquals(cache, embeddingCache))
                        {
                            _embeddingCache ??= new(embeddingCache, _session);
                        }
                    }
                }
            }
            catch (Exception)
            {
                // Optional cache observation must never prevent the engine from starting.
                _cache?.Dispose();
                _embeddingCache?.Dispose();
                _session.Disable();
            }

            return Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _cache?.Dispose();
            _embeddingCache?.Dispose();
            return _session.StopAsync(cancellationToken);
        }

        public void Dispose()
        {
            _started?.Dispose();
            _cache?.Dispose();
            _embeddingCache?.Dispose();
            // The bootstrap caller owns the externally registered session. Host disposal can
            // precede its startup-failure catch; do not discard that final diagnostic here.
        }
    }
}
