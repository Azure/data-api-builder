// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services.Cache;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry;

/// <summary>
/// Exercises real FusionCache layer events, the observer, and session aggregation without a host,
/// database, or distributed-cache server. Event arguments are deliberately never inspected.
/// Synchronous dispatch is a test setting, not an assertion about the host's default configuration.
/// </summary>
[TestClass]
[TestCategory("EngineTelemetry")]
public class EngineTelemetryCacheTests
{
    private const string SQL_SENTINEL = "CACHE_PRIVATE_SQL_63fbc9";
    private const string ROW_SENTINEL = "CACHE_PRIVATE_ROW_63fbc9";
    private const string QUERY = "SELECT value FROM " + SQL_SENTINEL + " WHERE id = @id";
    private const string ENTITY = "CacheFixtureEntity";
    private static readonly TimeSpan _testTimeout = TimeSpan.FromSeconds(10);

    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task SynchronousMemoryMissAndHitMatchActualLayerEvents()
    {
        using Fixture fixture = new();
        using LayerObservations observations = new(fixture.Cache);
        using IDisposable observer = new EngineTelemetryCacheObserver(fixture.Cache, fixture.Session);
        using (EngineTelemetryRequestScope request = BeginRequest(fixture))
        {
            ExerciseMemoryMissAndHit(fixture.Cache);
            request.Complete(EngineTelemetryOutcome.Success, 200);
        }

        LayerCounts observed = observations.Snapshot;
        Assert.IsTrue(observed.MemoryMisses > 0);
        Assert.IsTrue(observed.MemoryHits > 0);
        Assert.AreEqual(0L, observed.DistributedMisses);
        Assert.AreEqual(0L, observed.DistributedHits);
        EngineTelemetryEvent[] records = await fixture.DrainAsync();
        AssertLayerCounts(records, observed);
        Assert.AreEqual(1L, Sum(records, "request"));
        Assert.AreEqual(0L, Sum(records, "database_attempt"));
    }

    [TestMethod]
    public async Task SynchronousColdLookupCountsBothMemoryAndDistributedMisses()
    {
        MemoryDistributedCache distributed = new(Options.Create(new MemoryDistributedCacheOptions()));
        using Fixture fixture = new(distributed);
        using LayerObservations observations = new(fixture.Cache);
        using IDisposable observer = new EngineTelemetryCacheObserver(fixture.Cache, fixture.Session);
        using (EngineTelemetryRequestScope request = BeginRequest(fixture))
        {
            Assert.IsFalse(fixture.Cache.TryGet<string>(QUERY).HasValue);
            request.Complete(EngineTelemetryOutcome.Success, 200);
        }

        LayerCounts observed = observations.Snapshot;
        Assert.IsTrue(observed.MemoryMisses > 0);
        Assert.IsTrue(observed.DistributedMisses > 0);
        EngineTelemetryEvent[] records = await fixture.DrainAsync();
        AssertLayerCounts(records, observed);
        Assert.AreEqual(1L, Sum(records, "request"));
        Assert.AreEqual(0L, Sum(records, "database_attempt"));
    }

    [TestMethod]
    public async Task SynchronousDistributedHitAfterMemoryMissIsCountedSeparatelyFromNextMemoryHit()
    {
        MemoryDistributedCache distributed = new(Options.Create(new MemoryDistributedCacheOptions()));
        // Separate FusionCache instances own separate L1 caches and share the same L2 and name.
        // Set waits for the distributed write; no eviction, expiration, or server is necessary.
        using FusionCache writer = CreateCache(CreateOptions(), distributed);
        writer.Set(QUERY, ROW_SENTINEL);
        using Fixture fixture = new(distributed);
        using LayerObservations observations = new(fixture.Cache);
        using IDisposable observer = new EngineTelemetryCacheObserver(fixture.Cache, fixture.Session);
        using (EngineTelemetryRequestScope request = BeginRequest(fixture))
        {
            AssertCachedRow(fixture.Cache.TryGet<string>(QUERY));
            LayerCounts first = observations.Snapshot;
            Assert.IsTrue(first.MemoryMisses > 0, "The reader must start with an empty L1.");
            Assert.IsTrue(first.DistributedHits > 0, "The value must actually be read from L2.");

            AssertCachedRow(fixture.Cache.TryGet<string>(QUERY));
            LayerCounts second = observations.Snapshot;
            Assert.IsTrue(second.MemoryHits > first.MemoryHits);
            // Internal lookups, if any, are included in the actual per-layer totals rather
            // than assuming a fixed number of events for either public TryGet operation.
            request.Complete(EngineTelemetryOutcome.Success, 200);
        }

        EngineTelemetryEvent[] records = await fixture.DrainAsync();
        AssertLayerCounts(records, observations.Snapshot);
        Assert.AreEqual(1L, Sum(records, "request"));
        Assert.AreEqual(0L, Sum(records, "database_attempt"));
    }

    [TestMethod]
    public async Task DisposingObserverStopsAllFourLayerCountersWhileRequestRemainsActive()
    {
        MemoryDistributedCache distributed = new(Options.Create(new MemoryDistributedCacheOptions()));
        using FusionCache writer = CreateCache(CreateOptions(), distributed);
        writer.Set(QUERY, ROW_SENTINEL);
        writer.Set(QUERY + " /* second */", ROW_SENTINEL);
        using Fixture fixture = new(distributed);
        using LayerObservations observations = new(fixture.Cache);
        using IDisposable observer = new EngineTelemetryCacheObserver(fixture.Cache, fixture.Session);
        LayerCounts beforeDispose;
        using (EngineTelemetryRequestScope request = BeginRequest(fixture))
        {
            AssertCachedRow(fixture.Cache.TryGet<string>(QUERY));
            AssertCachedRow(fixture.Cache.TryGet<string>(QUERY));
            Assert.IsFalse(fixture.Cache.TryGet<string>(QUERY + " /* absent */").HasValue);
            beforeDispose = observations.Snapshot;
            AssertAllLayersObserved(beforeDispose);

            observer.Dispose();
            observer.Dispose();
            Assert.IsTrue(fixture.Session.IsEnabled);
            Assert.IsFalse(request.IsCompleted);
            Assert.AreSame(request, fixture.Session.CurrentRequest);
            AssertCachedRow(fixture.Cache.TryGet<string>(QUERY + " /* second */"));
            AssertCachedRow(fixture.Cache.TryGet<string>(QUERY + " /* second */"));
            Assert.IsFalse(fixture.Cache.TryGet<string>(QUERY + " /* also absent */").HasValue);
            LayerCounts afterDispose = observations.Snapshot;
            Assert.IsTrue(afterDispose.MemoryHits > beforeDispose.MemoryHits);
            Assert.IsTrue(afterDispose.MemoryMisses > beforeDispose.MemoryMisses);
            Assert.IsTrue(afterDispose.DistributedHits > beforeDispose.DistributedHits);
            Assert.IsTrue(afterDispose.DistributedMisses > beforeDispose.DistributedMisses);
            request.Complete(EngineTelemetryOutcome.Success, 200);
        }

        EngineTelemetryEvent[] records = await fixture.DrainAsync();
        AssertLayerCounts(records, beforeDispose);
        Assert.AreEqual(1L, Sum(records, "request"));
        Assert.AreEqual(0L, Sum(records, "database_attempt"));
    }

    [TestMethod]
    public async Task DisabledSessionDoesNotEvenAccessEventHubToSubscribe()
    {
        using Fixture fixture = new(enabled: false);
        using LayerObservations observations = new(fixture.Cache);
        // This facade only verifies the no-subscription boundary. All lookups still use the
        // real cache below; checking empty telemetry alone would not prove no subscriptions.
        Mock<IFusionCache> guardedCache = new(MockBehavior.Strict);
        guardedCache.SetupGet(cache => cache.Events).Returns(fixture.Cache.Events);
        using IDisposable observer = new EngineTelemetryCacheObserver(guardedCache.Object, fixture.Session);
        using (EngineTelemetryRequestScope request = BeginRequest(fixture))
        {
            ExerciseMemoryMissAndHit(fixture.Cache);
            request.Complete(EngineTelemetryOutcome.Success, 200);
        }

        observer.Dispose();
        guardedCache.VerifyGet(cache => cache.Events, Times.Never());
        guardedCache.VerifyNoOtherCalls();
        Assert.IsTrue(observations.Snapshot.MemoryHits > 0);
        Assert.IsTrue(observations.Snapshot.MemoryMisses > 0);
        Assert.AreEqual(0, (await fixture.DrainAsync()).Length);
        Assert.AreEqual(0, fixture.ExporterCreations);
    }

    [DataTestMethod]
    [DataRow("absent", 0L)]
    [DataRow("ineligible", 0L)]
    [DataRow("completed", 1L)]
    public async Task LookupsWithoutAnActiveEligibleRequestAreNotInventedAsUnknownUsage(string context, long requests)
    {
        using Fixture fixture = new();
        using LayerObservations observations = new(fixture.Cache);
        using IDisposable observer = new EngineTelemetryCacheObserver(fixture.Cache, fixture.Session);
        using EngineTelemetryRequestScope? request = context == "absent" ? null : BeginRequest(fixture, eligible: context != "ineligible");
        if (context == "completed")
        {
            request!.Complete(EngineTelemetryOutcome.Success, 200);
        }

        ExerciseMemoryMissAndHit(fixture.Cache);
        request?.Complete(EngineTelemetryOutcome.Success, 200);
        Assert.IsTrue(observations.Snapshot.MemoryHits > 0);
        Assert.IsTrue(observations.Snapshot.MemoryMisses > 0);
        EngineTelemetryEvent[] records = await fixture.DrainAsync();
        AssertLayerCounts(records, default);
        Assert.AreEqual(requests, Sum(records, "request"));
        Assert.AreEqual(0L, Sum(records, "database_attempt"));
    }

    [TestMethod]
    public async Task BackgroundLookupsWithoutExecutionContextAreSkippedDespiteAnActiveForegroundRequest()
    {
        using Fixture fixture = new();
        using LayerObservations observations = new(fixture.Cache);
        using IDisposable observer = new EngineTelemetryCacheObserver(fixture.Cache, fixture.Session);
        using (EngineTelemetryRequestScope request = BeginRequest(fixture))
        {
            Task background;
            using (ExecutionContext.SuppressFlow())
            {
                background = Task.Run(() =>
                {
                    Assert.IsNull(fixture.Session.CurrentRequest);
                    ExerciseMemoryMissAndHit(fixture.Cache);
                });
            }

            await background.WaitAsync(_testTimeout);
            Assert.AreSame(request, fixture.Session.CurrentRequest);
            request.Complete(EngineTelemetryOutcome.Success, 200);
        }

        Assert.IsTrue(observations.Snapshot.MemoryHits > 0);
        Assert.IsTrue(observations.Snapshot.MemoryMisses > 0);
        EngineTelemetryEvent[] records = await fixture.DrainAsync();
        AssertLayerCounts(records, default);
        Assert.AreEqual(1L, Sum(records, "request"));
        Assert.AreEqual(0L, Sum(records, "database_attempt"));
    }

    [TestMethod]
    public async Task BackgroundLookupsWithCompletedInheritedContextCannotAttachToTheNextRequest()
    {
        // Hit/Miss arguments cannot distinguish background work with a still-active inherited
        // request. This tests the supported expired-context boundary, not universal suppression.
        using Fixture fixture = new();
        using LayerObservations observations = new(fixture.Cache);
        using IDisposable observer = new EngineTelemetryCacheObserver(fixture.Cache, fixture.Session);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Task background;
        using (EngineTelemetryRequestScope expired = BeginRequest(fixture))
        {
            background = Task.Run(async () =>
            {
                await release.Task.WaitAsync(_testTimeout);
                Assert.AreSame(expired, fixture.Session.CurrentRequest);
                Assert.IsTrue(expired.IsCompleted);
                ExerciseMemoryMissAndHit(fixture.Cache);
            });
            expired.Complete(EngineTelemetryOutcome.Success, 200);
        }

        try
        {
            using EngineTelemetryRequestScope next = BeginRequest(fixture);
            release.TrySetResult();
            await background.WaitAsync(_testTimeout);
            Assert.AreSame(next, fixture.Session.CurrentRequest);
            next.Complete(EngineTelemetryOutcome.Success, 200);
        }
        finally
        {
            release.TrySetResult();
        }

        Assert.IsTrue(observations.Snapshot.MemoryHits > 0);
        Assert.IsTrue(observations.Snapshot.MemoryMisses > 0);
        EngineTelemetryEvent[] records = await fixture.DrainAsync();
        AssertLayerCounts(records, default);
        Assert.AreEqual(2L, Sum(records, "request"));
        Assert.AreEqual(0L, Sum(records, "database_attempt"));
    }

    [TestMethod]
    public async Task RealCacheServiceFactoryHitDoesNotInventAnotherDatabaseAttempt()
    {
        using Fixture fixture = new();
        using LayerObservations observations = new(fixture.Cache);
        using IDisposable observer = new EngineTelemetryCacheObserver(fixture.Cache, fixture.Session);
        DabCacheService service = new(fixture.Cache, NullLogger<DabCacheService>.Instance, new HttpContextAccessor());
        DatabaseQueryMetadata metadata = new(QUERY, "synthetic", new() { ["@id"] = new(ROW_SENTINEL) });
        int factoryCalls = 0;

        Task<string> QueryAsync()
        {
            Interlocked.Increment(ref factoryCalls);
            // This synthetic command boundary is entered only by the real miss factory.
            // The cache observer must not manufacture database attempts from cache lookups.
            using EngineTelemetryMeasurementScope? attempt = fixture.Session.BeginDatabaseAttempt(DatabaseType.MSSQL);
            Assert.IsNotNull(attempt);
            attempt.Complete(EngineTelemetryOutcome.Success);
            return Task.FromResult(ROW_SENTINEL);
        }

        for (int index = 0; index < 2; index++)
        {
            using EngineTelemetryRequestScope request = BeginRequest(fixture);
            using (EngineTelemetryMeasurementScope? operation = fixture.Session.BeginOperation(ENTITY, EngineTelemetryOperation.Read))
            {
                Assert.IsNotNull(operation);
                string? result = await service.GetOrSetAsync<string>(QueryAsync, metadata, cacheEntryTtl: 3600, cacheEntryLevel: EntityCacheLevel.L1);
                Assert.IsTrue(result == ROW_SENTINEL, "The synthetic row must survive both the factory and cache paths.");
                operation.Complete(EngineTelemetryOutcome.Success);
            }

            Assert.AreEqual(1, factoryCalls, "The second request must use the cached response.");
            request.Complete(EngineTelemetryOutcome.Success, 200);
        }

        Assert.IsTrue(observations.Snapshot.MemoryMisses > 0);
        Assert.IsTrue(observations.Snapshot.MemoryHits > 0);
        EngineTelemetryEvent[] records = await fixture.DrainAsync();
        AssertLayerCounts(records, observations.Snapshot);
        Assert.AreEqual(2L, Sum(records, "request"));
        Assert.AreEqual(2L, Sum(records, "operation"));
        Assert.AreEqual(1L, Sum(records, "database_attempt"));
        Assert.AreEqual(1L, Sum(records, "database_attempt", "success"));
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task HostingObservesEachSelectedCacheOnceAndDisposesBoth(bool embeddingCachingEnabled, bool aliasDefaultCache)
    {
        MemoryDistributedCache regularDistributed = new(Options.Create(new MemoryDistributedCacheOptions()));
        MemoryDistributedCache embeddingDistributed = new(Options.Create(new MemoryDistributedCacheOptions()));
        using FusionCache regularWriter = CreateCache(CreateOptions(), regularDistributed);
        using FusionCache embeddingWriter = CreateCache(CreateOptions(), embeddingDistributed);
        regularWriter.Set(QUERY + " distributed", ROW_SENTINEL);
        embeddingWriter.Set(QUERY + " distributed", ROW_SENTINEL);
        using Fixture fixture = new(regularDistributed);
        using FusionCache embeddingCache = CreateCache(CreateOptions(), embeddingDistributed);
        using LayerObservations defaultEvents = new(fixture.Cache);
        using LayerObservations embeddingEvents = new(embeddingCache);
        Mock<IFusionCacheProvider> caches = new(MockBehavior.Strict);
        caches.Setup(provider => provider.GetCache("EmbeddingsCache")).Returns(aliasDefaultCache ? fixture.Cache : embeddingCache);
        EmbeddingsOptions embeddings = new(EmbeddingProviderType.OpenAI, "https://synthetic.invalid", "synthetic",
            Enabled: true, Cache: new(Enabled: embeddingCachingEnabled));
        using ServiceProvider services = new ServiceCollection()
            .AddSingleton<IFusionCache>(fixture.Cache)
            .AddSingleton(caches.Object)
            .AddSingleton(embeddings)
            .BuildServiceProvider();
        using EngineTelemetryHosting hosting = new(fixture.Session, services, Mock.Of<IHostApplicationLifetime>());
        await hosting.StartAsync(CancellationToken.None);
        await hosting.StartAsync(CancellationToken.None);

        LayerCounts expected;
        using (EngineTelemetryRequestScope request = BeginRequest(fixture))
        {
            AssertCachedRow(fixture.Cache.TryGet<string>(QUERY + " distributed"));
            ExerciseMemoryMissAndHit(fixture.Cache);
            if (embeddingCachingEnabled && !aliasDefaultCache)
            {
                AssertCachedRow(embeddingCache.TryGet<string>(QUERY + " distributed"));
                ExerciseMemoryMissAndHit(embeddingCache);
            }

            LayerCounts regular = defaultEvents.Snapshot;
            LayerCounts embedding = embeddingEvents.Snapshot;
            expected = new(regular.MemoryHits + embedding.MemoryHits, regular.MemoryMisses + embedding.MemoryMisses,
                regular.DistributedHits + embedding.DistributedHits, regular.DistributedMisses + embedding.DistributedMisses);
            AssertAllLayersObserved(expected);
            hosting.Dispose();
            hosting.Dispose();
            Assert.IsTrue(fixture.Session.IsEnabled, "Observer disposal must not discard a bootstrap-owned session.");
            Assert.IsFalse(fixture.Cache.TryGet<string>(QUERY + " after disposal").HasValue);
            Assert.IsFalse(embeddingCache.TryGet<string>(QUERY + " after disposal").HasValue);
            request.Complete(EngineTelemetryOutcome.Success, 200);
        }

        AssertLayerCounts(await fixture.DrainAsync(), expected);
        caches.Verify(provider => provider.GetCache("EmbeddingsCache"), embeddingCachingEnabled ? Times.AtLeastOnce() : Times.Never());
    }

    [TestMethod]
    public async Task DisabledHostingDoesNotResolveCachesOrEmbeddingOptions()
    {
        using Fixture fixture = new(enabled: false);
        Mock<IServiceProvider> services = new(MockBehavior.Strict);
        using EngineTelemetryHosting hosting = new(fixture.Session, services.Object, Mock.Of<IHostApplicationLifetime>());
        await hosting.StartAsync(CancellationToken.None);
        services.VerifyNoOtherCalls();
        Assert.IsFalse(fixture.Session.IsEnabled);
    }

    [TestMethod]
    public async Task CacheObservationSetupFailureDoesNotFailHostStartup()
    {
        using Fixture fixture = new();
        Mock<IServiceProvider> services = new(MockBehavior.Strict);
        services.Setup(provider => provider.GetService(typeof(IFusionCache))).Throws(new InvalidOperationException(SQL_SENTINEL));
        using EngineTelemetryHosting hosting = new(fixture.Session, services.Object, Mock.Of<IHostApplicationLifetime>());
        await hosting.StartAsync(CancellationToken.None);
        Assert.IsFalse(fixture.Session.IsEnabled);
        services.Verify(provider => provider.GetService(typeof(IFusionCache)), Times.Once);
    }

    [TestMethod]
    public async Task DefaultAsyncDispatchCanOutliveRequestAndDoesNotPromiseCompleteCacheCounts()
    {
        // Null leaves the library's regular async dispatch default, without validation opt-in.
        using Fixture fixture = new(synchronousEvents: null);
        Assert.IsFalse(fixture.Options.EnableSyncEventHandlersExecution);
        using EngineTelemetryRequestScope request = BeginRequest(fixture);
        TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource<bool> finished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int observedMisses = 0;
        EventHandler<FusionCacheEntryEventArgs> delayedHandler = (_, _) =>
        {
            Interlocked.Increment(ref observedMisses);
            entered.TrySetResult();
            try
            {
                release.Task.WaitAsync(_testTimeout).GetAwaiter().GetResult();
                finished.TrySetResult(request.IsCompleted);
            }
            catch (Exception exception)
            {
                finished.TrySetException(exception);
            }
        };

        fixture.Cache.Events.Memory.Miss += delayedHandler;
        using IDisposable observer = new EngineTelemetryCacheObserver(fixture.Cache, fixture.Session);
        try
        {
            Assert.IsFalse(fixture.Cache.TryGet<string>(QUERY).HasValue);
            request.Complete(EngineTelemetryOutcome.Success, 200);
            await entered.Task.WaitAsync(_testTimeout);
            Assert.IsFalse(finished.Task.IsCompleted, "TryGet and request completion did not wait for the event handler.");
        }
        finally
        {
            release.TrySetResult();
            fixture.Cache.Events.Memory.Miss -= delayedHandler;
        }

        Assert.IsTrue(await finished.Task.WaitAsync(_testTimeout), "The real layer callback must finish after request completion.");
        EngineTelemetryEvent[] records = await fixture.DrainAsync();
        Assert.IsTrue(Volatile.Read(ref observedMisses) > 0);
        Assert.AreEqual(1L, Sum(records, "request"));
        Assert.AreEqual(0L, Sum(records, "database_attempt"));
        foreach (EngineTelemetryEvent record in Summaries(records, "cache_lookup"))
        {
            Assert.AreEqual("level1", record.Properties["cache_layer"]);
            Assert.AreEqual("miss", record.Properties["cache_result"]);
            Assert.AreEqual("request_context_observed", record.Properties["cache_coverage"]);
            Assert.AreEqual(1L, record.ConfigurationEpoch);
        }

        // The probe gates its own callback only: no ordering between it and the observer is
        // assumed. Observer callbacks may run before or after Complete. Neither equality with
        // the raw event count nor zero loss is a valid assertion under default async dispatch.
        TestContext.WriteLine("Default async dispatch: observed L1 misses = {0}; session L1 misses retained = {1}.",
            Volatile.Read(ref observedMisses), SumCache(records, "level1", "miss"));
    }

    private static EngineTelemetryRequestScope BeginRequest(Fixture fixture, bool eligible = true) =>
        fixture.Session.BeginRequest(EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous, eligible);

    private static void ExerciseMemoryMissAndHit(FusionCache cache)
    {
        Assert.IsFalse(cache.TryGet<string>(QUERY).HasValue);
        cache.Set(QUERY, ROW_SENTINEL);
        AssertCachedRow(cache.TryGet<string>(QUERY));
    }

    private static void AssertCachedRow(MaybeValue<string> result) =>
        Assert.IsTrue(result.HasValue && result.Value == ROW_SENTINEL, "The synthetic cached row must be present and unchanged.");

    private static void AssertAllLayersObserved(LayerCounts counts)
    {
        Assert.IsTrue(counts.MemoryHits > 0);
        Assert.IsTrue(counts.MemoryMisses > 0);
        Assert.IsTrue(counts.DistributedHits > 0);
        Assert.IsTrue(counts.DistributedMisses > 0);
    }

    private static void AssertLayerCounts(EngineTelemetryEvent[] records, LayerCounts expected)
    {
        // Compare the real layer events, not one assumed lookup per public operation. A miss
        // factory can perform another internal lookup, and aggregate Hit/Miss events are not L1.
        Assert.AreEqual(expected.MemoryHits, SumCache(records, "level1", "hit"));
        Assert.AreEqual(expected.MemoryMisses, SumCache(records, "level1", "miss"));
        Assert.AreEqual(expected.DistributedHits, SumCache(records, "level2", "hit"));
        Assert.AreEqual(expected.DistributedMisses, SumCache(records, "level2", "miss"));
        Assert.AreEqual(expected.MemoryHits + expected.MemoryMisses + expected.DistributedHits + expected.DistributedMisses,
            Sum(records, "cache_lookup"), "No aggregate or unknown-layer observations may be added.");
        foreach (EngineTelemetryEvent record in Summaries(records, "cache_lookup"))
        {
            Assert.AreEqual(1L, record.ConfigurationEpoch);
            Assert.AreEqual("request_context_observed", record.Properties["cache_coverage"]);
        }
    }

    private static IEnumerable<EngineTelemetryEvent> Summaries(IEnumerable<EngineTelemetryEvent> records, string family) => records
        .Where(record => record.Name == "dab.engine.usage_summary"
            && record.Properties.TryGetValue("family", out string? value) && value == family);

    private static long Sum(IEnumerable<EngineTelemetryEvent> records, string family, string counter = "count") =>
        Summaries(records, family).Sum(record => long.Parse(record.Properties[counter], CultureInfo.InvariantCulture));

    private static long SumCache(IEnumerable<EngineTelemetryEvent> records, string layer, string result) => Summaries(records, "cache_lookup")
        .Where(record => record.Properties["cache_layer"] == layer && record.Properties["cache_result"] == result)
        .Sum(record => long.Parse(record.Properties["count"], CultureInfo.InvariantCulture));

    private static FusionCacheOptions CreateOptions(bool? synchronousEvents = true)
    {
        FusionCacheOptions options = new()
        {
            CacheName = nameof(EngineTelemetryCacheTests),
            DefaultEntryOptions = new FusionCacheEntryOptions
            {
                Duration = TimeSpan.FromHours(1),
                AllowBackgroundDistributedCacheOperations = false,
                ReThrowDistributedCacheExceptions = true,
                ReThrowSerializationExceptions = true
            }
        };
        if (synchronousEvents.HasValue)
        {
            options.EnableSyncEventHandlersExecution = synchronousEvents.Value;
        }

        return options;
    }

    private static FusionCache CreateCache(FusionCacheOptions options, IDistributedCache? distributed = null)
    {
        FusionCache cache = new(Options.Create(options));
        if (distributed is not null)
        {
            cache.SetupDistributedCache(distributed, new FusionCacheSystemTextJsonSerializer());
        }

        return cache;
    }

    private readonly record struct LayerCounts(long MemoryHits, long MemoryMisses, long DistributedHits, long DistributedMisses);

    private sealed class LayerObservations : IDisposable
    {
        private readonly FusionCacheEventsHub _events;
        private long _memoryHits;
        private long _memoryMisses;
        private long _distributedHits;
        private long _distributedMisses;

        internal LayerObservations(IFusionCache cache)
        {
            _events = cache.Events;
            _events.Memory.Hit += OnMemoryHit;
            _events.Memory.Miss += OnMemoryMiss;
            _events.Distributed.Hit += OnDistributedHit;
            _events.Distributed.Miss += OnDistributedMiss;
        }

        internal LayerCounts Snapshot => new(Interlocked.Read(ref _memoryHits), Interlocked.Read(ref _memoryMisses),
            Interlocked.Read(ref _distributedHits), Interlocked.Read(ref _distributedMisses));

        // Never read, retain, serialize, or print event arguments, especially their keys.
        private void OnMemoryHit(object? sender, FusionCacheEntryHitEventArgs args) => Interlocked.Increment(ref _memoryHits);
        private void OnMemoryMiss(object? sender, FusionCacheEntryEventArgs args) => Interlocked.Increment(ref _memoryMisses);
        private void OnDistributedHit(object? sender, FusionCacheEntryHitEventArgs args) => Interlocked.Increment(ref _distributedHits);
        private void OnDistributedMiss(object? sender, FusionCacheEntryEventArgs args) => Interlocked.Increment(ref _distributedMisses);

        public void Dispose()
        {
            _events.Memory.Hit -= OnMemoryHit;
            _events.Memory.Miss -= OnMemoryMiss;
            _events.Distributed.Hit -= OnDistributedHit;
            _events.Distributed.Miss -= OnDistributedMiss;
        }
    }

    private sealed class Fixture : IDisposable
    {
        private readonly bool _enabled;
        private readonly CapturingExporter _exporter = new();
        private int _exporterCreations;
        internal FusionCacheOptions Options { get; }
        internal FusionCache Cache { get; }
        internal EngineTelemetrySession Session { get; }
        internal int ExporterCreations => Volatile.Read(ref _exporterCreations);

        internal Fixture(IDistributedCache? distributed = null, bool enabled = true, bool? synchronousEvents = true)
        {
            _enabled = enabled;
            Options = CreateOptions(synchronousEvents);
            Cache = CreateCache(Options, distributed);
            Session = EngineTelemetrySession.Create(
                exporterFactory: () => { Interlocked.Increment(ref _exporterCreations); return _exporter; },
                enableSyntheticCollection: enabled,
                readEnvironmentVariable: _ => null,
                showNotice: () => { },
                resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"),
                startTimer: false);
            Entity entity = new(Source: new(SQL_SENTINEL, EntitySourceType.Table, null, null),
                GraphQL: null!, Fields: null, Rest: null!, Permissions: [], Mappings: null, Relationships: null);
            RuntimeCacheOptions caching = new(Enabled: true, TtlSeconds: 3600)
            {
                Level2 = distributed is null ? null : new(Enabled: true)
            };
            Session.AcceptConfiguration(new RuntimeConfig(
                Schema: null,
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty),
                Runtime: new RuntimeOptions(new RestRuntimeOptions(), new GraphQLRuntimeOptions(), new McpRuntimeOptions(), Host: null, Cache: caching),
                Entities: new RuntimeEntities(new Dictionary<string, Entity> { [ENTITY] = entity })));
            Session.MarkHostReady();
            Assert.AreEqual(enabled, Session.IsEnabled);
            Assert.AreEqual(enabled, Session.IsReady);
        }

        internal async Task<EngineTelemetryEvent[]> DrainAsync()
        {
            await Session.StopAsync().WaitAsync(_testTimeout);
            if (_enabled)
            {
                await _exporter.Disposed.Task.WaitAsync(_testTimeout);
            }

            EngineTelemetryEvent[] records = _exporter.Events.ToArray();
            if (_enabled)
            {
                Assert.AreEqual(1, records.Count(record => record.Name == "dab.engine.stopped"));
                Assert.IsTrue(records.All(record => record.Properties["sender_dropped_events"] == "0"));
            }

            string serialized = JsonSerializer.Serialize(records);
            Assert.IsFalse(serialized.Contains(SQL_SENTINEL, StringComparison.Ordinal), "SQL and cache-key content must not reach product events.");
            Assert.IsFalse(serialized.Contains(ROW_SENTINEL, StringComparison.Ordinal), "Rows and parameter values must not reach product events.");
            return records;
        }

        public void Dispose()
        {
            Session.Dispose();
            Cache.Dispose();
        }
    }

    private sealed class CapturingExporter : IEngineTelemetryExporter
    {
        internal ConcurrentQueue<EngineTelemetryEvent> Events { get; } = new();
        internal TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Enqueue(record);
            return ValueTask.FromResult(true);
        }

        public void Dispose() => Disposed.TrySetResult();
    }
}
