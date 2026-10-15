// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Offline CLI-to-engine handoffs through the real sessions, delivery workers and an isolated
    /// sender pool. Identity callbacks supply store evidence; no config or identity file is read
    /// or written. The entry-point tests exercise real preflight/catch/finally paths, not a running
    /// host, a database, the environment-based bootstrap factory or an SDK/network exporter.
    /// </summary>
    [TestClass]
    [TestCategory("CliTelemetry")]
    public class CliTelemetryLaunchTests
    {
        private const string SENTINEL = "PRIVATE_CLI_LAUNCH_6c24";
        private const string FIRST_RUN = "dab.cli.first_run";
        private const string COMMAND = "dab.cli.command";
        private const string LAUNCH = "dab.cli.engine_launch";
        private const string PROCESS_STARTED = "dab.engine.process_started";
        private const string STARTUP_FAILED = "dab.engine.startup_failed";
        private const string READY = "dab.engine.ready";
        private const string CONFIGURATION_CHANGED = "dab.engine.configuration_changed";
        private const string STOPPED = "dab.engine.stopped";
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);
        private static readonly Guid _installationId = new("a14ad4f4-b34a-433a-b61f-c7babfa36073");
        private static readonly Guid _apiId = new("6627db99-71d6-4f81-a41f-559114f8eb74");
        private static readonly Guid _otherApiId = new("fbe13d4c-1a75-411f-94db-c881e0cff488");
        private static readonly string _root = CanonicalRoot("dab-config.json");
        private static readonly string _otherRoot = CanonicalRoot("dab-config.production.merged.json");
        private static readonly ImmutableDictionary<string, string> _noOptions = ImmutableDictionary<string, string>.Empty;

        [DataTestMethod]
        [DataRow((int)CliTelemetryLaunchSource.StartWeb, "start_web", "web")]
        [DataRow((int)CliTelemetryLaunchSource.StartStdio, "start_stdio", "mcp_stdio")]
        [DataRow((int)CliTelemetryLaunchSource.ExportGraphQL, "export_graphql", "web")]
        public async Task LaunchHandoffPrecedesFirstEngineEventAndStartupFailure(int source, string wireSource, string executionMode)
        {
            using OfflineSender sender = new();
            using CliTelemetrySession cli = sender.CreateCli(installation: new(_installationId, "newly_saved"));
            ProductTelemetryLaunchContext launch = BeginLaunch(cli, _root, (CliTelemetryLaunchSource)source);
            using EngineTelemetrySession engine = sender.CreateEngine(launch, _root, executionMode);

            engine.StartupFailed(TelemetryFailureStage.Configuration);
            engine.StartupFailed(TelemetryFailureStage.Parsing);
            cli.Complete(source == (int)CliTelemetryLaunchSource.ExportGraphQL ? "export" : "start", "none", _noOptions,
                CliTelemetryOutcome.ExecutionFailure, CliTelemetryFailureCategory.Initialization);
            await DrainAsync(cli, engine);

            CliTelemetryEvent[] cliEvents = sender.Events.OfType<CliTelemetryEvent>().ToArray();
            EngineTelemetryEvent[] engineEvents = sender.Events.OfType<EngineTelemetryEvent>().ToArray();
            CollectionAssert.AreEqual(new[] { FIRST_RUN, LAUNCH, COMMAND }, cliEvents.Select(record => record.Name).ToArray());
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, STARTUP_FAILED, STOPPED }, engineEvents.Select(record => record.Name).ToArray());
            Assert.AreNotEqual(cli.SessionId, launch.EngineSessionId);
            Assert.AreEqual(cli.SessionId, launch.ParentCliSessionId);
            AssertLaunchRecord(sender, launch, wireSource);
            Assert.AreEqual(1L, cliEvents[0].Sequence);
            Assert.AreEqual(2L, cliEvents[1].Sequence);
            Assert.AreEqual(1L, engineEvents[0].Sequence, "Engine sequencing must not continue the CLI's sequence.");
            foreach (EngineTelemetryEvent record in engineEvents)
            {
                AssertEngineLink(record, launch, wireSource);
                AssertInstallation(record, _installationId, "newly_saved");
                AssertApiIdentity(record, _apiId, "ephemeral");
                Assert.AreEqual(executionMode, record.Properties["execution_mode"]);
                Assert.AreEqual(0L, record.ConfigurationEpoch);
            }

            Assert.AreEqual("configuration", engineEvents[1].Properties["failure_stage"]);
            Assert.AreEqual("initialization", engineEvents[1].Properties["failure_category"]);
            CollectionAssert.AreEqual(new[] { _root }, sender.CreatedPaths.ToArray());
            Assert.AreEqual(0, sender.EngineIdentityPaths.Count, "Even the earliest failure must use the supplied identity without resolving it again.");
            Assert.AreEqual(1, sender.FactoryCalls, "Both producers must reach the same generic exporter.");
            AssertEnvelopes(sender.Events);
        }

        [TestMethod]
        public async Task SavedApiIdentityLinksInitAddAndLaunchSessions()
        {
            using OfflineSender sender = new();
            EngineTelemetryIdentity saved = new(_apiId, "newly_saved");
            EngineTelemetryIdentity reused = new(_apiId, "reused");

            // The injected callbacks represent a successful save and subsequent store reads.
            // This tests session correlation, not CLI command execution or sidecar persistence.
            using CliTelemetrySession init = sender.CreateCli(createIdentity: _ => saved);
            init.ConfigurationCreated(_root);
            Complete(init, "init");
            await init.StopAsync().WaitAsync(_timeout);

            using CliTelemetrySession add = sender.CreateCli(lookupIdentity: _ => reused);
            add.ObserveConfiguration(_root);
            Complete(add, "add");
            await add.StopAsync().WaitAsync(_timeout);

            using CliTelemetrySession start = sender.CreateCli(createIdentity: _ => reused);
            ProductTelemetryLaunchContext launch = BeginLaunch(start, _root, CliTelemetryLaunchSource.StartWeb);
            using EngineTelemetrySession engine = sender.CreateEngine(launch, _root);
            engine.AcceptConfiguration(CreateConfig(), configPath: _root);
            engine.MarkHostReady();
            Assert.IsTrue(engine.IsReady);
            Complete(start, "start");
            await DrainAsync(start, engine);

            IProductTelemetryEvent[] events = sender.Events;
            Assert.AreEqual(4, events.Select(record => record.SessionId).Distinct().Count());
            AssertApiIdentity(Event(events, init.SessionId, COMMAND), _apiId, "newly_saved");
            AssertApiIdentity(Event(events, add.SessionId, COMMAND), _apiId, "reused");
            AssertApiIdentity(Event(events, start.SessionId, COMMAND), _apiId, "reused");
            AssertApiIdentity(AssertLaunchRecord(sender, launch, "start_web"), _apiId, "reused");
            EngineTelemetryEvent[] engineEvents = events.OfType<EngineTelemetryEvent>().ToArray();
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, STOPPED }, engineEvents.Select(record => record.Name).ToArray());
            foreach (EngineTelemetryEvent record in engineEvents)
            {
                AssertEngineLink(record, launch, "start_web");
                AssertApiIdentity(record, _apiId, "reused");
            }

            CollectionAssert.AreEqual(new[] { _root, _root }, sender.CreatedPaths.ToArray());
            CollectionAssert.AreEqual(new[] { _root }, sender.LookedUpPaths.ToArray());
            Assert.AreEqual(0, sender.EngineIdentityPaths.Count);
            Assert.AreEqual(1, sender.FactoryCalls);
            AssertEnvelopes(events);
        }

        [DataTestMethod]
        [DataRow("bootstrap_default")]
        [DataRow("explicit")]
        [DataRow("canonical_equivalent")]
        public async Task SameRunEphemeralIdentitySurvivesObservationAndRootAcceptance(string acceptedRootKind)
        {
            using OfflineSender sender = new();
            EngineTelemetryIdentity ephemeral = new(_apiId, "ephemeral");
            using CliTelemetrySession cli = sender.CreateCli(createIdentity: _ => ephemeral);
            cli.ConfigurationCreated(_root);
            cli.ObserveConfiguration(_root);
            ProductTelemetryLaunchContext launch = BeginLaunch(cli, _root, CliTelemetryLaunchSource.StartStdio);
            Assert.AreSame(ephemeral, launch.ApiIdentity);
            using EngineTelemetrySession engine = sender.CreateEngine(launch, _root, "mcp_stdio");
            string? acceptedPath = acceptedRootKind switch
            {
                "bootstrap_default" => null,
                "explicit" => _root,
                _ => Path.Combine(Path.GetDirectoryName(_root)!, "uncreated", "..", Path.GetFileName(_root))
            };

            engine.MarkHostReady();
            engine.AcceptConfiguration(CreateConfig(), configPath: acceptedPath);
            Assert.IsTrue(engine.IsReady);
            engine.AcceptConfiguration(CreateConfig(), "hot_reload", _root);
            Complete(cli, "start");
            await DrainAsync(cli, engine);

            EngineTelemetryEvent[] engineEvents = sender.Events.OfType<EngineTelemetryEvent>().ToArray();
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, CONFIGURATION_CHANGED, STOPPED }, engineEvents.Select(record => record.Name).ToArray());
            CollectionAssert.AreEqual(new long[] { 0, 1, 2, 2 }, engineEvents.Select(record => record.ConfigurationEpoch).ToArray());
            foreach (IProductTelemetryEvent record in sender.Events)
            {
                AssertApiIdentity(record, _apiId, "ephemeral");
            }

            AssertLaunchRecord(sender, launch, "start_stdio");
            CollectionAssert.AreEqual(new[] { _root }, sender.CreatedPaths.ToArray());
            Assert.AreEqual(0, sender.LookedUpPaths.Count, "A same-run create, including an ephemeral result, wins over a later lookup.");
            Assert.AreEqual(0, sender.EngineIdentityPaths.Count, "Re-resolving an ephemeral handoff would break same-run correlation.");
            AssertEnvelopes(sender.Events);
        }

        [TestMethod]
        public async Task DifferentLaunchRootsKeepSeparateApiIdentitiesAndSuppressCommandIdentity()
        {
            using OfflineSender sender = new();
            using CliTelemetrySession cli = sender.CreateCli(createIdentity: path => path == _root
                ? new(_apiId, "ephemeral") : new(_otherApiId, "reused"));
            ProductTelemetryLaunchContext first = BeginLaunch(cli, _root, CliTelemetryLaunchSource.StartWeb);
            ProductTelemetryLaunchContext second = BeginLaunch(cli, _otherRoot, CliTelemetryLaunchSource.ExportGraphQL);
            using EngineTelemetrySession firstEngine = sender.CreateEngine(first, _root);
            using EngineTelemetrySession secondEngine = sender.CreateEngine(second, _otherRoot);
            firstEngine.AcceptConfiguration(CreateConfig(), configPath: _root);
            secondEngine.AcceptConfiguration(CreateConfig(), configPath: _otherRoot);
            firstEngine.MarkHostReady();
            secondEngine.MarkHostReady();
            Assert.IsTrue(firstEngine.IsReady);
            Assert.IsTrue(secondEngine.IsReady);
            Complete(cli, "export");
            await Task.WhenAll(cli.StopAsync(), firstEngine.StopAsync(), secondEngine.StopAsync()).WaitAsync(_timeout);

            IProductTelemetryEvent[] events = sender.Events;
            Assert.AreEqual(9, events.Length);
            Assert.AreNotEqual(first.EngineSessionId, second.EngineSessionId);
            AssertNoApiIdentity(Event(events, cli.SessionId, COMMAND));
            AssertApiIdentity(AssertLaunchRecord(sender, first, "start_web"), _apiId, "ephemeral");
            AssertApiIdentity(AssertLaunchRecord(sender, second, "export_graphql"), _otherApiId, "reused");
            foreach (EngineTelemetryEvent record in events.OfType<EngineTelemetryEvent>())
            {
                bool isFirst = record.SessionId == first.EngineSessionId;
                AssertEngineLink(record, isFirst ? first : second, isFirst ? "start_web" : "export_graphql");
                AssertApiIdentity(record, isFirst ? _apiId : _otherApiId, isFirst ? "ephemeral" : "reused");
            }

            CollectionAssert.AreEqual(new[] { _root, _otherRoot }, sender.CreatedPaths.ToArray());
            Assert.AreEqual(0, sender.EngineIdentityPaths.Count);
            AssertEnvelopes(events);
        }

        [DataTestMethod]
        [DataRow("newly_saved")]
        [DataRow("ephemeral")]
        public async Task DifferentAcceptedRootResolvesOwnIdentityWithoutRewritingBootstrapEvent(string handoffStability)
        {
            using OfflineSender sender = new();
            using CliTelemetrySession cli = sender.CreateCli(createIdentity: _ => new(_apiId, handoffStability));
            ProductTelemetryLaunchContext launch = BeginLaunch(cli, _root, CliTelemetryLaunchSource.StartWeb);
            using EngineTelemetrySession engine = sender.CreateEngine(launch, _root);
            EngineTelemetryEvent started = await sender.Exporter.FirstEngineStarted.Task.WaitAsync(_timeout);
            AssertApiIdentity(started, _apiId, handoffStability);
            Assert.AreEqual(0, sender.EngineIdentityPaths.Count);

            // The explicit bootstrap handoff was correct for its root. Accepting a different
            // root must resolve that root's identity, not rewrite the already-enqueued event.
            engine.MarkHostReady();
            engine.AcceptConfiguration(CreateConfig(), configPath: _otherRoot);
            Assert.IsTrue(engine.IsReady);
            engine.AcceptConfiguration(CreateConfig(), "hot_reload", _otherRoot);
            Complete(cli, "start");
            await DrainAsync(cli, engine);

            EngineTelemetryEvent[] engineEvents = sender.Events.OfType<EngineTelemetryEvent>().ToArray();
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, CONFIGURATION_CHANGED, STOPPED }, engineEvents.Select(record => record.Name).ToArray());
            Assert.AreSame(started, engineEvents[0]);
            AssertApiIdentity(engineEvents[0], _apiId, handoffStability);
            foreach (EngineTelemetryEvent record in engineEvents.Skip(1))
            {
                AssertEngineLink(record, launch, "start_web");
                AssertInstallation(record, _installationId, "reused");
                AssertApiIdentity(record, _otherApiId, "reused");
            }

            AssertApiIdentity(AssertLaunchRecord(sender, launch, "start_web"), _apiId, handoffStability);
            CollectionAssert.AreEqual(new[] { _otherRoot }, sender.EngineIdentityPaths.ToArray());
            AssertEnvelopes(sender.Events);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" \t ")]
        public async Task MissingBootstrapRootDoesNotSeedApiIdentity(string? bootstrapRoot)
        {
            using OfflineSender sender = new();
            using CliTelemetrySession cli = sender.CreateCli();
            ProductTelemetryLaunchContext launch = BeginLaunch(cli, _root, CliTelemetryLaunchSource.StartWeb);
            using EngineTelemetrySession engine = sender.CreateEngine(launch, bootstrapRoot);
            engine.AcceptConfiguration(CreateConfig(), configPath: _otherRoot);
            engine.MarkHostReady();
            Assert.IsTrue(engine.IsReady);
            Complete(cli, "start");
            await DrainAsync(cli, engine);

            EngineTelemetryEvent[] engineEvents = sender.Events.OfType<EngineTelemetryEvent>().ToArray();
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, STOPPED }, engineEvents.Select(record => record.Name).ToArray());
            AssertNoApiIdentity(engineEvents[0]);
            foreach (EngineTelemetryEvent record in engineEvents)
            {
                AssertEngineLink(record, launch, "start_web");
            }

            AssertApiIdentity(engineEvents[1], _otherApiId, "reused");
            AssertApiIdentity(engineEvents[2], _otherApiId, "reused");
            CollectionAssert.AreEqual(new[] { _otherRoot }, sender.EngineIdentityPaths.ToArray());
            AssertEnvelopes(sender.Events);
        }

        [DataTestMethod]
        [DataRow("web")]
        [DataRow("mcp_stdio")]
        [DataRow("embedded")]
        public async Task DirectEngineHasNoCliLinkageEvenWhileCliLaunchExists(string executionMode)
        {
            using OfflineSender sender = new();
            using CliTelemetrySession cli = sender.CreateCli();
            ProductTelemetryLaunchContext unrelated = BeginLaunch(cli, _root, CliTelemetryLaunchSource.StartWeb);
            using EngineTelemetrySession engine = sender.CreateEngine(launch: null, _otherRoot, executionMode);
            engine.AcceptConfiguration(CreateConfig(), configPath: _otherRoot);
            engine.MarkHostReady();
            Assert.IsTrue(engine.IsReady);
            Complete(cli, "start");
            await DrainAsync(cli, engine);

            EngineTelemetryEvent[] engineEvents = sender.Events.OfType<EngineTelemetryEvent>().ToArray();
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, STOPPED }, engineEvents.Select(record => record.Name).ToArray());
            Assert.AreNotEqual(cli.SessionId, engineEvents[0].SessionId);
            Assert.AreNotEqual(unrelated.EngineSessionId, engineEvents[0].SessionId);
            AssertNoApiIdentity(engineEvents[0]);
            foreach (EngineTelemetryEvent record in engineEvents)
            {
                AssertNoCliLink(record);
                Assert.AreEqual(executionMode, record.Properties["execution_mode"]);
            }

            AssertApiIdentity(engineEvents[1], _otherApiId, "reused");
            AssertApiIdentity(engineEvents[2], _otherApiId, "reused");
            CollectionAssert.AreEqual(new[] { _otherRoot }, sender.EngineIdentityPaths.ToArray());
            AssertEnvelopes(sender.Events);
        }

        [TestMethod]
        public async Task UnavailableInstallationIsOmittedAcrossTheHandoff()
        {
            using OfflineSender sender = new();
            using CliTelemetrySession cli = sender.CreateCli(installation: new(null, "unavailable"));
            ProductTelemetryLaunchContext launch = BeginLaunch(cli, _root, CliTelemetryLaunchSource.StartStdio);
            Assert.IsNull(launch.Installation.InstallationId);
            using EngineTelemetrySession engine = sender.CreateEngine(launch, _root, "mcp_stdio");
            engine.StartupFailed(TelemetryFailureStage.Parsing);
            Complete(cli, "start");
            await DrainAsync(cli, engine);

            Assert.AreEqual(5, sender.Events.Length);
            Assert.IsFalse(sender.Events.Any(record => record.Name == FIRST_RUN));
            AssertLaunchRecord(sender, launch, "start_stdio");
            foreach (IProductTelemetryEvent record in sender.Events)
            {
                AssertInstallation(record, null, "unavailable");
                AssertApiIdentity(record, _apiId, "ephemeral");
            }

            foreach (EngineTelemetryEvent record in sender.Events.OfType<EngineTelemetryEvent>())
            {
                AssertEngineLink(record, launch, "start_stdio");
            }

            Assert.AreEqual(0, sender.EngineIdentityPaths.Count);
            AssertEnvelopes(sender.Events);
        }

        [DataTestMethod]
        [DataRow("empty_engine")]
        [DataRow("empty_parent")]
        [DataRow("same_session")]
        [DataRow("negative_source")]
        [DataRow("unknown_source")]
        public async Task InvalidLaunchHeaderIsIgnoredInsteadOfBecomingEventProperties(string invalidField)
        {
            using OfflineSender sender = new();
            using CliTelemetrySession cli = sender.CreateCli();
            ProductTelemetryLaunchContext original = BeginLaunch(cli, _root, CliTelemetryLaunchSource.StartWeb);
            ProductTelemetryLaunchContext invalid = invalidField switch
            {
                "empty_engine" => original with { EngineSessionId = Guid.Empty },
                "empty_parent" => original with { ParentCliSessionId = Guid.Empty },
                "same_session" => original with { EngineSessionId = original.ParentCliSessionId },
                "negative_source" => original with { Source = (CliTelemetryLaunchSource)(-1) },
                _ => original with { Source = (CliTelemetryLaunchSource)int.MaxValue }
            };
            using EngineTelemetrySession engine = sender.CreateEngine(invalid, _root, executionMode: SENTINEL);
            Assert.IsTrue(engine.IsEnabled, "Invalid optional linkage must not prevent independent engine telemetry.");
            engine.AcceptConfiguration(CreateConfig(), configPath: _root);
            engine.MarkHostReady();
            Assert.IsTrue(engine.IsReady);
            Complete(cli, "start");
            await DrainAsync(cli, engine);

            EngineTelemetryEvent[] engineEvents = sender.Events.OfType<EngineTelemetryEvent>().ToArray();
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, STOPPED }, engineEvents.Select(record => record.Name).ToArray());
            Assert.AreNotEqual(original.EngineSessionId, engineEvents[0].SessionId);
            Assert.AreNotEqual(original.ParentCliSessionId, engineEvents[0].SessionId);
            AssertNoApiIdentity(engineEvents[0]);
            foreach (EngineTelemetryEvent record in engineEvents)
            {
                AssertNoCliLink(record);
                Assert.AreEqual("unknown", record.Properties["execution_mode"]);
            }

            AssertApiIdentity(engineEvents[1], _otherApiId, "reused");
            AssertApiIdentity(engineEvents[2], _otherApiId, "reused");
            CollectionAssert.AreEqual(new[] { _root }, sender.EngineIdentityPaths.ToArray());
            AssertEnvelopes(sender.Events);
        }

        [DataTestMethod]
        [DataRow("empty", "reused")]
        [DataRow("missing", "newly_saved")]
        [DataRow("valid", "ephemeral")]
        [DataRow("valid", "unavailable")]
        [DataRow("valid", SENTINEL)]
        public async Task InvalidInstallationEvidenceCannotLeakUnreviewedStability(string idKind, string stability)
        {
            using OfflineSender sender = new();
            using CliTelemetrySession cli = sender.CreateCli();
            ProductTelemetryLaunchContext original = BeginLaunch(cli, _root, CliTelemetryLaunchSource.StartWeb);
            Guid? installationId = idKind switch { "missing" => null, "empty" => Guid.Empty, _ => _installationId };
            ProductTelemetryLaunchContext launch = original with { Installation = new(installationId, stability) };
            using EngineTelemetrySession engine = sender.CreateEngine(launch, _root);
            engine.StartupFailed(TelemetryFailureStage.Configuration);
            Complete(cli, "start");
            await DrainAsync(cli, engine);

            EngineTelemetryEvent[] engineEvents = sender.Events.OfType<EngineTelemetryEvent>().ToArray();
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, STARTUP_FAILED, STOPPED }, engineEvents.Select(record => record.Name).ToArray());
            foreach (EngineTelemetryEvent record in engineEvents)
            {
                AssertEngineLink(record, launch, "start_web");
                AssertInstallation(record, null, "unavailable");
                AssertApiIdentity(record, _apiId, "ephemeral");
            }

            Assert.AreEqual(0, sender.EngineIdentityPaths.Count);
            AssertEnvelopes(sender.Events);
        }

        [DataTestMethod]
        [DataRow("missing")]
        [DataRow("empty_id")]
        [DataRow("unknown_stability")]
        public async Task MissingOrInvalidApiIdentityIsResolvedForAcceptedRoot(string invalidKind)
        {
            using OfflineSender sender = new();
            using CliTelemetrySession cli = sender.CreateCli();
            ProductTelemetryLaunchContext original = BeginLaunch(cli, _root, CliTelemetryLaunchSource.StartWeb);
            ProductTelemetryLaunchContext launch = original with
            {
                ApiIdentity = invalidKind switch
                {
                    "missing" => null,
                    "empty_id" => new(Guid.Empty, "reused"),
                    _ => new(_apiId, SENTINEL)
                }
            };
            using EngineTelemetrySession engine = sender.CreateEngine(launch, _root);
            engine.AcceptConfiguration(CreateConfig(), configPath: _root);
            engine.MarkHostReady();
            Assert.IsTrue(engine.IsReady);
            Complete(cli, "start");
            await DrainAsync(cli, engine);

            EngineTelemetryEvent[] engineEvents = sender.Events.OfType<EngineTelemetryEvent>().ToArray();
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, STOPPED }, engineEvents.Select(record => record.Name).ToArray());
            AssertNoApiIdentity(engineEvents[0]);
            foreach (EngineTelemetryEvent record in engineEvents)
            {
                AssertEngineLink(record, launch, "start_web");
                AssertInstallation(record, _installationId, "reused");
            }

            AssertApiIdentity(engineEvents[1], _otherApiId, "reused");
            AssertApiIdentity(engineEvents[2], _otherApiId, "reused");
            CollectionAssert.AreEqual(new[] { _root }, sender.EngineIdentityPaths.ToArray());
            AssertEnvelopes(sender.Events);
        }

        [TestMethod]
        public async Task MalformedRequiredContextFailsClosedBeforeSenderCreation()
        {
            using OfflineSender sender = new();
            using CliTelemetrySession cli = sender.CreateCli();
            ProductTelemetryLaunchContext original = BeginLaunch(cli, _root, CliTelemetryLaunchSource.StartWeb);
            using EngineTelemetrySession engine = sender.CreateEngine(original with { Installation = null! }, _root);
            Assert.IsFalse(engine.IsEnabled);
            engine.StartupFailed(TelemetryFailureStage.Configuration);
            engine.AcceptConfiguration(CreateConfig(), configPath: _root);
            engine.MarkHostReady();
            Complete(cli, "start");
            await DrainAsync(cli, engine);

            Assert.IsFalse(engine.IsReady);
            Assert.AreEqual(0, sender.EngineSenderAcquisitions);
            Assert.AreEqual(0, sender.EngineIdentityPaths.Count);
            Assert.AreEqual(0, sender.Events.OfType<EngineTelemetryEvent>().Count());
            CollectionAssert.AreEqual(new[] { LAUNCH, COMMAND }, sender.Events.Select(record => record.Name).ToArray());
            AssertEnvelopes(sender.Events);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task OptOutIgnoresValidAndMalformedLaunchContextsBeforeDependencies(bool malformed)
        {
            using OfflineSender sender = new();
            ConcurrentQueue<string> cliEnvironmentReads = new();
            ConcurrentQueue<string> engineEnvironmentReads = new();
            int notices = 0;
            int installations = 0;
            int identities = 0;
            ThrowingTimeProvider clock = new();
            ProductTelemetryLaunchContext launch = new(Guid.NewGuid(), Guid.NewGuid(), new(_installationId, "reused"),
                new(_apiId, "ephemeral"), CliTelemetryLaunchSource.StartWeb);
            if (malformed)
            {
                launch = launch with { Installation = null!, Source = (CliTelemetryLaunchSource)int.MaxValue };
            }

            using CliTelemetrySession cli = CliTelemetrySession.Create(sender.AcquireCliSender,
                enableSyntheticCollection: true, clock: clock,
                readEnvironmentVariable: name => { cliEnvironmentReads.Enqueue(name); return "true"; },
                showNotice: () => Interlocked.Increment(ref notices),
                resolveInstallation: () => { Interlocked.Increment(ref installations); return new(_installationId, "newly_saved"); },
                createIdentity: _ => { Interlocked.Increment(ref identities); return new(_apiId, "ephemeral"); },
                lookupIdentity: _ => { Interlocked.Increment(ref identities); return new(_apiId, "reused"); });
            using EngineTelemetrySession engine = EngineTelemetrySession.Create(sender.AcquireEngineSender,
                enableSyntheticCollection: true, configPath: _root, clock: clock,
                readEnvironmentVariable: name => { engineEnvironmentReads.Enqueue(name); return "true"; },
                showNotice: () => Interlocked.Increment(ref notices),
                resolveIdentity: _ => { Interlocked.Increment(ref identities); return new(_otherApiId, "reused"); },
                startTimer: true, launchContext: launch);

            cli.ConfigurationCreated(_root);
            cli.ObserveConfiguration(_root);
            Assert.IsNull(cli.BeginEngineLaunch(_root, CliTelemetryLaunchSource.StartWeb));
            Complete(cli, "start");
            engine.AcceptConfiguration(CreateConfig(), configPath: _root);
            engine.MarkHostReady();
            engine.StartupFailed(TelemetryFailureStage.Configuration);
            engine.Tick();
            await DrainAsync(cli, engine);

            Assert.IsFalse(cli.IsEnabled);
            Assert.AreEqual(Guid.Empty, cli.SessionId);
            Assert.IsFalse(engine.IsEnabled);
            Assert.IsFalse(engine.IsReady);
            Assert.IsNull(engine.HealthProbeToken);
            CollectionAssert.AreEqual(new[] { ProductTelemetryPolicy.OPT_OUT_ENV_VAR }, cliEnvironmentReads.ToArray());
            CollectionAssert.AreEqual(new[] { ProductTelemetryPolicy.OPT_OUT_ENV_VAR }, engineEnvironmentReads.ToArray());
            Assert.AreEqual(0, notices);
            Assert.AreEqual(0, installations);
            Assert.AreEqual(0, identities);
            Assert.AreEqual(0, clock.Calls, "A swallowed dependency exception is not proof of an early opt-out gate.");
            Assert.AreEqual(0, sender.CliSenderAcquisitions);
            Assert.AreEqual(0, sender.EngineSenderAcquisitions);
            Assert.AreEqual(0, sender.FactoryCalls);
            Assert.AreEqual(0, sender.Events.Length);
        }

        [TestMethod]
        public async Task ConcurrentLaunchesDoNotLeakBetweenCliAndEngineSessions()
        {
            const int runs = 6;
            using OfflineSender sender = new();
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource allEnginesCreated = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int enginesCreated = 0;
            Task<(ProductTelemetryLaunchContext Launch, string Root, bool Failed)>[] tasks = Enumerable.Range(0, runs)
                .Select(index => Task.Run(async () =>
                {
                    await release.Task.WaitAsync(_timeout);
                    string root = CanonicalRoot("concurrent-" + index.ToString(CultureInfo.InvariantCulture) + ".json");
                    CliTelemetryInstallation installation = new(Guid.NewGuid(), "reused");
                    EngineTelemetryIdentity identity = new(Guid.NewGuid(), "ephemeral");
                    CliTelemetryLaunchSource source = (CliTelemetryLaunchSource)(index % 3);
                    using CliTelemetrySession cli = sender.CreateCli(installation, _ => identity);
                    ProductTelemetryLaunchContext launch = BeginLaunch(cli, root, source);
                    using EngineTelemetrySession engine = sender.CreateEngine(launch, root,
                        source == CliTelemetryLaunchSource.StartStdio ? "mcp_stdio" : "web");
                    if (Interlocked.Increment(ref enginesCreated) == runs)
                    {
                        allEnginesCreated.TrySetResult();
                    }

                    // All pairs coexist before any failure, configuration acceptance or stop.
                    await allEnginesCreated.Task.WaitAsync(_timeout);
                    bool failed = index % 2 == 0;
                    if (failed)
                    {
                        engine.StartupFailed(TelemetryFailureStage.Parsing);
                    }
                    else
                    {
                        engine.AcceptConfiguration(CreateConfig(), configPath: root);
                        engine.MarkHostReady();
                        Assert.IsTrue(engine.IsReady);
                    }

                    cli.Complete(source == CliTelemetryLaunchSource.ExportGraphQL ? "export" : "start", "none", _noOptions,
                        failed ? CliTelemetryOutcome.ExecutionFailure : CliTelemetryOutcome.Success,
                        failed ? CliTelemetryFailureCategory.Initialization : CliTelemetryFailureCategory.None);
                    await DrainAsync(cli, engine);
                    return (Launch: launch, Root: root, Failed: failed);
                })).ToArray();
            release.TrySetResult();
            (ProductTelemetryLaunchContext Launch, string Root, bool Failed)[] results = await Task.WhenAll(tasks).WaitAsync(_timeout);

            IProductTelemetryEvent[] events = sender.Events;
            Assert.AreEqual(runs * 5, events.Length);
            Assert.AreEqual(runs * 2, events.Select(record => record.SessionId).Distinct().Count());
            foreach ((ProductTelemetryLaunchContext launch, string _, bool failed) in results)
            {
                string source = SourceName(launch.Source);
                AssertLaunchRecord(sender, launch, source);
                CliTelemetryEvent[] cliEvents = events.OfType<CliTelemetryEvent>()
                    .Where(record => record.SessionId == launch.ParentCliSessionId).ToArray();
                CollectionAssert.AreEqual(new[] { LAUNCH, COMMAND }, cliEvents.Select(record => record.Name).ToArray());
                Assert.AreEqual(failed ? "execution_failure" : "success", cliEvents[1].Properties["outcome"]);
                Assert.AreEqual(launch.Source == CliTelemetryLaunchSource.ExportGraphQL ? "export" : "start", cliEvents[1].Properties["command"]);
                EngineTelemetryEvent[] engineEvents = events.OfType<EngineTelemetryEvent>()
                    .Where(record => record.SessionId == launch.EngineSessionId).ToArray();
                CollectionAssert.AreEqual(new[] { PROCESS_STARTED, failed ? STARTUP_FAILED : READY, STOPPED },
                    engineEvents.Select(record => record.Name).ToArray());
                CollectionAssert.AreEqual(new long[] { 0, failed ? 0 : 1, failed ? 0 : 1 },
                    engineEvents.Select(record => record.ConfigurationEpoch).ToArray());
                IProductTelemetryEvent[] pair = events.Where(record => record.SessionId == launch.ParentCliSessionId
                    || record.SessionId == launch.EngineSessionId).ToArray();
                foreach (IProductTelemetryEvent record in pair)
                {
                    AssertInstallation(record, launch.Installation.InstallationId, "reused");
                    AssertApiIdentity(record, launch.ApiIdentity!.ApiId, "ephemeral");
                }

                foreach (EngineTelemetryEvent record in engineEvents)
                {
                    AssertEngineLink(record, launch, source);
                }
            }

            CollectionAssert.AreEquivalent(results.Select(result => result.Root).ToArray(), sender.CreatedPaths.ToArray());
            Assert.AreEqual(0, sender.EngineIdentityPaths.Count);
            Assert.AreEqual(runs, sender.CliSenderAcquisitions);
            Assert.AreEqual(runs, sender.EngineSenderAcquisitions);
            Assert.AreEqual(1, sender.FactoryCalls);
            AssertEnvelopes(events);
        }

        [TestMethod]
        public async Task ClosingCliDoesNotStopOrDiscardEngineEvents()
        {
            using OfflineSender sender = new();
            using CliTelemetrySession cli = sender.CreateCli();
            ProductTelemetryLaunchContext launch = BeginLaunch(cli, _root, CliTelemetryLaunchSource.StartWeb);
            using EngineTelemetrySession engine = sender.CreateEngine(launch, _root);
            await sender.Exporter.FirstEngineStarted.Task.WaitAsync(_timeout);
            Complete(cli, "start");
            await cli.StopAsync().WaitAsync(_timeout);
            cli.Dispose();

            Assert.IsTrue(engine.IsEnabled);
            Assert.AreEqual(0, sender.Exporter.DisposeCalls, "Releasing the CLI lease must not dispose the engine's shared sender.");
            Assert.AreEqual(2, sender.Events.OfType<CliTelemetryEvent>().Count());
            engine.AcceptConfiguration(CreateConfig(), configPath: _root);
            engine.MarkHostReady();
            Assert.IsTrue(engine.IsReady);
            using (EngineTelemetryRequestScope request = engine.BeginRequest(EngineTelemetryApi.Rest,
                EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous))
            {
                // Exercise the session's lifecycle after CLI disposal, not an HTTP/database call.
                request.Complete(EngineTelemetryOutcome.Success, 200);
            }

            await engine.StopAsync().WaitAsync(_timeout);
            EngineTelemetryEvent[] engineEvents = sender.Events.OfType<EngineTelemetryEvent>().ToArray();
            CollectionAssert.AreEqual(new[]
            {
                PROCESS_STARTED, READY, "dab.engine.first_request_served", "dab.engine.first_successful_request",
                "dab.engine.usage_summary", "dab.engine.usage_summary", STOPPED
            }, engineEvents.Select(record => record.Name).ToArray());
            foreach (EngineTelemetryEvent record in engineEvents)
            {
                AssertEngineLink(record, launch, "start_web");
                AssertApiIdentity(record, _apiId, "ephemeral");
            }

            Assert.AreEqual(2, sender.Events.OfType<CliTelemetryEvent>().Count(), "Engine shutdown must not manufacture another CLI event.");
            Assert.AreEqual(1, sender.FactoryCalls);
            Assert.AreEqual(0, sender.EngineIdentityPaths.Count);
            Assert.AreEqual(0, sender.Exporter.DisposeCalls);
            sender.Dispose();
            Assert.AreEqual(1, sender.Exporter.DisposeCalls, "The isolated pool, not either session, owns final exporter disposal.");
            AssertEnvelopes(sender.Events);
        }

        [DataTestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        [DoNotParallelize]
        public async Task StartEngineCorePreservesLaunchIdsThroughItsRealFailureCatch(bool enabled, bool stdio)
        {
            using OfflineSender sender = new();
            using CliTelemetrySession cli = sender.CreateCli();
            CliTelemetryLaunchSource source = stdio ? CliTelemetryLaunchSource.StartStdio : CliTelemetryLaunchSource.StartWeb;
            ProductTelemetryLaunchContext launch = BeginLaunch(cli, _root, source);
            using EngineTelemetrySession engine = sender.CreateEngine(launch, _root, stdio ? "mcp_stdio" : "web", enabled);
            TextWriter originalError = Console.Error;
            using StringWriter errors = new(CultureInfo.InvariantCulture);
            try
            {
                Console.SetError(errors);
                // The real command-line configuration validation throws before log-level
                // globals are changed, stdout is redirected, a host is built or config is read.
                // Deliberately do not call StartEngine/CreateStandalone: those choose an
                // environment-based destination instead of accepting this offline session.
                Assert.IsFalse(Program.StartEngineCore(["--ConfigFileName", _root, "--log-level", "7"],
                    runMcpStdio: stdio, mcpRole: null, productTelemetry: engine, validateUrls: false));
            }
            finally
            {
                Console.SetError(originalError);
            }

            Assert.IsTrue(errors.ToString().Contains("Unable to launch the runtime due to:", StringComparison.Ordinal));
            Assert.IsTrue(errors.ToString().Contains("LogLevel's valid range is 0 to 6", StringComparison.Ordinal),
                "Enabled and disabled telemetry must preserve the existing failure catch and diagnostic.");
            Assert.IsFalse(engine.IsEnabled, "StartEngineCore's finally must close the injected session.");
            Assert.IsFalse(engine.IsReady);
            Assert.IsTrue(cli.IsEnabled, "The engine entry point does not own the CLI session.");
            cli.Complete("start", "none", _noOptions, CliTelemetryOutcome.ExecutionFailure, CliTelemetryFailureCategory.Initialization);
            await cli.StopAsync().WaitAsync(_timeout);

            EngineTelemetryEvent[] engineEvents = sender.Events.OfType<EngineTelemetryEvent>().ToArray();
            Assert.AreEqual(enabled ? 1 : 0, sender.EngineSenderAcquisitions);
            Assert.AreEqual(0, sender.EngineIdentityPaths.Count);
            if (enabled)
            {
                CollectionAssert.AreEqual(new[] { PROCESS_STARTED, STARTUP_FAILED, STOPPED }, engineEvents.Select(record => record.Name).ToArray());
                foreach (EngineTelemetryEvent record in engineEvents)
                {
                    AssertEngineLink(record, launch, SourceName(source));
                    AssertInstallation(record, _installationId, "reused");
                    AssertApiIdentity(record, _apiId, "ephemeral");
                    Assert.AreEqual(0L, record.ConfigurationEpoch);
                }

                Assert.AreEqual("initialization", engineEvents[1].Properties["failure_stage"]);
                Assert.AreEqual("initialization", engineEvents[1].Properties["failure_category"]);
            }
            else
            {
                Assert.AreEqual(0, engineEvents.Length);
            }

            AssertLaunchRecord(sender, launch, SourceName(source));
            AssertEnvelopes(sender.Events);
        }

        [TestMethod]
        [DoNotParallelize]
        public async Task StartEngineCoreConfigurationPreflightPreservesLaunchIds()
        {
            using OfflineSender sender = new();
            using CliTelemetrySession cli = sender.CreateCli();
            ProductTelemetryLaunchContext launch = BeginLaunch(cli, _root, CliTelemetryLaunchSource.StartWeb);
            using EngineTelemetrySession engine = sender.CreateEngine(launch, _root);
            string? originalUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
            TextWriter originalError = Console.Error;
            using StringWriter errors = new(CultureInfo.InvariantCulture);
            try
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "not-a-url");
                Console.SetError(errors);
                Assert.IsFalse(Program.StartEngineCore(["--ConfigFileName", _root], runMcpStdio: false,
                    mcpRole: null, productTelemetry: engine, validateUrls: true));
            }
            finally
            {
                Console.SetError(originalError);
                Environment.SetEnvironmentVariable("ASPNETCORE_URLS", originalUrls);
            }

            Assert.IsTrue(errors.ToString().Contains("Invalid ASPNETCORE_URLS format", StringComparison.Ordinal));
            Assert.IsFalse(engine.IsEnabled);
            cli.Complete("start", "none", _noOptions, CliTelemetryOutcome.ExecutionFailure, CliTelemetryFailureCategory.Configuration);
            await cli.StopAsync().WaitAsync(_timeout);

            EngineTelemetryEvent[] engineEvents = sender.Events.OfType<EngineTelemetryEvent>().ToArray();
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, STARTUP_FAILED, STOPPED }, engineEvents.Select(record => record.Name).ToArray());
            foreach (EngineTelemetryEvent record in engineEvents)
            {
                AssertEngineLink(record, launch, "start_web");
                AssertInstallation(record, _installationId, "reused");
                AssertApiIdentity(record, _apiId, "ephemeral");
                Assert.AreEqual(0L, record.ConfigurationEpoch);
            }

            Assert.AreEqual("configuration", engineEvents[1].Properties["failure_stage"]);
            Assert.AreEqual("initialization", engineEvents[1].Properties["failure_category"]);
            Assert.AreEqual(0, sender.EngineIdentityPaths.Count);
            AssertLaunchRecord(sender, launch, "start_web");
            AssertEnvelopes(sender.Events);
        }

        private static string CanonicalRoot(string fileName)
            => Path.GetFullPath(Path.Combine(Path.GetTempPath(), SENTINEL, fileName));

        private static RuntimeConfig CreateConfig() => new(Schema: SENTINEL,
            DataSource: new(DatabaseType.MSSQL, SENTINEL), Entities: new(new Dictionary<string, Entity>()));

        private static ProductTelemetryLaunchContext BeginLaunch(CliTelemetrySession cli, string root, CliTelemetryLaunchSource source)
        {
            ProductTelemetryLaunchContext? launch = cli.BeginEngineLaunch(root, source);
            Assert.IsNotNull(launch);
            Assert.AreNotEqual(Guid.Empty, launch.EngineSessionId);
            Assert.AreNotEqual(cli.SessionId, launch.EngineSessionId);
            Assert.AreEqual(cli.SessionId, launch.ParentCliSessionId);
            return launch;
        }

        private static void Complete(CliTelemetrySession cli, string command)
            => cli.Complete(command, "none", _noOptions, CliTelemetryOutcome.Success);

        private static async Task DrainAsync(CliTelemetrySession cli, EngineTelemetrySession engine)
            => await Task.WhenAll(cli.StopAsync(), engine.StopAsync()).WaitAsync(_timeout);

        private static string SourceName(CliTelemetryLaunchSource source) => source switch
        {
            CliTelemetryLaunchSource.StartWeb => "start_web",
            CliTelemetryLaunchSource.StartStdio => "start_stdio",
            CliTelemetryLaunchSource.ExportGraphQL => "export_graphql",
            _ => throw new ArgumentOutOfRangeException(nameof(source))
        };

        private static IProductTelemetryEvent Event(IEnumerable<IProductTelemetryEvent> events, Guid sessionId, string name)
            => events.Single(record => record.SessionId == sessionId && record.Name == name);

        private static CliTelemetryEvent AssertLaunchRecord(OfflineSender sender, ProductTelemetryLaunchContext launch, string source)
        {
            CliTelemetryEvent record = sender.Events.OfType<CliTelemetryEvent>().Single(record => record.Name == LAUNCH
                && record.SessionId == launch.ParentCliSessionId
                && record.Properties["dab_launched_engine_session_id"] == launch.EngineSessionId.ToString("D"));
            Assert.AreEqual(launch.ParentCliSessionId.ToString("D"), record.Properties["dab_parent_cli_session_id"]);
            Assert.AreEqual(source, record.Properties["launch_source"]);
            return record;
        }

        private static void AssertEngineLink(EngineTelemetryEvent record, ProductTelemetryLaunchContext launch, string source)
        {
            Assert.AreEqual(launch.EngineSessionId, record.SessionId);
            Assert.AreEqual(launch.ParentCliSessionId.ToString("D"), record.Properties["dab_parent_cli_session_id"]);
            Assert.AreEqual("cli", record.Properties["launcher"]);
            Assert.AreEqual(source, record.Properties["launch_source"]);
            Assert.IsFalse(record.Properties.ContainsKey("dab_launched_engine_session_id"), "That property belongs to the CLI launch record, not engine context.");
        }

        private static void AssertNoCliLink(IProductTelemetryEvent record)
        {
            foreach (string key in new[]
            {
                "dab_parent_cli_session_id", "dab_launched_engine_session_id", "launch_source",
                "dab_installation_id", "dab_installation_id_stability"
            })
            {
                Assert.IsFalse(record.Properties.ContainsKey(key), key);
            }
        }

        private static void AssertApiIdentity(IProductTelemetryEvent record, Guid apiId, string stability)
        {
            Assert.AreEqual(apiId.ToString("D"), record.Properties["dab_api_id"]);
            Assert.AreEqual(stability, record.Properties["dab_api_id_stability"]);
        }

        private static void AssertNoApiIdentity(IProductTelemetryEvent record)
        {
            Assert.IsFalse(record.Properties.ContainsKey("dab_api_id"));
            Assert.IsFalse(record.Properties.ContainsKey("dab_api_id_stability"));
        }

        private static void AssertInstallation(IProductTelemetryEvent record, Guid? installationId, string stability)
        {
            Assert.AreEqual(stability, record.Properties["dab_installation_id_stability"]);
            Assert.AreEqual(installationId.HasValue, record.Properties.ContainsKey("dab_installation_id"));
            if (installationId is Guid id)
            {
                Assert.AreEqual(id.ToString("D"), record.Properties["dab_installation_id"]);
            }
        }

        private static void AssertEnvelopes(IProductTelemetryEvent[] events)
        {
            Assert.IsTrue(events.Length > 0);
            Assert.AreEqual(events.Length, events.Select(record => record.EventId).Distinct().Count());
            foreach (IGrouping<Guid, IProductTelemetryEvent> session in events.GroupBy(record => record.SessionId))
            {
                Assert.AreNotEqual(Guid.Empty, session.Key);
                CollectionAssert.AreEqual(Enumerable.Range(1, session.Count()).Select(index => (long)index).ToArray(),
                    session.Select(record => record.Sequence).ToArray(), "Ordering is per session, not across independent delivery workers.");
                Assert.AreEqual(1, session.Select(record => record.GetType()).Distinct().Count(), "CLI and engine envelopes must never share a session.");
            }

            foreach (IProductTelemetryEvent record in events)
            {
                Assert.AreNotEqual(Guid.Empty, record.EventId);
                Assert.IsTrue(record.IsSynthetic);
                Assert.AreEqual(TimeSpan.Zero, record.OccurredAt.Offset);
                Assert.IsFalse(record.Properties.ContainsKey("dab_config_epoch"), "Epoch is engine envelope metadata, not a producer property.");
                Assert.IsFalse(record.Properties.Keys.Any(key => key.Contains(SENTINEL, StringComparison.Ordinal)));
                Assert.IsFalse(record.Properties.Values.Any(value => value.Contains(SENTINEL, StringComparison.Ordinal)),
                    "Paths, configuration values and invalid category text must not escape into telemetry.");
            }
        }

        /// <summary>
        /// Uses the real pool's lease ownership, but only an injected generic in-memory exporter.
        /// The synthetic destination is a pool key; no default pool or environment factory is used.
        /// </summary>
        private sealed class OfflineSender : IDisposable
        {
            private readonly ProductTelemetrySenderPool _pool;
            private readonly ApplicationInsightsTelemetryDestination _destination;
            private int _factoryCalls;
            private int _cliSenderAcquisitions;
            private int _engineSenderAcquisitions;

            internal OfflineSender()
            {
                Assert.IsTrue(ApplicationInsightsTelemetryDestination.TryParse(
                    "InstrumentationKey=01234567-89ab-cdef-0123-456789abcdef;IngestionEndpoint=https://cli-launch.synthetic.invalid/",
                    out ApplicationInsightsTelemetryDestination? destination));
                _destination = destination;
                _pool = new(_ =>
                {
                    Interlocked.Increment(ref _factoryCalls);
                    return Exporter;
                });
            }

            internal CapturingExporter Exporter { get; } = new();
            internal IProductTelemetryEvent[] Events => Exporter.Records.ToArray();
            internal ConcurrentQueue<string?> CreatedPaths { get; } = new();
            internal ConcurrentQueue<string?> LookedUpPaths { get; } = new();
            internal ConcurrentQueue<string?> EngineIdentityPaths { get; } = new();
            internal int FactoryCalls => Volatile.Read(ref _factoryCalls);
            internal int CliSenderAcquisitions => Volatile.Read(ref _cliSenderAcquisitions);
            internal int EngineSenderAcquisitions => Volatile.Read(ref _engineSenderAcquisitions);

            internal IProductTelemetryExporter<IProductTelemetryEvent> AcquireCliSender()
            {
                Interlocked.Increment(ref _cliSenderAcquisitions);
                return _pool.AcquireLease(_destination);
            }

            internal IEngineTelemetryExporter AcquireEngineSender()
            {
                Interlocked.Increment(ref _engineSenderAcquisitions);
                return new EngineExporterAdapter(_pool.AcquireLease(_destination));
            }

            internal CliTelemetrySession CreateCli(CliTelemetryInstallation? installation = null,
                Func<string?, EngineTelemetryIdentity>? createIdentity = null,
                Func<string?, EngineTelemetryIdentity?>? lookupIdentity = null)
                => CliTelemetrySession.Create(AcquireCliSender, enableSyntheticCollection: true,
                    readEnvironmentVariable: _ => null, showNotice: () => { },
                    resolveInstallation: () => installation ?? new(_installationId, "reused"),
                    createIdentity: path =>
                    {
                        CreatedPaths.Enqueue(path);
                        return createIdentity is null ? new(_apiId, "ephemeral") : createIdentity(path);
                    },
                    lookupIdentity: path =>
                    {
                        LookedUpPaths.Enqueue(path);
                        return lookupIdentity?.Invoke(path);
                    });

            internal EngineTelemetrySession CreateEngine(ProductTelemetryLaunchContext? launch, string? configPath,
                string executionMode = "web", bool enabled = true)
                => EngineTelemetrySession.Create(AcquireEngineSender, enableSyntheticCollection: enabled,
                    configPath: configPath, executionMode: executionMode,
                    readEnvironmentVariable: _ => null, showNotice: () => { },
                    resolveIdentity: path =>
                    {
                        EngineIdentityPaths.Enqueue(path);
                        return new(_otherApiId, "reused");
                    },
                    startTimer: false, launchContext: launch);

            public void Dispose() => _pool.Dispose();
        }

        // Mirrors the hosting adapter while forwarding the original immutable engine envelope
        // to the same generic sender used by the CLI; it owns only its independently acquired lease.
        private sealed class EngineExporterAdapter(IProductTelemetryExporter<IProductTelemetryEvent> lease) : IEngineTelemetryExporter
        {
            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
                => lease.ExportAsync(record, cancellationToken);

            public void Dispose() => lease.Dispose();
        }

        private sealed class CapturingExporter : IProductTelemetryExporter<IProductTelemetryEvent>
        {
            private int _disposeCalls;
            internal ConcurrentQueue<IProductTelemetryEvent> Records { get; } = new();
            internal TaskCompletionSource<EngineTelemetryEvent> FirstEngineStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal int DisposeCalls => Volatile.Read(ref _disposeCalls);

            public ValueTask<bool> ExportAsync(IProductTelemetryEvent record, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ObjectDisposedException.ThrowIf(DisposeCalls != 0, this);
                Records.Enqueue(record);
                if (record is EngineTelemetryEvent engine && engine.Name == PROCESS_STARTED)
                {
                    FirstEngineStarted.TrySetResult(engine);
                }

                return ValueTask.FromResult(true);
            }

            public void Dispose() => Interlocked.Increment(ref _disposeCalls);
        }

        private sealed class ThrowingTimeProvider : TimeProvider
        {
            private int _calls;
            internal int Calls => Volatile.Read(ref _calls);
            public override long TimestampFrequency => Fail<long>();
            public override long GetTimestamp() => Fail<long>();
            public override DateTimeOffset GetUtcNow() => Fail<DateTimeOffset>();
            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => Fail<ITimer>();

            private T Fail<T>()
            {
                Interlocked.Increment(ref _calls);
                throw new InvalidOperationException("Disabled telemetry must not consult the clock.");
            }
        }
    }
}
