// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Primitives;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using static Azure.DataApiBuilder.Config.DabConfigEvents;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Exercises the real file-reload dispatcher, MCP registry refresh and telemetry session.
    /// Only the filesystem, config-provider accessor, database metadata and notification sink
    /// are substituted; no database, OS watcher, transport or ambient telemetry setup is used.
    /// Other ordered subscribers only record observations; this is not a full host test.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    [DoNotParallelize]
    public class EngineTelemetryMcpReloadFailureTests
    {
        private const string PROCESS_STARTED = "dab.engine.process_started";
        private const string READY = "dab.engine.ready";
        private const string CHANGED = "dab.engine.configuration_changed";
        private const string REJECTED = "dab.engine.configuration_change_failed";
        private const string STOPPED = "dab.engine.stopped";
        private const string SENTINEL = "MCP_RELOAD_PRIVATE_FAILURE_12a984";
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);
        private static readonly string[] _reloadEvents =
        [
            QUERY_MANAGER_FACTORY_ON_CONFIG_CHANGED,
            METADATA_PROVIDER_FACTORY_ON_CONFIG_CHANGED,
            QUERY_ENGINE_FACTORY_ON_CONFIG_CHANGED,
            MUTATION_ENGINE_FACTORY_ON_CONFIG_CHANGED,
            DOCUMENTOR_ON_CONFIG_CHANGED,
            AUTHZ_RESOLVER_ON_CONFIG_CHANGED,
            MCP_TOOL_REGISTRY_ON_CONFIG_CHANGED,
            GRAPHQL_SCHEMA_EVICTION_ON_CONFIG_CHANGED,
            GRAPHQL_SCHEMA_CREATOR_ON_CONFIG_CHANGED,
            GRAPHQL_SCHEMA_REFRESH_ON_CONFIG_CHANGED,
            LOG_LEVEL_INITIALIZER_ON_CONFIG_CHANGE
        ];

        [TestMethod]
        public async Task FileReloadWithSwallowedRegistryFailureRetainsEpochAndSnapshotThenRecovers()
        {
            using ReloadFixture fixture = new();
            IMcpTool originalTool = GetRequiredTool(fixture.Registry, "get_book");
            string originalDiscovery = JsonSerializer.Serialize(fixture.Registry.GetAdvertisedTools());

            // The generated read_records tool conflicts with the real built-in. The failure
            // occurs inside McpToolRegistryRefreshService, not a synthetic throwing subscriber.
            fixture.Reload(CreateConfigJson(entityName: "ReadRecords", description: SENTINEL));

            Assert.AreSame(originalTool, GetRequiredTool(fixture.Registry, "get_book"));
            Assert.AreSame(fixture.BuiltInTool, GetRequiredTool(fixture.Registry, "read_records"));
            Assert.AreEqual(originalDiscovery, JsonSerializer.Serialize(fixture.Registry.GetAdvertisedTools()));
            fixture.Notifier.Verify(notifier => notifier.NotifyToolsListChanged(), Times.Never);
            VerifyErrorLog(fixture.Logger, "The previous registry snapshot remains active.");
            TelemetryFailureContext? failedAttempt = fixture.RegistryAttempts.Single();
            Assert.IsNotNull(failedAttempt);
            Assert.IsTrue(failedAttempt.HasFailure);
            Assert.AreEqual(TelemetryFailureStage.Serving, failedAttempt.FailureStage);

            (RuntimeConfig? config, bool accepted, TelemetryFailureStage stage) = fixture.Completions.Single();
            Assert.IsFalse(accepted);
            Assert.IsNull(config, "A failed MCP rebuild must not supply an accepted configuration to telemetry.");
            Assert.AreEqual(TelemetryFailureStage.Serving, stage);
            AssertConfiguration(fixture.Session, fixture.InitialConfig, epoch: 1);
            Assert.IsTrue(fixture.Session.IsReady);
            CollectionAssert.AreEqual(
                new[] { "change_token" }.Concat(_reloadEvents).Append("rejected").ToArray(),
                fixture.Trace,
                "Swallowing the MCP exception must still allow GraphQL and logging handlers to finish.");
            Assert.IsTrue(fixture.SubscriberConfigurations.All(observation =>
                ReferenceEquals(fixture.InitialConfig, observation.Config) && observation.Epoch == 1));

            // The loader retains the parsed config for other components. This fix
            // rejects the telemetry epoch, not the application's partial configuration state.
            Assert.IsNotNull(fixture.Loader.RuntimeConfig);
            Assert.AreNotSame(fixture.InitialConfig, fixture.Loader.RuntimeConfig);
            Assert.IsTrue(fixture.Loader.RuntimeConfig.Entities.ContainsKey("ReadRecords"));
            Assert.IsNull(TelemetryFailureContext.Current);

            fixture.Trace.Clear();
            fixture.SubscriberConfigurations.Clear();
            fixture.Reload(CreateConfigJson(description: "Recovered book tool"));

            Assert.AreNotSame(originalTool, GetRequiredTool(fixture.Registry, "get_book"));
            Assert.AreEqual("Recovered book tool", fixture.Registry.GetAdvertisedTools()
                .Single(tool => tool.Name == "get_book").Description);
            fixture.Notifier.Verify(notifier => notifier.NotifyToolsListChanged(), Times.Once);
            (string? description, RuntimeConfig? notifiedConfig, long notifiedEpoch) = fixture.Notifications.Single();
            Assert.AreEqual("Recovered book tool", description,
                "The replacement registry must be published before notifying clients.");
            Assert.AreSame(fixture.InitialConfig, notifiedConfig);
            Assert.AreEqual(1L, notifiedEpoch,
                "Notification is still inside ordered dispatch, before telemetry acceptance.");
            Assert.AreEqual(2, fixture.Completions.Count);
            Assert.IsTrue(fixture.Completions[1].Accepted);
            Assert.AreSame(fixture.Loader.RuntimeConfig, fixture.Completions[1].Config);
            AssertConfiguration(fixture.Session, fixture.Loader.RuntimeConfig, epoch: 2);
            CollectionAssert.AreEqual(
                new[] { "change_token" }.Concat(_reloadEvents).Append("accepted").ToArray(), fixture.Trace);
            Assert.IsTrue(fixture.SubscriberConfigurations.All(observation =>
                ReferenceEquals(fixture.InitialConfig, observation.Config) && observation.Epoch == 1));
            TelemetryFailureContext? recoveredAttempt = fixture.RegistryAttempts.Last();
            Assert.IsNotNull(recoveredAttempt);
            Assert.AreNotSame(failedAttempt, recoveredAttempt);
            Assert.IsFalse(recoveredAttempt.HasFailure);
            Assert.IsNull(TelemetryFailureContext.Current);

            EngineTelemetryEvent[] records = await fixture.DrainAsync();
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, REJECTED, CHANGED, STOPPED },
                records.Select(record => record.Name).ToArray());
            EngineTelemetryEvent rejected = records.Single(record => record.Name == REJECTED);
            Assert.AreEqual(1L, rejected.ConfigurationEpoch);
            Assert.AreEqual("serving", rejected.Properties["failure_stage"]);
            Assert.AreEqual("configuration", rejected.Properties["failure_category"]);
            Assert.IsFalse(rejected.Properties.ContainsKey("snapshot_schema"));
            EngineTelemetryEvent changed = records.Single(record => record.Name == CHANGED);
            Assert.AreEqual(2L, changed.ConfigurationEpoch,
                "Only recovery may emit configuration_changed; the rejected candidate has no accepted epoch.");
            Assert.AreEqual("hot_reload", changed.Properties["configuration_delivery"]);
        }

        [TestMethod]
        public async Task FileReloadWithUnchangedDiscoveryAcceptsConfigurationWithoutNotification()
        {
            using ReloadFixture fixture = new();
            IMcpTool originalTool = GetRequiredTool(fixture.Registry, "get_book");
            string originalDiscovery = JsonSerializer.Serialize(fixture.Registry.GetAdvertisedTools());

            fixture.Reload(CreateConfigJson());

            // RefreshRegistry's false result means no notification, not necessarily failure.
            Assert.AreNotSame(originalTool, GetRequiredTool(fixture.Registry, "get_book"));
            Assert.AreEqual(originalDiscovery, JsonSerializer.Serialize(fixture.Registry.GetAdvertisedTools()));
            fixture.Notifier.Verify(notifier => notifier.NotifyToolsListChanged(), Times.Never);
            Assert.IsTrue(fixture.Completions.Single().Accepted);
            AssertConfiguration(fixture.Session, fixture.Loader.RuntimeConfig, epoch: 2);
            TelemetryFailureContext? attempt = fixture.RegistryAttempts.Single();
            Assert.IsNotNull(attempt);
            Assert.IsFalse(attempt.HasFailure);

            EngineTelemetryEvent[] records = await fixture.DrainAsync();
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, CHANGED, STOPPED },
                records.Select(record => record.Name).ToArray());
        }

        [TestMethod]
        public async Task FileReloadWithNotificationFailureStillAcceptsPublishedRegistry()
        {
            using ReloadFixture fixture = new();
            fixture.FailNotification = true;

            fixture.Reload(CreateConfigJson(description: "Published despite notification failure"));

            Assert.AreEqual("Published despite notification failure", fixture.Registry.GetAdvertisedTools()
                .Single(tool => tool.Name == "get_book").Description);
            fixture.Notifier.Verify(notifier => notifier.NotifyToolsListChanged(), Times.Once);
            VerifyErrorLog(fixture.Logger, "Failed to notify an MCP client");
            Assert.IsTrue(fixture.Completions.Single().Accepted);
            AssertConfiguration(fixture.Session, fixture.Loader.RuntimeConfig, epoch: 2);
            TelemetryFailureContext? attempt = fixture.RegistryAttempts.Single();
            Assert.IsNotNull(attempt);
            Assert.IsFalse(attempt.HasFailure, "Transport notification failure is not a registry publication failure.");

            EngineTelemetryEvent[] records = await fixture.DrainAsync();
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, CHANGED, STOPPED },
                records.Select(record => record.Name).ToArray());
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task FileReloadCanceledDuringRegistryRebuildNeverPublishesOrReportsAcceptance(bool telemetryEnabled)
        {
            using ReloadFixture fixture = new(telemetryEnabled);
            IMcpTool originalTool = GetRequiredTool(fixture.Registry, "get_book");
            string originalDiscovery = JsonSerializer.Serialize(fixture.Registry.GetAdvertisedTools());
            // Request real loader shutdown during MCP metadata enrichment, after ordered
            // dispatch has entered the refresh service but before it publishes its candidate.
            fixture.BeforeMetadataRead = fixture.Loader.Dispose;

            fixture.Reload(CreateConfigJson(description: "Canceled candidate"));
            await fixture.Loader.StopAsync(CancellationToken.None).WaitAsync(_timeout);

            Assert.AreSame(originalTool, GetRequiredTool(fixture.Registry, "get_book"));
            Assert.AreEqual(originalDiscovery, JsonSerializer.Serialize(fixture.Registry.GetAdvertisedTools()));
            fixture.Notifier.Verify(notifier => notifier.NotifyToolsListChanged(), Times.Never);
            Assert.AreEqual(0, fixture.Completions.Count,
                "Shutdown cancellation retains the loader's existing no-completion outcome.");
            CollectionAssert.AreEqual(new[] { "change_token" }
                .Concat(_reloadEvents.Take(Array.IndexOf(_reloadEvents, MCP_TOOL_REGISTRY_ON_CONFIG_CHANGED) + 1))
                .ToArray(), fixture.Trace);
            TelemetryFailureContext? attempt = fixture.RegistryAttempts.Single();
            if (telemetryEnabled)
            {
                Assert.IsNotNull(attempt);
                Assert.IsTrue(attempt.HasFailure,
                    "The swallowed MCP cancellation must already be recorded before the next ordered handler checks cancellation.");
                Assert.AreEqual(TelemetryFailureStage.Serving, attempt.FailureStage);
                AssertConfiguration(fixture.Session, fixture.InitialConfig, epoch: 1);
            }
            else
            {
                Assert.IsNull(attempt, "Disabled telemetry must not create a failure context.");
            }

            Assert.IsNull(TelemetryFailureContext.Current);
            EngineTelemetryEvent[] records = await fixture.DrainAsync();
            CollectionAssert.AreEqual(telemetryEnabled ? new[] { PROCESS_STARTED, READY, STOPPED } : Array.Empty<string>(),
                records.Select(record => record.Name).ToArray());
        }

        private static string CreateConfigJson(string entityName = "GetBook", string description = "Initial book tool") => $$"""
            {
              "$schema": "{{RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK}}",
              "data-source": {
                "database-type": "mssql",
                "connection-string": "Server=mcp-reload.invalid;Database=unused;Integrated Security=true;Connect Timeout=1;"
              },
              "runtime": {
                "host": { "mode": "development", "authentication": { "provider": "Unauthenticated" } },
                "rest": { "enabled": true },
                "graphql": { "enabled": false },
                "mcp": { "enabled": true }
              },
              "entities": {
                "{{entityName}}": {
                  "source": { "object": "dbo.test_procedure", "type": "stored-procedure" },
                  "rest": { "enabled": true },
                  "graphql": { "enabled": false },
                  "permissions": [{ "role": "anonymous", "actions": ["execute"] }],
                  "description": "{{description}}",
                  "mcp": { "custom-tool": true }
                }
              }
            }
            """;

        private static IMcpTool GetRequiredTool(McpToolRegistry registry, string name)
        {
            Assert.IsTrue(registry.TryGetTool(name, out IMcpTool? tool));
            Assert.IsNotNull(tool);
            return tool;
        }

        private static void AssertConfiguration(EngineTelemetrySession session, RuntimeConfig? config, long epoch)
        {
            using EngineTelemetryRequestScope request = session.BeginRequest(
                EngineTelemetryApi.Mcp, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            Assert.AreSame(config, request.Config);
            Assert.AreEqual(epoch, request.Configuration.Epoch);
        }

        private static void VerifyErrorLog(Mock<ILogger<McpToolRegistryRefreshService>> logger, string message)
        {
            logger.Verify(value => value.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((state, _) => state.ToString()!.Contains(message, StringComparison.Ordinal)),
                It.IsAny<Exception>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()), Times.Once);
        }

        private sealed class ReloadFixture : IDisposable
        {
            private readonly MockFileSystem _files = new();
            private readonly RuntimeConfigProvider _configProvider;
            private readonly IDisposable _changeRegistration;
            private readonly bool _telemetryEnabled;
            private readonly CapturingExporter _exporter = new();
            public FileSystemRuntimeConfigLoader Loader { get; }
            public EngineTelemetrySession Session { get; }
            public RuntimeConfig InitialConfig { get; }
            public McpToolRegistry Registry { get; } = new();
            public ReadRecordsTool BuiltInTool { get; } = new();
            public Mock<IMcpToolListChangedNotifier> Notifier { get; } = new();
            public Mock<ILogger<McpToolRegistryRefreshService>> Logger { get; } = new();
            public List<string> Trace { get; } = new();
            public List<(RuntimeConfig? Config, long Epoch)> SubscriberConfigurations { get; } = new();
            public List<TelemetryFailureContext?> RegistryAttempts { get; } = new();
            public List<(RuntimeConfig? Config, bool Accepted, TelemetryFailureStage Stage)> Completions { get; } = new();
            public List<(string? Description, RuntimeConfig? Config, long Epoch)> Notifications { get; } = new();
            public Action? BeforeMetadataRead { get; set; }
            public bool FailNotification { get; set; }

            public ReloadFixture(bool telemetryEnabled = true)
            {
                _telemetryEnabled = telemetryEnabled;
                Session = EngineTelemetrySession.Create(
                    () => _exporter, enableSyntheticCollection: telemetryEnabled,
                    readEnvironmentVariable: _ => null, showNotice: () => { },
                    resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);
                HotReloadEventHandler<HotReloadEventArgs> handler = new();
                string path = _files.Path.Combine(_files.Directory.GetCurrentDirectory(), "engine-telemetry-mcp-reload.json");
                _files.AddFile(path, new MockFileData(CreateConfigJson()));
                Loader = new(_files, handler, baseConfigFilePath: path, isCliLoader: true,
                    logger: NullLogger<FileSystemRuntimeConfigLoader>.Instance)
                {
                    TelemetryCaptureEnabled = () => Session.IsEnabled
                };
                Assert.IsTrue(Loader.TryLoadKnownConfig(out RuntimeConfig? initial));
                Assert.IsNotNull(initial);
                InitialConfig = initial;

                // Use a separate base loader for this accessor mock so the real file loader
                // does not acquire RuntimeConfigProvider's disk/schema validation subscriber.
                Mock<RuntimeConfigLoader> providerLoader = new(null, null);
                Mock<RuntimeConfigProvider> provider = new(providerLoader.Object);
                provider.Setup(value => value.GetConfig()).Returns(() => Loader.RuntimeConfig!);
                _configProvider = provider.Object;
                Mock<ISqlMetadataProvider> sqlMetadata = new();
                sqlMetadata.SetupGet(value => value.EntityToDatabaseObject).Returns(new Dictionary<string, DatabaseObject>());
                Mock<IMetadataProviderFactory> metadata = new();
                metadata.Setup(value => value.GetMetadataProvider(It.IsAny<string>())).Returns(() =>
                {
                    BeforeMetadataRead?.Invoke();
                    return sqlMetadata.Object;
                });
                Notifier.Setup(value => value.NotifyToolsListChanged()).Callback(() =>
                {
                    using EngineTelemetryRequestScope request = Session.BeginRequest(
                        EngineTelemetryApi.Mcp, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
                    Notifications.Add((Registry.GetAdvertisedTools().Single(tool => tool.Name == "get_book").Description,
                        request.Config, request.Configuration.Epoch));
                    if (FailNotification)
                    {
                        throw new InvalidOperationException(SENTINEL);
                    }
                });
                McpToolRegistryRefreshService refresh = new(
                    _configProvider, new[] { BuiltInTool }, Registry, metadata.Object,
                    new[] { Notifier.Object }, Logger.Object, handler);
                refresh.EnsureInitialized();
                Assert.AreEqual(2, Registry.GetAdvertisedTools().Count);
                Session.AcceptConfiguration(initial);
                Session.MarkHostReady();
                Assert.AreEqual(telemetryEnabled, Session.IsReady);

                _changeRegistration = ChangeToken.OnChange(Loader.GetChangeToken, () => ObserveSubscriber("change_token"));
                foreach (string eventName in _reloadEvents)
                {
                    // Register after the real refresh callback, so MCP observations see its
                    // swallowed outcome before the next ordered event can alter the context.
                    handler.Subscribe(eventName, (_, _) => ObserveSubscriber(eventName));
                }

                // Mirror Startup's forwarding only. Failure classification and the acceptance
                // decision must come from the real loader, never from test-side repair logic.
                Loader.TelemetryReloadCompleted = (config, accepted, stage) =>
                {
                    Completions.Add((config, accepted, stage));
                    Trace.Add(accepted ? "accepted" : "rejected");
                    if (accepted && config is not null)
                    {
                        Session.AcceptConfiguration(config, "hot_reload", Loader.ConfigFilePath);
                    }
                    else
                    {
                        Session.ConfigurationChangeFailed(stage);
                    }
                };
            }

            public void Reload(string json)
            {
                _files.File.WriteAllText(Loader.ConfigFilePath, json);
                Loader.ProcessHotReloadNotification();
            }

            public async Task<EngineTelemetryEvent[]> DrainAsync()
            {
                await Session.StopAsync().WaitAsync(_timeout);
                if (_telemetryEnabled)
                {
                    await _exporter.Disposed.Task.WaitAsync(_timeout);
                }

                EngineTelemetryEvent[] records = _exporter.Records.ToArray();
                Assert.IsFalse(records.Any(record => record.Properties.Any(property =>
                    property.Key.Contains(SENTINEL, StringComparison.Ordinal) ||
                    property.Value.Contains(SENTINEL, StringComparison.Ordinal))));
                return records;
            }

            private void ObserveSubscriber(string eventName)
            {
                Trace.Add(eventName);
                using EngineTelemetryRequestScope request = Session.BeginRequest(
                    EngineTelemetryApi.Mcp, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
                SubscriberConfigurations.Add((request.Config, request.Configuration.Epoch));
                if (eventName == MCP_TOOL_REGISTRY_ON_CONFIG_CHANGED)
                {
                    RegistryAttempts.Add(TelemetryFailureContext.Current);
                }
            }

            public void Dispose()
            {
                _changeRegistration.Dispose();
                _configProvider.Dispose();
                Loader.Dispose();
                Session.Dispose();
            }
        }

        private sealed class CapturingExporter : IEngineTelemetryExporter
        {
            public ConcurrentQueue<EngineTelemetryEvent> Records { get; } = new();
            public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Records.Enqueue(record);
                return ValueTask.FromResult(true);
            }

            public void Dispose() => Disposed.TrySetResult();
        }
    }
}
