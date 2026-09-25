// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Exercises the real Core session and scope APIs with in-memory configurations and injected
    /// exporter, clock, notice, environment reader and identity resolver. No host or protocol client
    /// is started. Protocol adapters and immutable delivery retries have separate test suites.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    public class EngineTelemetrySessionTests
    {
        private const string SENTINEL = "SESSION_PRIVATE_SENTINEL_73ef9";
        private const string ENTITY = SENTINEL + "_entity";
        private const string PROCESS_STARTED = "dab.engine.process_started";
        private const string READY = "dab.engine.ready";
        private const string CONFIGURATION_CHANGED = "dab.engine.configuration_changed";
        private const string FIRST_SERVED = "dab.engine.first_request_served";
        private const string FIRST_SUCCESS = "dab.engine.first_successful_request";
        private const string SUMMARY = "dab.engine.usage_summary";
        private const string STOPPED = "dab.engine.stopped";

        private static readonly DateTimeOffset _start = new(2026, 9, 21, 12, 0, 0, TimeSpan.Zero);
        private static readonly TimeSpan _testTimeout = TimeSpan.FromSeconds(10);
        private static readonly Guid _apiId = new("f6bb4a71-742a-445b-bca0-d4aa56659c28");

        [DataTestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        public async Task DisabledGateNeverInvokesEnvironmentFactoryNoticeIdentityOrClock(bool synthetic, bool hasFactory)
        {
            int environmentCalls = 0;
            int factoryCalls = 0;
            int noticeCalls = 0;
            int identityCalls = 0;
            ThrowingTimeProvider clock = new();
            CapturingExporter exporter = new();
            Func<IEngineTelemetryExporter>? factory = hasFactory ? () =>
            {
                Interlocked.Increment(ref factoryCalls);
                return exporter;
            }
            : null;
            using EngineTelemetrySession session = EngineTelemetrySession.Create(
                exporterFactory: factory,
                enableSyntheticCollection: synthetic,
                clock: clock,
                readEnvironmentVariable: _ => { environmentCalls++; return null; },
                showNotice: () => noticeCalls++,
                resolveIdentity: _ => { identityCalls++; return new(_apiId, "ephemeral"); },
                startTimer: true);

            await ExerciseDisabledSessionAsync(session);

            Assert.AreEqual(0, environmentCalls);
            Assert.AreEqual(0, factoryCalls);
            Assert.AreEqual(0, noticeCalls);
            Assert.AreEqual(0, identityCalls);
            Assert.AreEqual(0, clock.Calls, "A swallowed clock exception is still an unexpected dependency call.");
            Assert.AreEqual(0, exporter.Attempts.Count);
        }

        [DataTestMethod]
        [DataRow("1")]
        [DataRow("true")]
        [DataRow("TRUE")]
        [DataRow(" 1 ")]
        [DataRow(" true ")]
        public async Task OptOutVetoPrecedesFactoryNoticeIdentityAndEveryClockSurface(string optOut)
        {
            ConcurrentQueue<string> environmentReads = new();
            int factoryCalls = 0;
            int noticeCalls = 0;
            int identityCalls = 0;
            ThrowingTimeProvider clock = new();
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(
                exporterFactory: () => { Interlocked.Increment(ref factoryCalls); return exporter; },
                enableSyntheticCollection: true,
                configPath: SENTINEL,
                clock: clock,
                readEnvironmentVariable: name => { environmentReads.Enqueue(name); return optOut; },
                showNotice: () => noticeCalls++,
                resolveIdentity: _ => { identityCalls++; return new(_apiId, "ephemeral"); },
                startTimer: true);

            await ExerciseDisabledSessionAsync(session);

            CollectionAssert.AreEqual(new[] { "DAB_TELEMETRY_OPT_OUT" }, environmentReads.ToArray());
            Assert.AreEqual(0, factoryCalls);
            Assert.AreEqual(0, noticeCalls);
            Assert.AreEqual(0, identityCalls);
            Assert.AreEqual(0, clock.Calls);
            Assert.AreEqual(0, exporter.Attempts.Count);
        }

        [TestMethod]
        public async Task NoticeFailureFailsClosedBeforeFactoryIdentityOrTimer()
        {
            int factoryCalls = 0;
            int noticeCalls = 0;
            int identityCalls = 0;
            ManualTimeProvider clock = new();
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(
                exporterFactory: () => { Interlocked.Increment(ref factoryCalls); return exporter; },
                enableSyntheticCollection: true,
                clock: clock,
                readEnvironmentVariable: _ => null,
                showNotice: () => { noticeCalls++; throw new InvalidOperationException(SENTINEL); },
                resolveIdentity: _ => { identityCalls++; return new(_apiId, "ephemeral"); },
                startTimer: true);

            await ExerciseDisabledSessionAsync(session);

            Assert.AreEqual(1, noticeCalls);
            Assert.AreEqual(0, factoryCalls);
            Assert.AreEqual(0, identityCalls);
            Assert.AreEqual(0, clock.Timers.Count);
            Assert.AreEqual(0, exporter.Attempts.Count);
        }

        [DataTestMethod]
        [DataRow("ephemeral", false)]
        [DataRow("newly_saved", true)]
        [DataRow("reused", true)]
        public async Task NoticePrecedesExportAndIdentityIsResolvedOnlyForFirstAcceptedConfiguration(string stability, bool overridePath)
        {
            int factoryCalls = 0;
            int noticeCalls = 0;
            int noticeCallsAtFactory = 0;
            int environmentCalls = 0;
            string? environmentValue = null;
            ConcurrentQueue<string?> identityPaths = new();
            ManualTimeProvider clock = new();
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(
                exporterFactory: () =>
                {
                    Interlocked.Increment(ref factoryCalls);
                    noticeCallsAtFactory = Volatile.Read(ref noticeCalls);
                    return exporter;
                },
                enableSyntheticCollection: true,
                configPath: SENTINEL + "_root",
                clock: clock,
                readEnvironmentVariable: name =>
                {
                    if (name == "DAB_TELEMETRY_OPT_OUT")
                    {
                        environmentCalls++;
                        return environmentValue;
                    }

                    return null;
                },
                showNotice: () => Interlocked.Increment(ref noticeCalls),
                resolveIdentity: path => { identityPaths.Enqueue(path); return new(_apiId, stability); },
                startTimer: false);

            EngineTelemetryEvent started = await exporter.FirstExported.Task.WaitAsync(_testTimeout);
            Assert.AreEqual(PROCESS_STARTED, started.Name);
            Assert.AreEqual(0, identityPaths.Count, "Process start must not resolve deployment identity before configuration.");
            Assert.IsFalse(started.Properties.ContainsKey("dab_api_id"));
            Assert.IsFalse(started.Properties.ContainsKey("dab_api_id_stability"));

            // Changing the injected value is not an in-process disable control.
            environmentValue = "true";
            RuntimeConfig config = CreateConfig();
            session.MarkHostReady();
            session.AcceptConfiguration(config, "late_configuration", overridePath ? SENTINEL + "_accepted" : null);
            session.AcceptConfiguration(config, "hot_reload", SENTINEL + "_ignored");
            session.AcceptConfiguration(CreateConfig(DatabaseType.PostgreSQL), "hot_reload", SENTINEL + "_reload");
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);

            Assert.AreEqual(1, factoryCalls);
            Assert.AreEqual(1, noticeCalls);
            Assert.AreEqual(1, noticeCallsAtFactory);
            Assert.AreEqual(1, environmentCalls);
            CollectionAssert.AreEqual(new[] { overridePath ? SENTINEL + "_accepted" : SENTINEL + "_root" }, identityPaths.ToArray());
            Assert.AreEqual("late_configuration", Event(records, READY).Properties["configuration_delivery"]);
            foreach (EngineTelemetryEvent record in records.Where(record => record.Name != PROCESS_STARTED))
            {
                Assert.AreEqual(_apiId.ToString("D"), record.Properties["dab_api_id"]);
                Assert.AreEqual(stability, record.Properties["dab_api_id_stability"]);
            }

            AssertEnvelope(records);
            AssertNoPrivateValues(records);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ReadinessRequiresBothHostAndAcceptedConfigurationAndIsEmittedOnce(bool hostFirst)
        {
            ManualTimeProvider clock = new();
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, clock);
            RuntimeConfig config = CreateConfig();

            clock.Advance(TimeSpan.FromMilliseconds(3));
            if (hostFirst)
            {
                session.MarkHostReady();
                session.MarkHostReady();
            }
            else
            {
                session.AcceptConfiguration(config);
                session.AcceptConfiguration(config);
            }

            Assert.IsFalse(session.IsReady);
            clock.Advance(TimeSpan.FromMilliseconds(7));
            if (hostFirst)
            {
                session.AcceptConfiguration(config);
            }
            else
            {
                session.MarkHostReady();
            }

            Assert.IsTrue(session.IsReady);
            session.MarkHostReady();
            session.AcceptConfiguration(config);
            session.StartupFailed(TelemetryFailureStage.Metadata);
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);

            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, STOPPED }, records.Select(record => record.Name).ToArray());
            EngineTelemetryEvent ready = Event(records, READY);
            Assert.AreEqual(1L, ready.ConfigurationEpoch);
            Assert.AreEqual("10", ready.Properties["startup_ms"]);
            Assert.AreEqual(_start.AddMilliseconds(10), ready.OccurredAt);
            Assert.AreEqual("configuration-v1", ready.Properties["snapshot_schema"]);
            Assert.IsFalse(session.IsReady);
            Assert.AreEqual(0, clock.Timers.Count);
            AssertEnvelope(records);
        }

        [DataTestMethod]
        [DataRow(true, false, false)]
        [DataRow(false, true, false)]
        [DataRow(false, false, true)]
        public async Task HealthOnlyConfigurationIsNotReadyUntilADataServingApiIsAccepted(bool rest, bool graphQl, bool mcp)
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, new());
            session.MarkHostReady();
            session.AcceptConfiguration(CreateConfig(rest: false, graphQl: false, mcp: false));
            Assert.IsFalse(session.IsReady, "Health defaults to enabled, but is not data-serving readiness.");

            session.AcceptConfiguration(CreateConfig(rest: rest, graphQl: graphQl, mcp: mcp), "late_configuration");
            Assert.IsTrue(session.IsReady);
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);

            Assert.AreEqual(2L, Event(records, READY).ConfigurationEpoch);
            Assert.AreEqual(0, records.Count(record => record.Name == CONFIGURATION_CHANGED));
            Assert.AreEqual(rest ? "enabled" : "disabled", Event(records, READY).Properties["runtime.rest.effective"]);
            Assert.AreEqual(graphQl ? "enabled" : "disabled", Event(records, READY).Properties["runtime.graphql.effective"]);
            Assert.AreEqual(mcp ? "enabled" : "disabled", Event(records, READY).Properties["runtime.mcp.effective"]);
        }

        [TestMethod]
        public async Task RequestsCapturedBeforeReadyCannotRecordEvenWhenCompletedOrPromotedAfterReady()
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, new());
            using EngineTelemetryRequestScope beforeConfig = session.BeginRequest(EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            session.AcceptConfiguration(CreateConfig());
            using EngineTelemetryRequestScope beforeHost = session.BeginRequest(EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            Assert.IsNull(session.CurrentRequest);
            Assert.IsNull(beforeConfig.Config);
            Assert.IsNull(beforeHost.Config);
            AssertNoActiveMeasurements(session);

            session.MarkHostReady();
            beforeConfig.MarkEligible();
            beforeHost.MarkEligible();
            beforeConfig.Complete(EngineTelemetryOutcome.Success, 200);
            beforeHost.Complete(EngineTelemetryOutcome.Success, 200);
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);

            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, READY, STOPPED }, records.Select(record => record.Name).ToArray());
        }

        [TestMethod]
        public async Task SameReferenceIsIgnoredButValueEqualRecordCloneAndReacceptedOldReferenceGetNewEpochs()
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, new());
            RuntimeConfig config = CreateConfig();
            RuntimeConfig clone = config with { };
            Assert.AreNotSame(config, clone);
            Assert.AreEqual(config, clone, "This test must distinguish reference equality from record value equality.");
            AcceptAndReady(session, config);

            session.AcceptConfiguration(config, "hot_reload");
            session.AcceptConfiguration(clone, "hot_reload");
            session.AcceptConfiguration(clone, "hot_reload");
            session.AcceptConfiguration(config, SENTINEL);
            session.MarkHostReady();
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            EngineTelemetryEvent[] changed = records.Where(record => record.Name == CONFIGURATION_CHANGED).ToArray();

            CollectionAssert.AreEqual(new long[] { 2, 3 }, changed.Select(record => record.ConfigurationEpoch).ToArray());
            Assert.AreEqual("hot_reload", changed[0].Properties["configuration_delivery"]);
            Assert.AreEqual("unknown", changed[1].Properties["configuration_delivery"]);
            Assert.AreEqual(1L, Event(records, READY).ConfigurationEpoch);
            Assert.AreEqual(3L, Event(records, STOPPED).ConfigurationEpoch);
            Assert.AreEqual(0, Summaries(records).Length, "A reload alone does not synthesize usage or flush a window.");
            AssertNoPrivateValues(records);
        }

        [TestMethod]
        public async Task RejectedConfigurationReportsFailureAgainstCurrentEpochWithoutAcceptingAnotherOne()
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, new());
            session.ConfigurationChangeFailed();
            RuntimeConfig accepted = CreateConfig();
            AcceptAndReady(session, accepted);
            session.ConfigurationChangeFailed();
            session.ConfigurationChangeFailed();
            using (EngineTelemetryRequestScope request = session.BeginRequest(EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous))
            {
                Assert.AreSame(accepted, request.Config);
                Assert.AreEqual(1L, request.Configuration.Epoch);
                request.Complete(EngineTelemetryOutcome.Success, 200);
            }

            session.AcceptConfiguration(CreateConfig(DatabaseType.MySQL), "hot_reload");
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            EngineTelemetryEvent[] failures = records.Where(record => record.Name == "dab.engine.configuration_change_failed").ToArray();

            Assert.AreEqual(3, failures.Length);
            Assert.AreEqual(0L, failures[0].ConfigurationEpoch, "Rejected late configuration is nonterminal even before the first accepted epoch.");
            Assert.IsTrue(failures.Skip(1).All(record => record.ConfigurationEpoch == 1 && record.Properties["failure_category"] == "configuration"));
            Assert.IsTrue(failures.All(record => !record.Properties.ContainsKey("snapshot_schema")));
            Assert.AreEqual(2L, Event(records, CONFIGURATION_CHANGED).ConfigurationEpoch);
            Assert.AreEqual("mssql", Event(records, READY).Properties["data_sources.types"]);
            Assert.AreEqual("mysql", Event(records, CONFIGURATION_CHANGED).Properties["data_sources.types"]);
            Assert.AreEqual(1L, Summary(records, "request").ConfigurationEpoch);
        }

        [DataTestMethod]
        [DataRow((int)TelemetryFailureStage.Initialization, "initialization")]
        [DataRow((int)TelemetryFailureStage.Configuration, "configuration")]
        [DataRow((int)TelemetryFailureStage.Parsing, "parsing")]
        [DataRow((int)TelemetryFailureStage.Validation, "validation")]
        [DataRow((int)TelemetryFailureStage.Metadata, "metadata")]
        [DataRow((int)TelemetryFailureStage.Serving, "serving")]
        [DataRow(-1, "unknown")]
        [DataRow(int.MaxValue, "unknown")]
        public async Task StartupFailureIsOnceSanitizedAndPreventsReadiness(int stage, string expectedStage)
        {
            ManualTimeProvider clock = new();
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, clock);
            session.StartupFailed((TelemetryFailureStage)stage);
            session.StartupFailed(TelemetryFailureStage.Serving);
            session.MarkHostReady();
            session.AcceptConfiguration(CreateConfig());
            Assert.IsFalse(session.IsReady);
            clock.Advance(TimeSpan.FromMinutes(10));
            session.Tick();
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);

            EngineTelemetryEvent failed = Event(records, "dab.engine.startup_failed");
            Assert.AreEqual(0L, failed.ConfigurationEpoch);
            Assert.AreEqual(expectedStage, failed.Properties["failure_stage"]);
            Assert.AreEqual("initialization", failed.Properties["failure_category"]);
            Assert.AreEqual("startup_failed", Event(records, "dab.engine.heartbeat").Properties["run_state"]);
            Assert.AreEqual(0, records.Count(record => record.Name == READY || record.Name == SUMMARY));
            AssertNoPrivateValues(records);
        }

        [TestMethod]
        public async Task ManualTimerUsesInjectedClockAndReportsInitializingThenReadyWithoutInventingUsage()
        {
            ManualTimeProvider clock = new();
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, clock, startTimer: true);
            ManualTimer timer = clock.Timers.Single();
            Assert.AreEqual(TimeSpan.FromMinutes(1), timer.DueTime);
            Assert.AreEqual(TimeSpan.FromMinutes(1), timer.Period);
            Assert.IsTrue(timer.FlowSuppressed, "A timer must not retain the creating request's execution context.");

            clock.Advance(TimeSpan.FromMinutes(9));
            timer.Fire();
            clock.Advance(TimeSpan.FromMinutes(1));
            timer.Fire();
            timer.Fire();
            AcceptAndReady(session);
            clock.Advance(TimeSpan.FromMinutes(10));
            timer.Fire();
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            EngineTelemetryEvent[] heartbeats = records.Where(record => record.Name == "dab.engine.heartbeat").ToArray();

            Assert.AreEqual(2, heartbeats.Length);
            CollectionAssert.AreEqual(new[] { "initializing", "ready" }, heartbeats.Select(record => record.Properties["run_state"]).ToArray());
            CollectionAssert.AreEqual(new long[] { 0, 1 }, heartbeats.Select(record => record.ConfigurationEpoch).ToArray());
            Assert.AreEqual(_start.AddMinutes(10), heartbeats[0].OccurredAt);
            Assert.AreEqual(_start.AddMinutes(20), heartbeats[1].OccurredAt);
            Assert.IsTrue(heartbeats.All(record => record.Properties["uptime"] == "1m_1h"));
            Assert.AreEqual(0, Summaries(records).Length);
            Assert.IsTrue(timer.IsDisposed);
            timer.Fire();
            session.Tick();
            Assert.AreEqual(records.Length, exporter.Records.Count);
        }

        [TestMethod]
        public async Task InFlightRequestRetainsOldEntityProviderSnapshotAndEpochAfterReload()
        {
            ManualTimeProvider clock = new();
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, clock);
            RuntimeConfig oldConfig = CreateConfig(DatabaseType.MSSQL, EntitySourceType.Table);
            RuntimeConfig newConfig = CreateConfig(DatabaseType.PostgreSQL, EntitySourceType.View, entityCount: 2);
            AcceptAndReady(session, oldConfig);
            using EngineTelemetryRequestScope oldRequest = session.BeginRequest(EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);

            clock.Advance(TimeSpan.FromMilliseconds(10));
            session.AcceptConfiguration(newConfig, "hot_reload");
            using (EngineTelemetryRequestScope newRequest = session.BeginRequest(EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Custom))
            {
                Assert.AreSame(newConfig, newRequest.Config);
                Assert.AreSame(oldRequest.Configuration.Owner, newRequest.Configuration.Owner);
                Assert.AreEqual(2L, newRequest.Configuration.Epoch);
                using (EngineTelemetryMeasurementScope? operation = session.BeginOperation(ENTITY, EngineTelemetryOperation.Read))
                {
                    Assert.IsNotNull(operation);
                    operation.Complete(EngineTelemetryOutcome.Failure);
                }

                newRequest.Complete(EngineTelemetryOutcome.Failure, 500);
            }

            Assert.AreSame(oldRequest, session.CurrentRequest);
            Assert.AreSame(oldConfig, oldRequest.Config);
            // Resolve metadata after the reload, not merely before it in an already-created operation.
            using (EngineTelemetryMeasurementScope? operation = session.BeginOperation(ENTITY, EngineTelemetryOperation.Read))
            {
                Assert.IsNotNull(operation);
                operation.Complete(EngineTelemetryOutcome.Success);
            }

            using (EngineTelemetryMeasurementScope? attempt = session.BeginDatabaseAttempt(DatabaseType.MSSQL))
            {
                Assert.IsNotNull(attempt);
                attempt.Complete(EngineTelemetryOutcome.Success);
            }

            session.RecordCacheLookup(EngineTelemetryCacheLayer.Level1, EngineTelemetryCacheResult.Miss);
            using (EngineTelemetryMeasurementScope? embedding = session.BeginEmbedding())
            {
                Assert.IsNotNull(embedding);
                embedding.Complete(EngineTelemetryOutcome.Success);
            }

            clock.Advance(TimeSpan.FromMilliseconds(10));
            oldRequest.Complete(EngineTelemetryOutcome.Success, 200);
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);

            EngineTelemetryEvent ready = Event(records, READY);
            EngineTelemetryEvent changed = Event(records, CONFIGURATION_CHANGED);
            Assert.AreEqual("mssql", ready.Properties["data_sources.types"]);
            Assert.AreEqual("1", ready.Properties["scale.entity_count"]);
            Assert.AreEqual("enabled", ready.Properties["entities.any.table"]);
            Assert.AreEqual("postgresql", changed.Properties["data_sources.types"]);
            Assert.AreEqual("2-10", changed.Properties["scale.entity_count"]);
            Assert.AreEqual("enabled", changed.Properties["entities.any.view"]);
            EngineTelemetryEvent oldOperation = Summary(records, "operation", epoch: 1);
            EngineTelemetryEvent newOperation = Summary(records, "operation", epoch: 2);
            Assert.AreEqual("ms_sql", oldOperation.Properties["provider"]);
            Assert.AreEqual("table", oldOperation.Properties["object_type"]);
            Assert.AreEqual("postgre_sql", newOperation.Properties["provider"]);
            Assert.AreEqual("view", newOperation.Properties["object_type"]);
            AssertOutcomes(oldOperation, success: 1);
            AssertOutcomes(newOperation, failure: 1);
            AssertOutcomes(Summary(records, "request", epoch: 1), success: 1);
            AssertOutcomes(Summary(records, "request", epoch: 2), failure: 1);
            Assert.AreEqual(1L, Summary(records, "database_attempt").ConfigurationEpoch);
            Assert.AreEqual(1L, Summary(records, "cache_lookup").ConfigurationEpoch);
            Assert.AreEqual(1L, Summary(records, "embedding").ConfigurationEpoch);
            Assert.AreEqual(2L, Event(records, FIRST_SERVED).ConfigurationEpoch);
            Assert.AreEqual(1L, Event(records, FIRST_SUCCESS).ConfigurationEpoch);
            Assert.AreEqual("20", Event(records, FIRST_SUCCESS).Properties["since_ready_ms"]);
            AssertEnvelope(records);
            AssertNoPrivateValues(records);
        }

        [TestMethod]
        public async Task ConcurrentCompletionsEmitFirstServedAndFirstSuccessOnlyOnceAcrossReloads()
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, new());
            AcceptAndReady(session);
            EngineTelemetryRequestScope[] oldRequests = Enumerable.Range(0, 16).Select(_ => CaptureRequest(session)).ToArray();
            session.AcceptConfiguration(CreateConfig(DatabaseType.MySQL), "hot_reload");

            await CompleteConcurrentlyAsync(oldRequests, EngineTelemetryOutcome.Failure, 503);
            EngineTelemetryRequestScope[] newRequests = Enumerable.Range(0, 16).Select(_ => CaptureRequest(session)).ToArray();
            await CompleteConcurrentlyAsync(newRequests, EngineTelemetryOutcome.Success, 200);
            session.AcceptConfiguration(CreateConfig(DatabaseType.PostgreSQL), "hot_reload");
            CaptureRequest(session).Complete(EngineTelemetryOutcome.Success, 200);
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);

            Assert.AreEqual(1L, Event(records, FIRST_SERVED).ConfigurationEpoch);
            Assert.AreEqual("failure", Event(records, FIRST_SERVED).Properties["outcome"]);
            Assert.AreEqual(2L, Event(records, FIRST_SUCCESS).ConfigurationEpoch);
            Assert.AreEqual("success", Event(records, FIRST_SUCCESS).Properties["outcome"]);
            AssertOutcomes(Summary(records, "request", epoch: 1), failure: 16);
            AssertOutcomes(Summary(records, "request", epoch: 2), success: 16);
            AssertOutcomes(Summary(records, "request", epoch: 3), success: 1);
            Assert.IsTrue(oldRequests.All(request => request.IsCompleted && request.Configuration.Epoch == 1));
            Assert.IsTrue(newRequests.All(request => request.IsCompleted && request.Configuration.Epoch == 2));
            Assert.IsNull(session.CurrentRequest);
            AssertEnvelope(records);
        }

        [TestMethod]
        public async Task ConcurrentDuplicateCompleteCountsOneCanceledRequestAndCannotBecomeSuccessLater()
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, new());
            AcceptAndReady(session);
            EngineTelemetryRequestScope request = CaptureRequest(session);
            request.SetOutcome(EngineTelemetryOutcome.Success);
            await CompleteConcurrentlyAsync(Enumerable.Repeat(request, 32), EngineTelemetryOutcome.Canceled, status: null);
            request.Complete(EngineTelemetryOutcome.Success, 200);
            request.Dispose();
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);

            Assert.AreEqual(EngineTelemetryOutcome.Canceled, request.Outcome);
            AssertOutcomes(Summary(records, "request"), canceled: 1);
            Assert.AreEqual("unknown", Summary(records, "http_outcome").Properties["http_status_class"]);
            Assert.AreEqual(1L, Count(Summary(records, "http_outcome")));
            Assert.AreEqual("canceled", Event(records, FIRST_SERVED).Properties["outcome"]);
            Assert.AreEqual(0, records.Count(record => record.Name == FIRST_SUCCESS));
        }

        [TestMethod]
        public async Task RequestDisposeOnlyRestoresAmbientStateAndCompletionUsesTheCapturedOwner()
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, new());
            AcceptAndReady(session);
            EngineTelemetryRequestScope outer = session.BeginRequest(EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            EngineTelemetryRequestScope inner = session.BeginRequest(EngineTelemetryApi.Mcp, EngineTelemetryTransport.Stdio, EngineTelemetryRole.Custom);
            Assert.AreSame(inner, session.CurrentRequest);
            inner.Dispose();
            inner.Dispose();
            Assert.AreSame(outer, session.CurrentRequest);
            Assert.IsFalse(inner.IsCompleted);

            // The inner completion callback runs while a different request is ambient.
            inner.Complete(EngineTelemetryOutcome.Failure, 500);
            outer.Dispose();
            Assert.IsNull(session.CurrentRequest);
            Assert.IsFalse(outer.IsCompleted);
            outer.Complete(EngineTelemetryOutcome.Success, 200);
            using (EngineTelemetryRequestScope abandoned = session.BeginRequest(EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous))
            {
                abandoned.SetOutcome(EngineTelemetryOutcome.Success);
            }

            Assert.IsNull(session.CurrentRequest);
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            EngineTelemetryEvent[] requests = Summaries(records, "request");
            Assert.AreEqual(2, requests.Length);
            AssertOutcomes(requests.Single(record => record.Properties["api"] == "mcp"), failure: 1);
            AssertOutcomes(requests.Single(record => record.Properties["api"] == "rest"), success: 1);
            Assert.AreEqual("mcp", Event(records, FIRST_SERVED).Properties["api"]);
            Assert.AreEqual("rest", Event(records, FIRST_SUCCESS).Properties["api"]);
            Assert.AreEqual("rest", Summary(records, "http_outcome").Properties["api"]);
        }

        [TestMethod]
        public async Task IneligibleRequestNeedsExplicitPromotionAndLatePromotionCannotUndoCompletion()
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, new());
            AcceptAndReady(session);
            using (EngineTelemetryRequestScope discovery = session.BeginRequest(EngineTelemetryApi.Mcp, EngineTelemetryTransport.Stdio, EngineTelemetryRole.Unknown, eligible: false))
            {
                Assert.AreSame(discovery, session.CurrentRequest);
                AssertNoActiveMeasurements(session);
                discovery.Complete(EngineTelemetryOutcome.Success);
                discovery.MarkEligible();
                discovery.Complete(EngineTelemetryOutcome.Success);
                AssertNoActiveMeasurements(session);
            }

            using (EngineTelemetryRequestScope data = session.BeginRequest(EngineTelemetryApi.Mcp, EngineTelemetryTransport.Stdio, EngineTelemetryRole.Unknown, eligible: false))
            {
                AssertNoActiveMeasurements(session);
                data.MarkEligible();
                data.SetRole(EngineTelemetrySession.ClassifyRole(SENTINEL, authenticated: true));
                using (EngineTelemetryMeasurementScope? operation = session.BeginOperation(ENTITY, EngineTelemetryOperation.Read))
                {
                    Assert.IsNotNull(operation);
                    operation.Complete(EngineTelemetryOutcome.Success);
                }

                data.Complete(EngineTelemetryOutcome.Success, 200);
            }

            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            AssertOutcomes(Summary(records, "request"), success: 1);
            Assert.AreEqual("custom", Summary(records, "request").Properties["role_class"]);
            Assert.AreEqual("stdio", Summary(records, "request").Properties["transport"]);
            AssertOutcomes(Summary(records, "operation"), success: 1);
            Assert.AreEqual(0, Summaries(records, "http_outcome").Length, "Even a supplied status is not an HTTP observation on stdio.");
            AssertNoPrivateValues(records);
        }

        [TestMethod]
        public async Task NestedOperationsSqlRetriesCacheLayersAndEmbeddingEmitIndependentJsonCountFamilies()
        {
            ManualTimeProvider clock = new();
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, clock);
            AcceptAndReady(session);
            using EngineTelemetryRequestScope request = session.BeginRequest(EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Authenticated);
            using (EngineTelemetryMeasurementScope? read = session.BeginOperation(ENTITY, EngineTelemetryOperation.Read))
            {
                Assert.IsNotNull(read);
                using (EngineTelemetryMeasurementScope? nested = session.BeginOperation(ENTITY, EngineTelemetryOperation.Write))
                {
                    Assert.IsNotNull(nested);
                    nested.Complete(EngineTelemetryOutcome.Failure);
                    using (EngineTelemetryMeasurementScope? failedAttempt = session.BeginDatabaseAttempt(DatabaseType.MSSQL))
                    {
                        Assert.IsNotNull(failedAttempt);
                        // Disposal without Complete observes one failed SQL attempt.
                    }

                    using (EngineTelemetryMeasurementScope? retry = session.BeginDatabaseAttempt(DatabaseType.MSSQL))
                    {
                        Assert.IsNotNull(retry);
                        retry.Complete(EngineTelemetryOutcome.Success);
                        retry.Dispose();
                    }

                    nested.Dispose();
                }

                session.RecordCacheLookup(EngineTelemetryCacheLayer.Level1, EngineTelemetryCacheResult.Miss);
                session.RecordCacheLookup(EngineTelemetryCacheLayer.Level2, EngineTelemetryCacheResult.Hit);
                using (EngineTelemetryMeasurementScope? embedding = session.BeginEmbedding())
                {
                    Assert.IsNotNull(embedding);
                    embedding.Complete(EngineTelemetryOutcome.Canceled);
                }

                read.Complete(EngineTelemetryOutcome.Success);
                read.Dispose();
            }

            // Disposing the nested scopes must restore depth so a sibling is counted.
            using (EngineTelemetryMeasurementScope? write = session.BeginOperation(ENTITY, EngineTelemetryOperation.Write))
            {
                Assert.IsNotNull(write);
                write.Complete(EngineTelemetryOutcome.Canceled);
            }

            clock.Advance(TimeSpan.FromMilliseconds(5));
            request.Complete(EngineTelemetryOutcome.PartialFailure, 200);
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            EngineTelemetryEvent[] summaries = Summaries(records);

            CollectionAssert.AreEquivalent(new[] { "request", "operation", "database_attempt", "cache_lookup", "embedding", "http_outcome" },
                summaries.Select(record => record.Properties["family"]).Distinct().ToArray());
            Assert.AreEqual(8, summaries.Length);
            AssertOutcomes(Summary(records, "request"), partialFailure: 1);
            EngineTelemetryEvent[] operations = Summaries(records, "operation");
            AssertOutcomes(operations.Single(record => record.Properties["operation"] == "read"), success: 1);
            AssertOutcomes(operations.Single(record => record.Properties["operation"] == "write"), canceled: 1);
            AssertOutcomes(Summary(records, "database_attempt"), failure: 1, success: 1);
            Assert.AreEqual("ms_sql", Summary(records, "database_attempt").Properties["provider"]);
            AssertOutcomes(Summary(records, "embedding"), canceled: 1);
            EngineTelemetryEvent[] caches = Summaries(records, "cache_lookup");
            Assert.AreEqual("miss", caches.Single(record => record.Properties["cache_layer"] == "level1").Properties["cache_result"]);
            Assert.AreEqual("hit", caches.Single(record => record.Properties["cache_layer"] == "level2").Properties["cache_result"]);
            Assert.IsTrue(caches.All(record => Count(record) == 1));
            EngineTelemetryEvent http = Summary(records, "http_outcome");
            Assert.AreEqual("success", http.Properties["http_status_class"], "HTTP 200 is independent of logical partial failure.");
            Assert.AreEqual(1L, Count(http));
            foreach (EngineTelemetryEvent record in caches.Append(http))
            {
                AssertNoOutcomesOrLatency(record);
            }

            EngineTelemetryEvent requestSummary = Summary(records, "request");
            Assert.AreEqual(EngineTelemetryHistogram.BUCKET_SCHEMA, requestSummary.Properties["latency_schema"]);
            CollectionAssert.AreEqual(EngineTelemetryHistogram.UpperBoundsMilliseconds.ToArray(),
                JsonSerializer.Deserialize<long[]>(requestSummary.Properties["latency_bounds_ms"])!);
            CollectionAssert.AreEqual(new long[] { 0, 1, 0, 0, 0, 0, 0, 0, 0, 0 },
                JsonSerializer.Deserialize<long[]>(requestSummary.Properties["latency_buckets"])!);
            Assert.AreEqual("1", requestSummary.Properties["timed_count"]);
            Assert.AreEqual("true", requestSummary.Properties["latency_complete"]);
            foreach (EngineTelemetryEvent record in summaries)
            {
                Assert.AreEqual(1L, record.ConfigurationEpoch);
                Assert.AreEqual("true", record.Properties["final"]);
                Assert.AreEqual(_start.ToString("O", CultureInfo.InvariantCulture), record.Properties["window_start"]);
                Assert.AreEqual(_start.AddMilliseconds(5).ToString("O", CultureInfo.InvariantCulture), record.Properties["window_end"]);
                Assert.AreEqual("false", record.Properties["capped"]);
                Assert.AreEqual("sql_commands_only", record.Properties["database_attempt_coverage"]);
                Assert.AreEqual("request_context_observed", record.Properties["cache_coverage"]);
                if (record.Properties["family"] != "request")
                {
                    Assert.IsFalse(record.Properties.ContainsKey("latency_buckets"));
                }
            }

            Assert.AreEqual(0, records.Count(record => record.Name == FIRST_SUCCESS));
            AssertEnvelope(records);
        }

        [TestMethod]
        public async Task UncompletedMeasurementsDefaultToFailureAndHooksAfterRequestCompletionAreIgnored()
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, new());
            AcceptAndReady(session);
            using EngineTelemetryRequestScope request = session.BeginRequest(EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            using (EngineTelemetryMeasurementScope? operation = session.BeginOperation(ENTITY, EngineTelemetryOperation.Read))
            using (EngineTelemetryMeasurementScope? attempt = session.BeginDatabaseAttempt(DatabaseType.MSSQL))
            using (EngineTelemetryMeasurementScope? embedding = session.BeginEmbedding())
            {
                Assert.IsNotNull(operation);
                Assert.IsNotNull(attempt);
                Assert.IsNotNull(embedding);
            }

            request.Complete(EngineTelemetryOutcome.Failure, 500);
            Assert.AreSame(request, session.CurrentRequest, "Complete does not restore the ambient scope; Dispose does.");
            AssertNoActiveMeasurements(session);
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            foreach (string family in new[] { "request", "operation", "database_attempt", "embedding" })
            {
                AssertOutcomes(Summary(records, family), failure: 1);
            }

            Assert.AreEqual(0, Summaries(records, "cache_lookup").Length);
            Assert.AreEqual("server_error", Summary(records, "http_outcome").Properties["http_status_class"]);
        }

        [DataTestMethod]
        [DataRow(DatabaseType.MSSQL, EntitySourceType.Table, "ms_sql", "table", "read")]
        [DataRow(DatabaseType.DWSQL, EntitySourceType.View, "dw_sql", "view", "read")]
        [DataRow(DatabaseType.PostgreSQL, EntitySourceType.Table, "postgre_sql", "table", "read")]
        [DataRow(DatabaseType.MySQL, EntitySourceType.View, "my_sql", "view", "read")]
        [DataRow(DatabaseType.CosmosDB_NoSQL, EntitySourceType.Table, "cosmos_db", "document", "read")]
        [DataRow(DatabaseType.MSSQL, EntitySourceType.StoredProcedure, "ms_sql", "stored_procedure", "execute")]
        public async Task OperationDimensionsUseCapturedProviderAndSourceType(DatabaseType database, EntitySourceType sourceType,
            string provider, string objectType, string operationName)
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, new());
            AcceptAndReady(session, CreateConfig(database, sourceType));
            using EngineTelemetryRequestScope request = session.BeginRequest(EngineTelemetryApi.GraphQL, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            using (EngineTelemetryMeasurementScope? operation = session.BeginOperation(ENTITY, EngineTelemetryOperation.Read))
            {
                Assert.IsNotNull(operation);
                operation.Complete(EngineTelemetryOutcome.Success);
            }

            request.Complete(EngineTelemetryOutcome.Success, 200);
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            EngineTelemetryEvent summary = Summary(records, "operation");
            Assert.AreEqual(provider, summary.Properties["provider"]);
            Assert.AreEqual(objectType, summary.Properties["object_type"]);
            Assert.AreEqual(operationName, summary.Properties["operation"]);
            AssertOutcomes(summary, success: 1);
            AssertNoPrivateValues(records);
        }

        [TestMethod]
        public async Task MissingEntityMetadataStaysUnknownWithoutSuppressingTheOperation()
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, new());
            AcceptAndReady(session);
            using EngineTelemetryRequestScope request = session.BeginRequest(EngineTelemetryApi.Rest, EngineTelemetryTransport.InProcess, EngineTelemetryRole.Anonymous);
            foreach (string? entity in new[] { null, SENTINEL + "_missing" })
            {
                using EngineTelemetryMeasurementScope? operation = session.BeginOperation(entity, EngineTelemetryOperation.Read);
                Assert.IsNotNull(operation);
                operation.Complete(EngineTelemetryOutcome.Success);
            }

            request.Complete(EngineTelemetryOutcome.Success, 200);
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            EngineTelemetryEvent summary = Summary(records, "operation");
            Assert.AreEqual("unknown", summary.Properties["provider"]);
            Assert.AreEqual("unknown", summary.Properties["object_type"]);
            AssertOutcomes(summary, success: 2);
            Assert.AreEqual(0, Summaries(records, "http_outcome").Length);
            AssertNoPrivateValues(records);
        }

        [TestMethod]
        public async Task ParallelAsyncBranchesKeepRequestOwnerButDoNotShareOperationDepth()
        {
            const int BRANCHES = 16;
            TaskCompletionSource allEntered = Signal();
            TaskCompletionSource release = Signal();
            int entered = 0;
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, new());
            RuntimeConfig config = CreateConfig();
            AcceptAndReady(session, config);
            using EngineTelemetryRequestScope request = session.BeginRequest(EngineTelemetryApi.GraphQL, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            Task[] branches = Enumerable.Range(0, BRANCHES).Select(_ => Task.Run(async () =>
            {
                Assert.AreSame(request, session.CurrentRequest);
                using EngineTelemetryMeasurementScope? operation = session.BeginOperation(ENTITY, EngineTelemetryOperation.Read);
                Assert.IsNotNull(operation);
                using (EngineTelemetryMeasurementScope? nested = session.BeginOperation(ENTITY, EngineTelemetryOperation.Write))
                {
                    Assert.IsNotNull(nested);
                    nested.Complete(EngineTelemetryOutcome.Failure);
                }

                if (Interlocked.Increment(ref entered) == BRANCHES)
                {
                    allEntered.TrySetResult();
                }

                await release.Task.WaitAsync(_testTimeout);
                Assert.AreSame(config, session.CurrentRequest!.Config);
                operation.Complete(EngineTelemetryOutcome.Success);
            })).ToArray();

            try
            {
                await allEntered.Task.WaitAsync(_testTimeout);
                session.AcceptConfiguration(CreateConfig(DatabaseType.PostgreSQL, EntitySourceType.View), "hot_reload");
                release.TrySetResult();
                await Task.WhenAll(branches).WaitAsync(_testTimeout);
                Assert.AreSame(request, session.CurrentRequest);
                request.Complete(EngineTelemetryOutcome.Success, 200);
                EngineTelemetryEvent[] records = await DrainAsync(session, exporter);

                EngineTelemetryEvent operation = Summary(records, "operation");
                Assert.AreEqual("read", operation.Properties["operation"]);
                Assert.AreEqual("ms_sql", operation.Properties["provider"]);
                AssertOutcomes(operation, success: BRANCHES);
                Assert.AreEqual(1, Summaries(records, "operation").Length);
                AssertOutcomes(Summary(records, "request"), success: 1);
            }
            finally
            {
                release.TrySetResult();
                await Task.WhenAll(branches).WaitAsync(_testTimeout);
            }
        }

        [TestMethod]
        public async Task SessionsHaveIndependentAmbientScopesOwnersAndMilestonesEvenWithTheSameConfig()
        {
            CapturingExporter firstExporter = new();
            CapturingExporter secondExporter = new();
            using EngineTelemetrySession first = CreateSession(firstExporter, new());
            using EngineTelemetrySession second = CreateSession(secondExporter, new());
            RuntimeConfig config = CreateConfig();
            AcceptAndReady(first, config);
            AcceptAndReady(second, config);
            using EngineTelemetryRequestScope firstRequest = first.BeginRequest(EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            Assert.IsNull(second.CurrentRequest);
            AssertNoActiveMeasurements(second);
            using EngineTelemetryRequestScope secondRequest = second.BeginRequest(EngineTelemetryApi.Mcp, EngineTelemetryTransport.Stdio, EngineTelemetryRole.Authenticated);
            Assert.AreNotSame(firstRequest.Configuration.Owner, secondRequest.Configuration.Owner);
            Assert.AreSame(firstRequest, first.CurrentRequest);
            Assert.AreSame(secondRequest, second.CurrentRequest);

            await Task.WhenAll(
                Task.Run(() => firstRequest.Complete(EngineTelemetryOutcome.Failure, 400)),
                Task.Run(() => secondRequest.Complete(EngineTelemetryOutcome.Success, 200))).WaitAsync(_testTimeout);
            EngineTelemetryEvent[] firstRecords = await DrainAsync(first, firstExporter);
            EngineTelemetryEvent[] secondRecords = await DrainAsync(second, secondExporter);

            Assert.AreNotEqual(firstRecords[0].SessionId, secondRecords[0].SessionId);
            Assert.AreEqual("rest", Event(firstRecords, FIRST_SERVED).Properties["api"]);
            Assert.AreEqual(0, firstRecords.Count(record => record.Name == FIRST_SUCCESS));
            Assert.AreEqual("mcp", Event(secondRecords, FIRST_SUCCESS).Properties["api"]);
            AssertOutcomes(Summary(firstRecords, "request"), failure: 1);
            AssertOutcomes(Summary(secondRecords, "request"), success: 1);
            AssertEnvelope(firstRecords);
            AssertEnvelope(secondRecords);
        }

        [TestMethod]
        public async Task SessionSerializationKeepsEveryLogicalOutcomeIndependentOfHttpSuccess()
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, new());
            AcceptAndReady(session);
            foreach (EngineTelemetryOutcome outcome in Enum.GetValues<EngineTelemetryOutcome>())
            {
                using EngineTelemetryRequestScope request = session.BeginRequest(EngineTelemetryApi.GraphQL, EngineTelemetryTransport.Http, EngineTelemetryRole.Custom);
                request.Complete(outcome, 200);
            }

            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            AssertOutcomes(Summary(records, "request"), unknown: 1, success: 1, failure: 1, partialFailure: 1, canceled: 1);
            EngineTelemetryEvent http = Summary(records, "http_outcome");
            Assert.AreEqual("success", http.Properties["http_status_class"]);
            Assert.AreEqual(5L, Count(http));
            AssertNoOutcomesOrLatency(http);
            Assert.AreEqual("unknown", Event(records, FIRST_SERVED).Properties["outcome"]);
            Assert.AreEqual("success", Event(records, FIRST_SUCCESS).Properties["outcome"]);
            Assert.AreEqual(5L, Count(Summary(records, "request"), "timed_count"));
        }

        [DataTestMethod]
        [DataRow("embedded", "embedded")]
        [DataRow("web", "web")]
        [DataRow("mcp_stdio", "mcp_stdio")]
        [DataRow(SENTINEL, "unknown")]
        public async Task ContextAndSnapshotsContainVersionStringsAndClosedCategoriesNotRawConfiguration(string mode, string expectedMode)
        {
            ManualTimeProvider clock = new();
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, clock, executionMode: mode);
            AcceptAndReady(session, CreateConfig());
            CaptureRequest(session).Complete(EngineTelemetryOutcome.Success, 200);
            session.AcceptConfiguration(CreateConfig(DatabaseType.MySQL, EntitySourceType.View, entityCount: 2), SENTINEL);
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);

            AssertEnvelope(records);
            foreach (EngineTelemetryEvent record in records)
            {
                Assert.AreEqual(expectedMode, record.Properties["execution_mode"]);
                Assert.AreEqual("unknown", record.Properties["distribution"]);
                Assert.AreEqual("unknown", record.Properties["release_channel"]);
                Assert.AreEqual(_start, record.OccurredAt);
                Assert.IsFalse(record.Properties.ContainsKey("configuration"));
                Assert.IsFalse(record.Properties.ContainsKey("connection_string"));
            }

            Assert.AreEqual("unknown", Event(records, CONFIGURATION_CHANGED).Properties["configuration_delivery"]);
            AssertNoPrivateValues(records);
        }

        [TestMethod]
        public async Task IdentityResolverFailureDisablesWithoutLeakingExceptionOrEmittingReadyOrStopped()
        {
            int identityCalls = 0;
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(
                exporterFactory: () => exporter,
                enableSyntheticCollection: true,
                clock: new ManualTimeProvider(),
                readEnvironmentVariable: _ => null,
                showNotice: () => { },
                resolveIdentity: _ => { identityCalls++; throw new InvalidOperationException(SENTINEL); },
                startTimer: false);
            await exporter.FirstExported.Task.WaitAsync(_testTimeout);

            session.AcceptConfiguration(CreateConfig());
            session.MarkHostReady();
            session.AcceptConfiguration(CreateConfig());
            await session.StopAsync().WaitAsync(_testTimeout);
            await exporter.Disposed.Task.WaitAsync(_testTimeout);

            Assert.IsFalse(session.IsEnabled);
            Assert.IsFalse(session.IsReady);
            Assert.AreEqual(1, identityCalls);
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED }, exporter.Records.Select(record => record.Name).ToArray());
            AssertNoPrivateValues(exporter.Records.ToArray());
        }

        [TestMethod]
        public async Task StopAsyncAwaitsPendingExportThenDrainsFinalSummaryBeforeStoppedExactlyOnce()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            CapturingExporter exporter = BlockFirstExport(entered, release);
            ManualTimeProvider clock = new();
            using EngineTelemetrySession session = CreateSession(exporter, clock, startTimer: true);
            try
            {
                await entered.Task.WaitAsync(_testTimeout);
                AcceptAndReady(session);
                CaptureRequest(session).Complete(EngineTelemetryOutcome.Success, 200);
                Task stop = session.StopAsync();
                Assert.IsFalse(stop.IsCompleted, "The exporter still owns the first pending event.");
                Assert.IsFalse(session.IsEnabled);
                Assert.IsFalse(session.IsReady);
                Assert.IsTrue(clock.Timers.Single().IsDisposed);
                release.TrySetResult();
                await stop.WaitAsync(_testTimeout);
                await exporter.Disposed.Task.WaitAsync(_testTimeout);
                EngineTelemetryEvent[] records = exporter.Records.ToArray();

                Assert.AreEqual(STOPPED, records[^1].Name);
                Assert.AreEqual("graceful_shutdown", Event(records, STOPPED).Properties["reason"]);
                Assert.AreEqual("under_1m", Event(records, STOPPED).Properties["uptime"]);
                AssertOutcomes(Summary(records, "request"), success: 1);
                Assert.IsTrue(Summaries(records).All(record => record.Properties["final"] == "true" && record.Sequence < records[^1].Sequence));
                Assert.AreEqual(1, exporter.DisposeCalls);
                await session.StopAsync().WaitAsync(_testTimeout);
                session.AcceptConfiguration(CreateConfig());
                session.MarkHostReady();
                session.Tick();
                CaptureRequest(session).Complete(EngineTelemetryOutcome.Success, 200);
                Assert.AreEqual(records.Length, exporter.Records.Count);
                AssertEnvelope(records);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [DataTestMethod]
        [DataRow("disable")]
        [DataRow("canceled_stop")]
        [DataRow("graceful_stop")]
        public async Task SlowIdentityResolutionMustNotBlockDisableOrShutdown(string mode)
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(
                () => exporter, enableSyntheticCollection: true, readEnvironmentVariable: _ => null,
                showNotice: () => { }, startTimer: false,
                resolveIdentity: _ =>
                {
                    entered.TrySetResult();
                    release.Task.WaitAsync(_testTimeout).GetAwaiter().GetResult();
                    return new(_apiId, "ephemeral");
                });
            await exporter.FirstExported.Task.WaitAsync(_testTimeout);
            session.MarkHostReady();
            Task accepting = Task.Run(() => session.AcceptConfiguration(CreateConfig()));
            Task? stopping = null;
            try
            {
                await entered.Task.WaitAsync(_testTimeout);
                stopping = Task.Run(async () =>
                {
                    if (mode == "disable")
                    {
                        session.Disable();
                    }
                    else
                    {
                        await session.StopAsync(new CancellationToken(canceled: mode == "canceled_stop"));
                    }
                });
                await stopping.WaitAsync(TimeSpan.FromSeconds(1));
                Assert.IsFalse(session.IsEnabled);
                Assert.IsFalse(session.IsReady);
            }
            finally
            {
                release.TrySetResult();
                await accepting.WaitAsync(_testTimeout);
                if (stopping is not null)
                {
                    await stopping.WaitAsync(_testTimeout);
                }
            }

            await exporter.Disposed.Task.WaitAsync(_testTimeout);
            Assert.IsFalse(exporter.Records.Any(record => record.Name == READY),
                "An identity read finishing after stop must not publish a configuration or readiness.");
            Assert.AreEqual(mode == "graceful_stop" ? 1 : 0, exporter.Records.Count(record => record.Name == STOPPED));
        }

        [TestMethod]
        public async Task ConcurrentConfigurationAcceptanceResolvesIdentityOnce()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            int identityCalls = 0;
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(
                () => exporter, enableSyntheticCollection: true, readEnvironmentVariable: _ => null,
                showNotice: () => { }, startTimer: false,
                resolveIdentity: _ =>
                {
                    Interlocked.Increment(ref identityCalls);
                    entered.TrySetResult();
                    release.Task.WaitAsync(_testTimeout).GetAwaiter().GetResult();
                    return new(_apiId, "ephemeral");
                });
            session.MarkHostReady();
            RuntimeConfig config = CreateConfig();
            Task[] accepting = Enumerable.Range(0, 8).Select(_ => Task.Run(() => session.AcceptConfiguration(config))).ToArray();
            try
            {
                await entered.Task.WaitAsync(_testTimeout);
            }
            finally
            {
                release.TrySetResult();
                await Task.WhenAll(accepting).WaitAsync(_testTimeout);
            }

            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            Assert.AreEqual(1, identityCalls);
            Assert.AreEqual(1, records.Count(record => record.Name == READY));
            Assert.IsFalse(records.Any(record => record.Name == CONFIGURATION_CHANGED));
            Assert.AreEqual(_apiId.ToString("D"), Event(records, READY).Properties["dab_api_id"]);
        }

        [TestMethod]
        public async Task NewerConfigurationAcceptanceCannotBeOverwrittenByAnOlderIdentityWaiter()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(
                () => exporter, enableSyntheticCollection: true, readEnvironmentVariable: _ => null,
                showNotice: () => { }, startTimer: false,
                resolveIdentity: _ =>
                {
                    entered.TrySetResult();
                    release.Task.WaitAsync(_testTimeout).GetAwaiter().GetResult();
                    return new(_apiId, "ephemeral");
                });
            RuntimeConfig initial = CreateConfig(DatabaseType.MSSQL);
            RuntimeConfig replacement = CreateConfig(DatabaseType.PostgreSQL, entityCount: 2);
            session.MarkHostReady();
            Task acceptingInitial = Task.Run(() => session.AcceptConfiguration(initial));
            try
            {
                await entered.Task.WaitAsync(_testTimeout);
                // Own the publication gate so the newer acceptance deterministically publishes
                // first after identity resolves. No sleeps, scheduler assumptions or production hook.
                object publicationGate = typeof(EngineTelemetrySession)
                    .GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session)!;
                lock (publicationGate)
                {
                    release.TrySetResult();
                    session.AcceptConfiguration(replacement, "hot_reload");
                }
            }
            finally
            {
                release.TrySetResult();
                await acceptingInitial.WaitAsync(_testTimeout);
            }

            using (EngineTelemetryRequestScope request = session.BeginRequest(
                EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous))
            {
                Assert.AreSame(replacement, request.Config, "A superseded startup acceptance must not restore stale configuration.");
                Assert.AreEqual(1L, request.Configuration.Epoch);
            }

            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            Assert.AreEqual("postgresql", Event(records, READY).Properties["data_sources.types"]);
            Assert.AreEqual("hot_reload", Event(records, READY).Properties["configuration_delivery"]);
            Assert.IsFalse(records.Any(record => record.Name == CONFIGURATION_CHANGED));
        }

        [TestMethod]
        public async Task InitialAcceptanceCannotReplaceAnAlreadyAcceptedReload()
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, new());
            RuntimeConfig staleStartupConfig = CreateConfig(DatabaseType.MSSQL);
            RuntimeConfig replacement = CreateConfig(DatabaseType.PostgreSQL);
            session.MarkHostReady();
            session.AcceptConfiguration(replacement, "hot_reload");
            // Startup read its local reference before the completed reload, but only now
            // reaches the acceptance call. The caller marks this as initial-only admission.
            session.AcceptConfiguration(staleStartupConfig, "startup", onlyIfUnconfigured: true);
            using EngineTelemetryRequestScope request = session.BeginRequest(
                EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            Assert.AreSame(replacement, request.Config);
            Assert.AreEqual(1L, request.Configuration.Epoch);
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            Assert.AreEqual("postgresql", Event(records, READY).Properties["data_sources.types"]);
            Assert.IsFalse(records.Any(record => record.Name == CONFIGURATION_CHANGED));
        }

        [TestMethod]
        public async Task TerminalRequestOutcomeCannotBeReplacedByALateToolResult()
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = CreateSession(exporter, new());
            AcceptAndReady(session);
            using EngineTelemetryRequestScope request = session.BeginRequest(
                EngineTelemetryApi.Mcp, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            request.Complete(EngineTelemetryOutcome.Canceled);
            await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => request.SetOutcome(EngineTelemetryOutcome.Success))));
            request.Complete(EngineTelemetryOutcome.Success, 200);

            Assert.AreEqual(EngineTelemetryOutcome.Canceled, request.Outcome);
            EngineTelemetryEvent[] records = await DrainAsync(session, exporter);
            AssertOutcomes(Summary(records, "request"), canceled: 1);
            Assert.IsFalse(records.Any(record => record.Name == FIRST_SUCCESS));
        }

        [TestMethod]
        public async Task CanceledStopMustDiscardWithoutReadingTheClockOrConstructingFinalEvents()
        {
            CapturingExporter exporter = new();
            RejectAfterInitializationClock clock = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(
                () => exporter, enableSyntheticCollection: true, clock: clock,
                readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveIdentity: _ => new(_apiId, "ephemeral"), startTimer: false);
            await exporter.FirstExported.Task.WaitAsync(_testTimeout);
            AcceptAndReady(session);
            clock.RejectReads = true;

            await session.StopAsync(new CancellationToken(canceled: true)).WaitAsync(_testTimeout);
            await exporter.Disposed.Task.WaitAsync(_testTimeout);

            Assert.IsFalse(session.IsEnabled);
            Assert.IsFalse(exporter.Records.Any(record => record.Name == STOPPED || record.Name == SUMMARY));
        }

        private sealed class RejectAfterInitializationClock : TimeProvider
        {
            public bool RejectReads { get; set; }

            public override DateTimeOffset GetUtcNow()
                => RejectReads ? throw new InvalidOperationException("Shutdown must discard without creating events.") : _start;

            public override long GetTimestamp()
                => RejectReads ? throw new InvalidOperationException("Shutdown must discard without measuring uptime.") : 0;
        }

        [TestMethod]
        public async Task RepeatedStopAsyncWhileDrainingMustNotCompleteBeforeTheOriginalDrain()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            CapturingExporter exporter = BlockFirstExport(entered, release);
            using EngineTelemetrySession session = CreateSession(exporter, new());
            Task? firstStop = null;
            Task? secondStop = null;
            try
            {
                await entered.Task.WaitAsync(_testTimeout);
                AcceptAndReady(session);
                firstStop = session.StopAsync();
                secondStop = session.StopAsync();

                Assert.IsFalse(firstStop.IsCompleted);
                // Regression: the current _enabled guard returns Task.CompletedTask to this
                // caller even while the first StopAsync still owns an undrained exporter.
                Assert.IsFalse(secondStop.IsCompleted, "Every graceful-stop caller must await the pending drain, not merely the disabled collection flag.");
            }
            finally
            {
                release.TrySetResult();
                if (firstStop is not null && secondStop is not null)
                {
                    await Task.WhenAll(firstStop, secondStop).WaitAsync(_testTimeout);
                    await exporter.Disposed.Task.WaitAsync(_testTimeout);
                }
            }
        }

        [DataTestMethod]
        [DataRow("disable")]
        [DataRow("dispose")]
        [DataRow("canceled_stop")]
        public async Task DisableOrCanceledStopCancelsInFlightAndDiscardsQueuedAndUnflushedData(string shutdown)
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            CapturingExporter exporter = BlockFirstExport(entered, release);
            ManualTimeProvider clock = new();
            using EngineTelemetrySession session = CreateSession(exporter, clock, startTimer: true);
            try
            {
                await entered.Task.WaitAsync(_testTimeout);
                AcceptAndReady(session);
                CaptureRequest(session).Complete(EngineTelemetryOutcome.Success, 200);
                // One completed window is queued; another remains only in aggregation state.
                // Window arithmetic is tested separately; this verifies the session's discard boundary.
                clock.Advance(TimeSpan.FromHours(6));
                session.Tick();
                CaptureRequest(session).Complete(EngineTelemetryOutcome.Failure, 500);
                if (shutdown == "disable")
                {
                    session.Disable();
                }
                else if (shutdown == "dispose")
                {
                    session.Dispose();
                }
                else
                {
                    using CancellationTokenSource cancellation = new();
                    cancellation.Cancel();
                    await session.StopAsync(cancellation.Token).WaitAsync(_testTimeout);
                }

                // Cancellation, not release, must unblock the cooperative exporter.
                await exporter.Disposed.Task.WaitAsync(_testTimeout);
                await session.StopAsync().WaitAsync(_testTimeout);
                session.Tick();
                session.AcceptConfiguration(CreateConfig());
                CaptureRequest(session).Complete(EngineTelemetryOutcome.Success, 200);

                Assert.IsFalse(session.IsEnabled);
                Assert.IsFalse(session.IsReady);
                Assert.IsTrue(clock.Timers.Single().IsDisposed);
                Assert.AreEqual(1, exporter.Attempts.Count);
                Assert.AreEqual(PROCESS_STARTED, exporter.Attempts.Single().Name);
                Assert.AreEqual(0, exporter.Records.Count, "No queued snapshot, summary, milestone, or stopped record may be transmitted after disable.");
                Assert.AreEqual(1, exporter.DisposeCalls);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [TestMethod]
        public async Task LaterStopCallerCanCancelAnAlreadyPendingDrain()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            CapturingExporter exporter = BlockFirstExport(entered, release);
            using EngineTelemetrySession session = CreateSession(exporter, new());
            using CancellationTokenSource cancellation = new();
            Task? firstStop = null;
            Task? secondStop = null;
            try
            {
                await entered.Task.WaitAsync(_testTimeout);
                AcceptAndReady(session);
                firstStop = session.StopAsync();
                secondStop = session.StopAsync(cancellation.Token);
                Assert.IsFalse(firstStop.IsCompleted);
                Assert.IsFalse(secondStop.IsCompleted);
                cancellation.Cancel();
                await secondStop.WaitAsync(TimeSpan.FromSeconds(1));
                await exporter.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(1));
                Assert.AreEqual(0, exporter.Records.Count, "Cancellation must discard, not flush the pending queue.");
                Assert.AreEqual(1, exporter.Attempts.Count);
            }
            finally
            {
                release.TrySetResult();
                if (firstStop is not null && secondStop is not null)
                {
                    await Task.WhenAll(firstStop, secondStop).WaitAsync(_testTimeout);
                }
            }
        }

        private static EngineTelemetrySession CreateSession(CapturingExporter exporter, ManualTimeProvider clock,
            string executionMode = "embedded", bool startTimer = false)
            => EngineTelemetrySession.Create(
                exporterFactory: () => exporter,
                enableSyntheticCollection: true,
                executionMode: executionMode,
                clock: clock,
                readEnvironmentVariable: _ => null,
                showNotice: () => { },
                resolveIdentity: _ => new(_apiId, "ephemeral"),
                startTimer: startTimer);

        private static RuntimeConfig CreateConfig(DatabaseType database = DatabaseType.MSSQL,
            EntitySourceType sourceType = EntitySourceType.Table, int entityCount = 1,
            bool rest = true, bool graphQl = true, bool mcp = true)
        {
            Entity entity = new(
                Source: new(SENTINEL, sourceType, null, null),
                GraphQL: null!, Fields: null, Rest: null!,
                Permissions: [new(SENTINEL, [new(EntityActionOperation.Read, null, new(SENTINEL, SENTINEL))])],
                Mappings: new() { [SENTINEL] = SENTINEL }, Relationships: null,
                Description: SENTINEL);
            Dictionary<string, Entity> entities = new(StringComparer.Ordinal) { [ENTITY] = entity };
            for (int index = 1; index < entityCount; index++)
            {
                entities.Add(ENTITY + index.ToString(CultureInfo.InvariantCulture), entity);
            }

            RuntimeOptions runtime = new(
                Rest: new(Enabled: rest, Path: "/" + SENTINEL),
                GraphQL: new(Enabled: graphQl, Path: "/" + SENTINEL),
                Mcp: new(Enabled: mcp, Path: "/" + SENTINEL, Description: SENTINEL),
                Host: null, BaseRoute: SENTINEL);
            return new(Schema: SENTINEL, DataSource: new(database, SENTINEL), Entities: new(entities), Runtime: runtime);
        }

        private static void AcceptAndReady(EngineTelemetrySession session, RuntimeConfig? config = null)
        {
            session.AcceptConfiguration(config ?? CreateConfig());
            session.MarkHostReady();
            Assert.IsTrue(session.IsReady);
        }

        private static EngineTelemetryRequestScope CaptureRequest(EngineTelemetrySession session)
        {
            EngineTelemetryRequestScope request = session.BeginRequest(EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            // A response-completion callback retains its scope after the request adapter unwinds.
            request.Dispose();
            return request;
        }

        private static async Task ExerciseDisabledSessionAsync(EngineTelemetrySession session)
        {
            Assert.IsFalse(session.IsEnabled);
            session.AcceptConfiguration(CreateConfig());
            session.MarkHostReady();
            session.ConfigurationChangeFailed();
            session.StartupFailed((TelemetryFailureStage)int.MaxValue);
            session.Tick();
            using EngineTelemetryRequestScope request = session.BeginRequest(EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            request.MarkEligible();
            request.Complete(EngineTelemetryOutcome.Success, 200);
            Assert.IsNull(session.CurrentRequest);
            AssertNoActiveMeasurements(session);
            await session.StopAsync().WaitAsync(_testTimeout);
            session.Disable();
            Assert.IsFalse(session.IsReady);
        }

        private static void AssertNoActiveMeasurements(EngineTelemetrySession session)
        {
            Assert.IsNull(session.BeginOperation(ENTITY, EngineTelemetryOperation.Read));
            Assert.IsNull(session.BeginDatabaseAttempt(DatabaseType.MSSQL));
            Assert.IsNull(session.BeginEmbedding());
            session.RecordCacheLookup(EngineTelemetryCacheLayer.Level1, EngineTelemetryCacheResult.Hit);
        }

        private static async Task CompleteConcurrentlyAsync(IEnumerable<EngineTelemetryRequestScope> requests, EngineTelemetryOutcome outcome, int? status)
        {
            EngineTelemetryRequestScope[] captured = requests.ToArray();
            Assert.IsTrue(captured.Length > 0);
            TaskCompletionSource allEntered = Signal();
            TaskCompletionSource release = Signal();
            int entered = 0;
            Task[] completions = captured.Select(request => Task.Run(async () =>
            {
                if (Interlocked.Increment(ref entered) == captured.Length)
                {
                    allEntered.TrySetResult();
                }

                await release.Task.WaitAsync(_testTimeout);
                request.Complete(outcome, status);
            })).ToArray();
            try
            {
                await allEntered.Task.WaitAsync(_testTimeout);
            }
            finally
            {
                release.TrySetResult();
                await Task.WhenAll(completions).WaitAsync(_testTimeout);
            }
        }

        private static async Task<EngineTelemetryEvent[]> DrainAsync(EngineTelemetrySession session, CapturingExporter exporter)
        {
            await session.StopAsync().WaitAsync(_testTimeout);
            await exporter.Disposed.Task.WaitAsync(_testTimeout);
            return exporter.Records.ToArray();
        }

        private static EngineTelemetryEvent Event(IEnumerable<EngineTelemetryEvent> records, string name)
            => records.Single(record => record.Name == name);

        private static EngineTelemetryEvent[] Summaries(IEnumerable<EngineTelemetryEvent> records, string? family = null)
            => records.Where(record => record.Name == SUMMARY && (family is null || record.Properties["family"] == family)).ToArray();

        private static EngineTelemetryEvent Summary(IEnumerable<EngineTelemetryEvent> records, string family, long epoch = 1)
            => Summaries(records, family).Single(record => record.ConfigurationEpoch == epoch);

        private static long Count(EngineTelemetryEvent record, string key = "count")
            => long.Parse(record.Properties[key], CultureInfo.InvariantCulture);

        private static void AssertOutcomes(EngineTelemetryEvent record, long unknown = 0, long success = 0,
            long failure = 0, long partialFailure = 0, long canceled = 0)
        {
            Assert.AreEqual(unknown, Count(record, "unknown"));
            Assert.AreEqual(success, Count(record, "success"));
            Assert.AreEqual(failure, Count(record, "failure"));
            Assert.AreEqual(partialFailure, Count(record, "partial_failure"));
            Assert.AreEqual(canceled, Count(record, "canceled"));
            Assert.AreEqual(unknown + success + failure + partialFailure + canceled, Count(record));
        }

        private static void AssertNoOutcomesOrLatency(EngineTelemetryEvent record)
        {
            foreach (string key in new[] { "unknown", "success", "failure", "partial_failure", "canceled", "latency_schema", "latency_buckets", "timed_count" })
            {
                Assert.IsFalse(record.Properties.ContainsKey(key), record.Properties["family"] + " must not contain " + key);
            }
        }

        private static void AssertEnvelope(EngineTelemetryEvent[] records)
        {
            Assert.IsTrue(records.Length > 0);
            Assert.AreEqual(PROCESS_STARTED, records[0].Name);
            Assert.AreEqual(0L, records[0].ConfigurationEpoch);
            Assert.AreEqual(records.Length, records.Select(record => record.EventId).Distinct().Count());
            Guid sessionId = records[0].SessionId;
            Assert.AreNotEqual(Guid.Empty, sessionId);
            for (int index = 0; index < records.Length; index++)
            {
                EngineTelemetryEvent record = records[index];
                Assert.AreNotEqual(Guid.Empty, record.EventId);
                Assert.AreEqual(sessionId, record.SessionId);
                Assert.AreEqual(index + 1L, record.Sequence);
                Assert.IsTrue(record.IsSynthetic);
                Assert.AreEqual(TimeSpan.Zero, record.OccurredAt.Offset);
                Assert.AreEqual("0", record.Properties["sender_dropped_events"]);
                foreach (string key in new[] { "dab_version", "dotnet_version" })
                {
                    Assert.IsTrue(Version.TryParse(record.Properties[key], out Version? version), key + " must be a version, not raw configuration.");
                    Assert.IsNotNull(version);
                    Assert.IsTrue(version.Build >= 0);
                    Assert.AreEqual(-1, version.Revision, key + " uses major.minor.patch components.");
                }
            }
        }

        private static void AssertNoPrivateValues(EngineTelemetryEvent[] records)
            => Assert.IsFalse(JsonSerializer.Serialize(records).Contains(SENTINEL, StringComparison.Ordinal));

        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private static CapturingExporter BlockFirstExport(TaskCompletionSource entered, TaskCompletionSource release)
            => new((record, token) =>
            {
                if (record.Name == PROCESS_STARTED)
                {
                    entered.TrySetResult();
                    release.Task.WaitAsync(_testTimeout, token).GetAwaiter().GetResult();
                }

                return true;
            });

        private sealed class CapturingExporter : IEngineTelemetryExporter
        {
            private readonly Func<EngineTelemetryEvent, CancellationToken, bool>? _export;
            private int _disposeCalls;

            public CapturingExporter(Func<EngineTelemetryEvent, CancellationToken, bool>? export = null)
            {
                _export = export;
            }

            public ConcurrentQueue<EngineTelemetryEvent> Attempts { get; } = new();
            public ConcurrentQueue<EngineTelemetryEvent> Records { get; } = new();
            public TaskCompletionSource<EngineTelemetryEvent> FirstExported { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Disposed { get; } = Signal();
            public int DisposeCalls => Volatile.Read(ref _disposeCalls);

            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Attempts.Enqueue(record);
                bool exported = _export?.Invoke(record, cancellationToken) ?? true;
                cancellationToken.ThrowIfCancellationRequested();
                if (exported)
                {
                    Records.Enqueue(record);
                    FirstExported.TrySetResult(record);
                }

                return ValueTask.FromResult(exported);
            }

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCalls);
                Disposed.TrySetResult();
            }
        }

        private sealed class ManualTimeProvider : TimeProvider
        {
            private long _elapsedTicks;

            public ConcurrentQueue<ManualTimer> Timers { get; } = new();
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() => Interlocked.Read(ref _elapsedTicks);
            public override DateTimeOffset GetUtcNow() => _start.AddTicks(Interlocked.Read(ref _elapsedTicks));
            public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _elapsedTicks, elapsed.Ticks);

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                ManualTimer timer = new(callback, state, dueTime, period, ExecutionContext.IsFlowSuppressed());
                Timers.Enqueue(timer);
                return timer;
            }
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private int _disposed;

            public ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period, bool flowSuppressed)
            {
                _callback = callback;
                _state = state;
                DueTime = dueTime;
                Period = period;
                FlowSuppressed = flowSuppressed;
            }

            public TimeSpan DueTime { get; private set; }
            public TimeSpan Period { get; private set; }
            public bool FlowSuppressed { get; }
            public bool IsDisposed => Volatile.Read(ref _disposed) != 0;

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (IsDisposed)
                {
                    return false;
                }

                DueTime = dueTime;
                Period = period;
                return true;
            }

            public void Fire()
            {
                if (!IsDisposed)
                {
                    _callback(_state);
                }
            }

            public void Dispose() => Interlocked.Exchange(ref _disposed, 1);

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }

        private sealed class ThrowingTimeProvider : TimeProvider
        {
            private int _calls;

            public int Calls => Volatile.Read(ref _calls);
            public override long TimestampFrequency => throw UnexpectedUse();
            public override long GetTimestamp() => throw UnexpectedUse();
            public override DateTimeOffset GetUtcNow() => throw UnexpectedUse();
            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => throw UnexpectedUse();

            private InvalidOperationException UnexpectedUse()
            {
                Interlocked.Increment(ref _calls);
                return new("A disabled session must not use its clock.");
            }
        }
    }
}
