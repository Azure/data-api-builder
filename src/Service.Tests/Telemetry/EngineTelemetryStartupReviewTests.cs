// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ZiggyCreatures.Caching.Fusion;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry;

/// <summary>
/// Registration/closure regressions only: no host, config files, database, or external exporter.
/// Cache dispatch checks characterize the synthetic-only trade-off, not production latency.
/// </summary>
[TestClass]
[TestCategory("EngineTelemetry")]
[DoNotParallelize]
public class EngineTelemetryStartupReviewTests
{
    [TestMethod]
    public async Task ConfigureServicesRetainsOriginalOwnerForRegistrationsAndReloadCallbacks()
    {
        CapturingExporter originalExporter = new();
        CapturingExporter replacementExporter = new();
        using EngineTelemetrySession original = CreateSession(originalExporter, enabled: true);
        using EngineTelemetrySession replacement = CreateSession(replacementExporter, enabled: true);
        Startup startup = CreateStartup(original);
        ServiceCollection services = ConfigureServices(startup);
        startup.ProductTelemetry = replacement;
        await using ServiceProvider provider = BuildProvider(services);
        FileSystemRuntimeConfigLoader loader = provider.GetRequiredService<FileSystemRuntimeConfigLoader>();
        Assert.AreEqual(startup.Configuration["ConfigFileName"], loader.ConfigFilePath);
        Assert.AreSame(original, provider.GetRequiredService<EngineTelemetrySession>());
        Assert.AreSame(original, provider.GetRequiredService<IProductTelemetryControl>());
        Assert.AreSame(original, provider.GetRequiredService<RuntimeConfigProvider>().ProductTelemetry);
        Action<RuntimeConfig?, bool, TelemetryFailureStage>? callback = loader.TelemetryReloadCompleted;
        Assert.IsNotNull(callback);

        RuntimeConfig config = new(Schema: null, DataSource: new(DatabaseType.MSSQL, string.Empty),
            Runtime: new(Rest: new(Enabled: true), GraphQL: new(Enabled: false), Mcp: new(Enabled: false), Host: null),
            Entities: new(new Dictionary<string, Entity>()));
        original.MarkHostReady(); // Synthetic state transition; this is not hosting/readiness integration.
        callback(config, true, TelemetryFailureStage.Unknown);
        Assert.IsTrue(original.IsReady);
        Assert.IsFalse(replacement.IsReady);
        callback(config with { }, true, TelemetryFailureStage.Unknown);
        callback(null, false, TelemetryFailureStage.Metadata);
        await Task.WhenAll(original.StopAsync(), replacement.StopAsync());

        EngineTelemetryEvent[] records = originalExporter.Events.ToArray();
        CollectionAssert.AreEqual(new[]
        {
            "dab/engine.process_started", "dab/engine.ready", "dab/engine.configuration_changed",
            "dab/engine.configuration_change_failed", "dab/engine.stopped"
        },
            records.Select(record => record.Name).ToArray());
        CollectionAssert.AreEqual(new[] { "dab/engine.process_started", "dab/engine.stopped" },
            replacementExporter.Events.Select(record => record.Name).ToArray());
        EngineTelemetryEvent changed = records.Single(record => record.Name == "dab/engine.configuration_changed");
        Assert.AreEqual(2L, changed.ConfigurationEpoch);
        Assert.AreEqual("hot_reload", changed.Properties["configuration_delivery"]);
        EngineTelemetryEvent failed = records.Single(record => record.Name == "dab/engine.configuration_change_failed");
        Assert.AreEqual(2L, failed.ConfigurationEpoch);
        Assert.AreEqual("metadata", failed.Properties["failure_stage"]);
    }

    [DataTestMethod]
    [DataRow(true, false)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    [DataRow(false, true)]
    public async Task CacheOptionsUseOriginalOwnerAfterStartupPropertyReplacement(bool originalEnabled, bool embeddingsCache)
    {
        using EngineTelemetrySession original = CreateSession(new(), originalEnabled);
        using EngineTelemetrySession replacement = CreateSession(new(), !originalEnabled);
        Startup startup = CreateStartup(original);
        ServiceCollection services = ConfigureServices(startup, embeddingsCache);
        startup.ProductTelemetry = replacement;
        await using ServiceProvider provider = BuildProvider(services);
        IFusionCache cache = embeddingsCache
            ? provider.GetRequiredService<IFusionCacheProvider>().GetCache("EmbeddingsCache")
            : provider.GetRequiredService<IFusionCache>();
        // FusionCache 2.1 uses IOptions for the default cache and IOptionsMonitor for
        // named caches; the monitor's separate default instance is not the configured cache.
        FusionCacheOptions options = embeddingsCache
            ? provider.GetRequiredService<IOptionsMonitor<FusionCacheOptions>>().Get(cache.CacheName)
            : provider.GetRequiredService<IOptions<FusionCacheOptions>>().Value;
        Assert.AreEqual(originalEnabled, options.EnableSyncEventHandlersExecution);
        await Task.WhenAll(original.StopAsync(), replacement.StopAsync());
    }

    [TestMethod]
    public async Task EnabledDefaultCacheRunsThirdPartyHandlerOnCallingThread()
    {
        using EngineTelemetrySession original = CreateSession(new(), enabled: true);
        using EngineTelemetrySession replacement = CreateSession(new(), enabled: false);
        Startup startup = CreateStartup(original);
        ServiceCollection services = ConfigureServices(startup);
        startup.ProductTelemetry = replacement;
        await using ServiceProvider provider = BuildProvider(services);
        IFusionCache cache = provider.GetRequiredService<IFusionCache>();
        int callingThread = Environment.CurrentManagedThreadId;
        int handlerThread = 0;
        cache.Events.Memory.Miss += (_, _) => Volatile.Write(ref handlerThread, Environment.CurrentManagedThreadId);

        Assert.IsFalse(cache.TryGet<string>("startup-review-absent").HasValue);
        Assert.AreEqual(callingThread, Volatile.Read(ref handlerThread),
            "The real Startup registration synchronizes unrelated handlers too, before TryGet returns.");
        await Task.WhenAll(original.StopAsync(), replacement.StopAsync());
    }

    private static Startup CreateStartup(EngineTelemetrySession session)
    {
        string absentPath = Path.Combine(Path.GetTempPath(), "dab-startup-review-" + Guid.NewGuid().ToString("N"), "missing.json");
        Assert.IsFalse(File.Exists(absentPath));
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConfigFileName"] = absentPath
        }).Build();
        return new Startup(configuration, NullLogger<Startup>.Instance) { ProductTelemetry = session };
    }

    private static ServiceCollection ConfigureServices(Startup startup, bool embeddingsCache = false)
    {
        ServiceCollection services = new();
        services.AddLogging();
        startup.ConfigureServices(services);
        if (embeddingsCache)
        {
            EmbeddingsOptions options = new(EmbeddingProviderType.OpenAI, "https://synthetic.invalid", "synthetic",
                Cache: new(Enabled: true));
            MethodInfo? configure = typeof(Startup).GetMethod("ConfigureEmbeddingsCache",
                BindingFlags.Instance | BindingFlags.NonPublic, binder: null,
                types: [typeof(IServiceCollection), typeof(EmbeddingsOptions), typeof(bool)], modifiers: null);
            Assert.IsNotNull(configure);
            configure.Invoke(startup, [services, options, true]);
        }

        return services;
    }

    private static ServiceProvider BuildProvider(ServiceCollection services)
    {
        ServiceProvider provider = services.BuildServiceProvider();
        // Resolve the eagerly constructed registrations so asynchronous root disposal owns cleanup.
        Assert.IsNull(provider.GetRequiredService<FileSystemRuntimeConfigLoader>().RuntimeConfig);
        Assert.IsFalse(provider.GetRequiredService<RuntimeConfigProvider>().TryGetLoadedConfig(out _));
        return provider;
    }

    private static EngineTelemetrySession CreateSession(CapturingExporter exporter, bool enabled)
    {
        EngineTelemetrySession session = EngineTelemetrySession.Create(exporterFactory: () => exporter,
            enableSyntheticCollection: enabled, readEnvironmentVariable: _ => null, showNotice: () => { },
            resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);
        Assert.AreEqual(enabled, session.IsEnabled);
        return session;
    }

    private sealed class CapturingExporter : IEngineTelemetryExporter
    {
        internal ConcurrentQueue<EngineTelemetryEvent> Events { get; } = new();

        public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Enqueue(record);
            return ValueTask.FromResult(true);
        }

        public void Dispose() { }
    }
}
