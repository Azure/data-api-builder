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
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Offline tests of the real Core session and immutable bridge. Identity callbacks return
    /// store evidence; filesystem safety/publication races and the CLI grammar have separate tests.
    /// No host, database, SDK exporter, profile directory or process environment is modified.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    [TestCategory("CliTelemetry")]
    public class CliTelemetrySessionTests
    {
        private const string SENTINEL = "PRIVATE_CLI_SESSION_SENTINEL_e762";
        private const string ROOT = SENTINEL + "_actual_resolved_root";
        private const string OTHER_ROOT = SENTINEL + "_different_actual_resolved_root";
        private const string FIRST_RUN = "dab.cli.first_run";
        private const string COMMAND = "dab.cli.command";
        private const string LAUNCH = "dab.cli.engine_launch";
        private static readonly Guid _installationId = new("56b8f9f3-f6b8-41b3-9a30-946738178db9");
        private static readonly Guid _apiId = new("c1a211f9-021c-44c3-bd4d-c8242b56af9d");
        private static readonly Guid _otherApiId = new("d96605d3-5b64-4d90-a1a9-bd45973c9eb7");
        private static readonly DateTimeOffset _start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        private static readonly TimeSpan _testTimeout = TimeSpan.FromSeconds(10);
        private static readonly ImmutableDictionary<string, string> _noOptions = ImmutableDictionary<string, string>.Empty;

        [TestMethod]
        public async Task DefaultCreationIsDisabled()
        {
            using CliTelemetrySession session = CliTelemetrySession.Create();
            await ExerciseDisabledAsync(session);
            Assert.AreEqual(Guid.Empty, session.SessionId);
        }

        [DataTestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        public async Task DisabledGatePrecedesEveryDependencyAndStateChange(bool synthetic, bool hasFactory)
        {
            int environmentCalls = 0;
            int factoryCalls = 0;
            int noticeCalls = 0;
            int installationCalls = 0;
            int createCalls = 0;
            int lookupCalls = 0;
            ThrowingTimeProvider clock = new();
            CapturingExporter exporter = new();
            Func<IProductTelemetryExporter<IProductTelemetryEvent>>? factory = hasFactory ? () =>
            {
                Interlocked.Increment(ref factoryCalls);
                return exporter;
            }
            : null;
            using CliTelemetrySession session = CliTelemetrySession.Create(factory, synthetic, clock,
                readEnvironmentVariable: _ => { environmentCalls++; return null; },
                showNotice: () => noticeCalls++,
                resolveInstallation: () => { installationCalls++; return new(_installationId, "newly_saved"); },
                createIdentity: _ => { createCalls++; return new(_apiId, "ephemeral"); },
                lookupIdentity: _ => { lookupCalls++; return new(_apiId, "reused"); });

            await ExerciseDisabledAsync(session);

            Assert.AreEqual(Guid.Empty, session.SessionId);
            Assert.AreEqual(0, environmentCalls);
            Assert.AreEqual(0, factoryCalls);
            Assert.AreEqual(0, noticeCalls);
            Assert.AreEqual(0, installationCalls);
            Assert.AreEqual(0, createCalls);
            Assert.AreEqual(0, lookupCalls);
            Assert.AreEqual(0, clock.Calls, "Swallowing an unexpected clock call is not a disabled gate.");
            Assert.AreEqual(0, exporter.Attempts.Count);
        }

        [DataTestMethod]
        [DataRow("1")]
        [DataRow("true")]
        [DataRow("TRUE")]
        [DataRow(" 1 ")]
        [DataRow(" true ")]
        public async Task OptOutVetoIsTheOnlyEnvironmentReadAndPrecedesAllWork(string optOut)
        {
            ConcurrentQueue<string> reads = new();
            int dependencyCalls = 0;
            ThrowingTimeProvider clock = new();
            using CliTelemetrySession session = CliTelemetrySession.Create(
                exporterFactory: () => { Interlocked.Increment(ref dependencyCalls); return new CapturingExporter(); },
                enableSyntheticCollection: true,
                clock: clock,
                readEnvironmentVariable: name => { reads.Enqueue(name); return optOut; },
                showNotice: () => dependencyCalls++,
                resolveInstallation: () => { dependencyCalls++; return new(_installationId, "newly_saved"); },
                createIdentity: _ => { dependencyCalls++; return new(_apiId, "ephemeral"); },
                lookupIdentity: _ => { dependencyCalls++; return new(_apiId, "reused"); });

            await ExerciseDisabledAsync(session);

            CollectionAssert.AreEqual(new[] { ProductTelemetryPolicy.OPT_OUT_ENV_VAR }, reads.ToArray());
            Assert.AreEqual(Guid.Empty, session.SessionId);
            Assert.AreEqual(0, dependencyCalls);
            Assert.AreEqual(0, clock.Calls);
        }

        [TestMethod]
        public async Task OptOutReadFailureFailsClosedBeforeNoticeIdentityClockAndFactory()
        {
            int calls = 0;
            ThrowingTimeProvider clock = new();
            using CliTelemetrySession session = CliTelemetrySession.Create(
                () => { calls++; return new CapturingExporter(); }, true, clock,
                readEnvironmentVariable: _ => throw new InvalidOperationException(SENTINEL),
                showNotice: () => calls++,
                resolveInstallation: () => { calls++; return new(_installationId, "newly_saved"); });

            await ExerciseDisabledAsync(session);
            Assert.AreEqual(0, calls);
            Assert.AreEqual(0, clock.Calls);
        }

        [TestMethod]
        public async Task NoticeFailureFailsClosedBeforeInstallationContextClockAndFactory()
        {
            ConcurrentQueue<string> reads = new();
            int noticeCalls = 0;
            int otherCalls = 0;
            ThrowingTimeProvider clock = new();
            using CliTelemetrySession session = CliTelemetrySession.Create(
                () => { otherCalls++; return new CapturingExporter(); }, true, clock,
                readEnvironmentVariable: name => { reads.Enqueue(name); return null; },
                showNotice: () => { noticeCalls++; throw new IOException(SENTINEL); },
                resolveInstallation: () => { otherCalls++; return new(_installationId, "newly_saved"); });

            await ExerciseDisabledAsync(session);
            CollectionAssert.AreEqual(new[] { ProductTelemetryPolicy.OPT_OUT_ENV_VAR }, reads.ToArray());
            Assert.AreEqual(1, noticeCalls);
            Assert.AreEqual(0, otherCalls);
            Assert.AreEqual(0, clock.Calls);
        }

        [DataTestMethod]
        [DataRow("context")]
        [DataRow("installation")]
        [DataRow("clock")]
        public async Task InitializationDependencyFailureDoesNotCreateASender(string failingDependency)
        {
            int factories = 0;
            TimeProvider clock = failingDependency == "clock" ? new ThrowingTimeProvider() : new ManualTimeProvider();
            using CliTelemetrySession session = CliTelemetrySession.Create(
                () => { factories++; return new CapturingExporter(); }, true, clock,
                readEnvironmentVariable: name => failingDependency == "context" && name != ProductTelemetryPolicy.OPT_OUT_ENV_VAR
                    ? throw new InvalidOperationException(SENTINEL) : null,
                showNotice: () => { },
                resolveInstallation: () => failingDependency == "installation"
                    ? throw new IOException(SENTINEL) : new(_installationId, "newly_saved"));

            await ExerciseDisabledAsync(session);
            Assert.AreEqual(0, factories);
        }

        [TestMethod]
        public async Task StartupPolicyIsCapturedEvenForTheDefaultIdentityStore()
        {
            ConcurrentQueue<string> reads = new();
            string? optOut = null;
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CliTelemetrySession.Create(() => exporter, true, new ManualTimeProvider(),
                readEnvironmentVariable: name =>
                {
                    reads.Enqueue(name);
                    return name == ProductTelemetryPolicy.OPT_OUT_ENV_VAR ? optOut : null;
                },
                showNotice: () => { },
                resolveInstallation: () => new(null, "unavailable"));
            optOut = "true";

            // Null has no filesystem target. This exercises the real Resolve(path, readEnv)
            // default while proving its policy read cannot consult the changed environment.
            ProductTelemetryLaunchContext? launch = session.BeginEngineLaunch(null, CliTelemetryLaunchSource.StartWeb);
            Assert.IsNotNull(launch);
            Assert.IsNotNull(launch.ApiIdentity);
            Assert.AreEqual("ephemeral", launch.ApiIdentity.Stability);
            session.Complete("start", "none", _noOptions, CliTelemetryOutcome.Success);
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);

            Assert.AreEqual(1, reads.Count(name => name == ProductTelemetryPolicy.OPT_OUT_ENV_VAR));
            Assert.AreEqual(2, records.Length);
            AssertNoApiIdentity(Event(records, COMMAND));
        }

        [DataTestMethod]
        [DataRow("newly_saved", true, true)]
        [DataRow("reused", true, false)]
        [DataRow("unavailable", false, false)]
        [DataRow("unavailable", true, false)]
        [DataRow("newly_saved", false, false)]
        [DataRow(SENTINEL, true, false)]
        public async Task OnlyValidNewlySavedInstallationEvidenceEmitsFirstRun(string stability, bool hasId, bool firstRun)
        {
            int notices = 0;
            int installations = 0;
            int factories = 0;
            int noticesAtInstallation = 0;
            int noticesAtFactory = 0;
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CliTelemetrySession.Create(() =>
            {
                Interlocked.Increment(ref factories);
                noticesAtFactory = Volatile.Read(ref notices);
                return exporter;
            }, true, new ManualTimeProvider(), readEnvironmentVariable: _ => null,
                showNotice: () => Interlocked.Increment(ref notices),
                resolveInstallation: () =>
                {
                    installations++;
                    noticesAtInstallation = Volatile.Read(ref notices);
                    return new(hasId ? _installationId : null, stability);
                });

            if (!firstRun)
            {
                Assert.AreEqual(0, factories, "There is no process-start event or eager exporter.");
            }

            session.Complete("init", "none", _noOptions, CliTelemetryOutcome.Success);
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);

            Assert.AreEqual(1, notices);
            Assert.AreEqual(1, installations);
            Assert.AreEqual(1, factories);
            Assert.AreEqual(1, noticesAtInstallation);
            Assert.AreEqual(1, noticesAtFactory);
            Assert.AreEqual(firstRun ? 1 : 0, records.Count(record => record.Name == FIRST_RUN));
            Assert.AreEqual(firstRun ? 2 : 1, records.Length);
            bool validInstallation = hasId && (stability is "newly_saved" or "reused");
            foreach (CliTelemetryEvent record in records)
            {
                Assert.AreEqual(validInstallation ? stability : "unavailable", record.Properties["dab_installation_id_stability"]);
                Assert.AreEqual(validInstallation, record.Properties.ContainsKey("dab_installation_id"));
                if (validInstallation)
                {
                    Assert.AreEqual(_installationId.ToString("D"), record.Properties["dab_installation_id"]);
                }

                AssertNoApiIdentity(record);
            }

            AssertEnvelope(records, session.SessionId);
            AssertNoPrivateValues(records);
        }

        [TestMethod]
        public async Task EmptyInstallationGuidIsUnavailableButCommandTelemetryStillWorks()
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter, installation: new(Guid.Empty, "newly_saved"));
            Assert.IsTrue(session.IsEnabled);
            session.Complete("validate", "none", _noOptions, CliTelemetryOutcome.ValidationFailure, CliTelemetryFailureCategory.Configuration);
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);

            Assert.AreEqual(1, records.Length);
            CliTelemetryEvent command = Event(records, COMMAND);
            Assert.AreEqual("unavailable", command.Properties["dab_installation_id_stability"]);
            Assert.IsFalse(command.Properties.ContainsKey("dab_installation_id"));
            Assert.AreEqual("validation_failure", command.Properties["outcome"]);
        }

        [TestMethod]
        public async Task ConcurrentInvocationsHonorTheStoresSinglePublicationWinnerEvidence()
        {
            int installations = 0;
            int notices = 0;
            Task<CliTelemetryEvent[]>[] invocations = Enumerable.Range(0, 12).Select(_ => Task.Run(async () =>
            {
                CapturingExporter exporter = new();
                using CliTelemetrySession session = CliTelemetrySession.Create(() => exporter, true, new ManualTimeProvider(),
                    readEnvironmentVariable: _ => null,
                    showNotice: () => Interlocked.Increment(ref notices),
                    resolveInstallation: () => new(_installationId,
                        Interlocked.Increment(ref installations) == 1 ? "newly_saved" : "reused"));
                session.Complete("init", "none", _noOptions, CliTelemetryOutcome.Success);
                return await DrainAsync(session, exporter);
            })).ToArray();
            CliTelemetryEvent[][] runs = await Task.WhenAll(invocations).WaitAsync(_testTimeout);
            CliTelemetryEvent[] records = runs.SelectMany(run => run).ToArray();

            Assert.AreEqual(12, installations);
            Assert.AreEqual(12, notices, "A reused installation does not suppress the per-invocation notice.");
            Assert.AreEqual(1, records.Count(record => record.Name == FIRST_RUN));
            Assert.AreEqual(12, records.Count(record => record.Name == COMMAND));
            Assert.AreEqual(12, records.Select(record => record.SessionId).Distinct().Count());
            Assert.IsTrue(records.All(record => record.Properties["dab_installation_id"] == _installationId.ToString("D")));
        }

        [TestMethod]
        public async Task ContextRetainsSafeVersionPlatformCategoriesWithoutClaimingPackagingOrLauncher()
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CliTelemetrySession.Create(() => exporter, true, new ManualTimeProvider(),
                readEnvironmentVariable: name => name == ProductTelemetryPolicy.OPT_OUT_ENV_VAR ? null : SENTINEL,
                showNotice: () => { }, resolveInstallation: () => new(null, "unavailable"));
            session.Complete("appname", "none", _noOptions, CliTelemetryOutcome.Success);
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);
            ImmutableDictionary<string, string> properties = records.Single().Properties;

            foreach (string key in new[] { "dab_version", "dotnet_version" })
            {
                Assert.IsTrue(Version.TryParse(properties[key], out Version? version));
                Assert.IsNotNull(version);
                Assert.IsTrue(version.Build >= 0);
                Assert.AreEqual(-1, version.Revision);
            }

            foreach (string key in new[] { "os_family", "os_version", "architecture", "hosting", "container" })
            {
                Assert.IsTrue(properties.ContainsKey(key));
            }

            foreach (string key in new[] { "packaging", "install_channel", "distribution", "release_channel" })
            {
                Assert.AreEqual("unknown", properties[key]);
            }

            Assert.IsFalse(properties.ContainsKey("execution_mode"));
            Assert.IsFalse(properties.ContainsKey("launcher"));
            AssertNoPrivateValues(records);
        }

        [TestMethod]
        [DoNotParallelize]
        public async Task DefaultNoticeBypassesManagedStdoutAndErrorWriters()
        {
            TextWriter originalOut = Console.Out;
            TextWriter originalError = Console.Error;
            using StringWriter stdout = new(CultureInfo.InvariantCulture);
            using StringWriter stderr = new(CultureInfo.InvariantCulture);
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                CapturingExporter exporter = new();
                using CliTelemetrySession session = CliTelemetrySession.Create(() => exporter, true, new ManualTimeProvider(),
                    readEnvironmentVariable: _ => null, resolveInstallation: () => new(null, "unavailable"));
                Assert.IsTrue(session.IsEnabled);
                await session.StopAsync().WaitAsync(_testTimeout);
                Assert.AreEqual(string.Empty, stdout.ToString());
                Assert.AreEqual(string.Empty, stderr.ToString(), "The notice must use OpenStandardError, not a redirected host diagnostic writer.");
                Assert.AreEqual(0, exporter.Attempts.Count);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }

        [TestMethod]
        public async Task ConcurrentCompletionEmitsOnceAndDurationUsesOnlyMonotonicTime()
        {
            ManualTimeProvider clock = new();
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter, clock);
            clock.Advance(TimeSpan.FromTicks(123456));
            clock.ShiftWallClock(TimeSpan.FromDays(-3));
            TaskCompletionSource release = Signal();
            Task[] completions = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
            {
                await release.Task.WaitAsync(_testTimeout);
                session.Complete("validate", "none", _noOptions, CliTelemetryOutcome.Success);
            })).ToArray();
            release.TrySetResult();
            await Task.WhenAll(completions).WaitAsync(_testTimeout);
            clock.Advance(TimeSpan.FromDays(10));
            session.Complete(SENTINEL, SENTINEL, _noOptions, CliTelemetryOutcome.ExecutionFailure);
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);

            CliTelemetryEvent command = records.Single();
            Assert.AreEqual(COMMAND, command.Name);
            Assert.AreEqual("validate", command.Properties["command"]);
            Assert.AreEqual("success", command.Properties["outcome"]);
            Assert.AreEqual("12", command.Properties["duration_ms"]);
            Assert.AreEqual(_start.AddDays(-3).AddTicks(123456), command.OccurredAt);
            Assert.AreEqual(2, clock.TimestampCalls, "Only creation and the winning Complete may sample duration.");
            AssertEnvelope(records, session.SessionId);
        }

        [TestMethod]
        public async Task RegressingTimestampNeverProducesNegativeDuration()
        {
            ManualTimeProvider clock = new();
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter, clock);
            clock.Advance(TimeSpan.FromMilliseconds(-1));
            session.Complete("init", "none", _noOptions, CliTelemetryOutcome.Success);
            Assert.AreEqual("0", (await DrainAsync(session, exporter)).Single().Properties["duration_ms"]);
        }

        [DataTestMethod]
        [DataRow("init", "none", "init", "none")]
        [DataRow("add", "help", "add", "help")]
        [DataRow("update", "version", "update", "version")]
        [DataRow("start", "none", "start", "none")]
        [DataRow("validate", "none", "validate", "none")]
        [DataRow("export", "none", "export", "none")]
        [DataRow("add-telemetry", "none", "add-telemetry", "none")]
        [DataRow("configure", "none", "configure", "none")]
        [DataRow("auto-config", "none", "auto-config", "none")]
        [DataRow("auto-config-simulate", "none", "auto-config-simulate", "none")]
        [DataRow("appname", "none", "appname", "none")]
        [DataRow(SENTINEL, SENTINEL, "unknown", "none")]
        [DataRow("INIT", "HELP", "unknown", "none")]
        [DataRow(null, null, "unknown", "none")]
        public async Task CommandAndControlAreClosedCanonicalCategories(string? command, string? control, string expectedCommand, string expectedControl)
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter);
            session.Complete(command!, control!, _noOptions, CliTelemetryOutcome.Success);
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);
            Assert.AreEqual(expectedCommand, records.Single().Properties["command"]);
            Assert.AreEqual(expectedControl, records.Single().Properties["control"]);
            Assert.AreEqual("none", records.Single().Properties["subcommand"]);
            AssertNoPrivateValues(records);
        }

        [DataTestMethod]
        [DataRow((int)CliTelemetryOutcome.Success, (int)CliTelemetryFailureCategory.None, "success", "none")]
        [DataRow((int)CliTelemetryOutcome.ParseFailure, (int)CliTelemetryFailureCategory.Arguments, "parse_failure", "arguments")]
        [DataRow((int)CliTelemetryOutcome.ValidationFailure, (int)CliTelemetryFailureCategory.Configuration, "validation_failure", "configuration")]
        [DataRow((int)CliTelemetryOutcome.ExecutionFailure, (int)CliTelemetryFailureCategory.Storage, "execution_failure", "storage")]
        [DataRow((int)CliTelemetryOutcome.ExecutionFailure, (int)CliTelemetryFailureCategory.Initialization, "execution_failure", "initialization")]
        [DataRow((int)CliTelemetryOutcome.ExecutionFailure, (int)CliTelemetryFailureCategory.Execution, "execution_failure", "execution")]
        [DataRow((int)CliTelemetryOutcome.Canceled, (int)CliTelemetryFailureCategory.Canceled, "canceled", "canceled")]
        [DataRow((int)CliTelemetryOutcome.Unknown, (int)CliTelemetryFailureCategory.Unknown, "unknown", "unknown")]
        [DataRow(-1, int.MaxValue, "unknown", "unknown")]
        public async Task OutcomeAndFailureCategoriesUseOnlyFixedWireValues(int outcome,
            int category, string expectedOutcome, string expectedCategory)
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter);
            session.Complete("init", "none", _noOptions, (CliTelemetryOutcome)outcome, (CliTelemetryFailureCategory)category);
            CliTelemetryEvent command = (await DrainAsync(session, exporter)).Single();
            Assert.AreEqual(expectedOutcome, command.Properties["outcome"]);
            Assert.AreEqual(expectedCategory, command.Properties["failure_category"]);
        }

        [TestMethod]
        public async Task OnlyReviewedOptionKeysWithLiteralTrueEscapeTheSession()
        {
            // These are the grammar adapter's keys, NOT raw argv. Presence of a sensitive
            // option name approves neither its value nor any caller-invented option name.
            string[] approved =
            [
                "option_config", "option_connection_string", "option_runtime_embeddings_api_key",
                "option_otel_headers", "option_log_level", "option_mcp_stdio",
                "option_cosmosdb_nosql_database", "option_runtime_telemetry_azure_log_analytics_auth_dcr_immutable_id",
                "option_template_cache_ttl_seconds", "option_decode"
            ];
            ImmutableDictionary<string, string>.Builder options = _noOptions.ToBuilder();
            foreach (string key in approved)
            {
                options.Add(key, "true");
            }

            options.Add("option_" + SENTINEL.ToLowerInvariant(), "true");
            options.Add("option_" + new string('x', 101), "true");
            options.Add("option_output", SENTINEL);
            options.Add("option_source", new string('x', 101));
            options.Add("option_permissions", "false");
            options.Add("option_rest_enabled", "TRUE");
            options.Add("option_graphql_enabled", " true ");
            options.Add("option_database_type", "mssql");
            options.Add("option_source_type", null!);
            options.Add("option_CONFIG", "true");
            options.Add("option_../../" + SENTINEL, "true");
            options.Add("--config", "true");
            options.Add("command", SENTINEL);
            options.Add("dab_api_id", SENTINEL);
            options.Add("dab_config_epoch", SENTINEL);
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter);
            session.Complete("configure", "none", options.ToImmutable(), CliTelemetryOutcome.Success);
            options["option_config"] = SENTINEL;
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);
            CliTelemetryEvent command = records.Single();

            CollectionAssert.AreEquivalent(approved, command.Properties.Keys.Where(key => key.StartsWith("option_", StringComparison.Ordinal)).ToArray());
            Assert.IsTrue(command.Properties.Where(pair => pair.Key.StartsWith("option_", StringComparison.Ordinal)).All(pair => pair.Value == "true"));
            AssertNoApiIdentity(command);
            Assert.IsFalse(command.Properties.ContainsKey("dab_config_epoch"));
            AssertNoPrivateValues(records);
        }

        [DataTestMethod]
        [DataRow(96, true)]
        [DataRow(97, false)]
        public async Task OversizedOptionDictionaryIsOmittedWithoutLosingTheCommand(int count, bool retained)
        {
            ImmutableDictionary<string, string>.Builder options = _noOptions.ToBuilder();
            options.Add("option_config", "true");
            for (int index = 1; index < count; index++)
            {
                options.Add("option_unapproved_" + index.ToString(CultureInfo.InvariantCulture), "true");
            }

            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter);
            session.Complete("validate", "none", options.ToImmutable(), CliTelemetryOutcome.Success);
            CliTelemetryEvent command = (await DrainAsync(session, exporter)).Single();
            Assert.AreEqual(retained, command.Properties.ContainsKey("option_config"));
            Assert.AreEqual(retained ? 1 : 0, command.Properties.Keys.Count(key => key.StartsWith("option_", StringComparison.Ordinal)));
        }

        [TestMethod]
        public async Task FirstTypedFailureIsStableAndMarksAfterCompletionAreIgnored()
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter);
            Assert.IsFalse(session.HasFailure);
            Assert.AreEqual(CliTelemetryOutcome.Unknown, session.FailureOutcome);
            Assert.AreEqual(CliTelemetryFailureCategory.None, session.FailureCategory);
            session.MarkFailure(CliTelemetryOutcome.Success, CliTelemetryFailureCategory.None);
            Assert.IsFalse(session.HasFailure);
            session.MarkFailure(CliTelemetryOutcome.ValidationFailure, CliTelemetryFailureCategory.Configuration);
            session.MarkFailure(CliTelemetryOutcome.ExecutionFailure, CliTelemetryFailureCategory.Execution);
            Assert.IsTrue(session.HasFailure);
            Assert.AreEqual(CliTelemetryOutcome.ValidationFailure, session.FailureOutcome);
            Assert.AreEqual(CliTelemetryFailureCategory.Configuration, session.FailureCategory);
            Assert.AreEqual(0, exporter.Attempts.Count);

            session.Complete("validate", "none", _noOptions, session.FailureOutcome, session.FailureCategory);
            session.MarkFailure(CliTelemetryOutcome.Canceled, CliTelemetryFailureCategory.Canceled);
            CliTelemetryEvent command = (await DrainAsync(session, exporter)).Single();
            Assert.AreEqual("validation_failure", command.Properties["outcome"]);
            Assert.AreEqual("configuration", command.Properties["failure_category"]);
            Assert.AreEqual(CliTelemetryOutcome.ValidationFailure, session.FailureOutcome);
        }

        [TestMethod]
        public async Task InvalidFailureEnumsBecomeUnknownAndCompleteHonorsItsExplicitClassification()
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter);
            session.MarkFailure((CliTelemetryOutcome)int.MaxValue, (CliTelemetryFailureCategory)(-1));
            Assert.IsTrue(session.HasFailure);
            Assert.AreEqual(CliTelemetryOutcome.Unknown, session.FailureOutcome);
            Assert.AreEqual(CliTelemetryFailureCategory.Unknown, session.FailureCategory);
            session.Complete("start", "none", _noOptions, CliTelemetryOutcome.Canceled, CliTelemetryFailureCategory.Canceled);
            Assert.AreEqual("canceled", (await DrainAsync(session, exporter)).Single().Properties["outcome"]);
        }

        [TestMethod]
        public async Task LookupIsReadOnlyDeduplicatedAndUsesOnlyTheExactResolvedRoot()
        {
            ConcurrentQueue<string?> lookedUp = new();
            int creates = 0;
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter,
                createIdentity: _ => { creates++; return new(_otherApiId, "newly_saved"); },
                lookupIdentity: path => { lookedUp.Enqueue(path); return new(_apiId, "reused"); });
            session.ObserveConfiguration(ROOT);
            session.ObserveConfiguration(ROOT);
            Assert.AreEqual(0, exporter.Attempts.Count);
            session.Complete("validate", "none", _noOptions, CliTelemetryOutcome.Success);
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);

            CollectionAssert.AreEqual(new[] { ROOT }, lookedUp.ToArray());
            Assert.AreEqual(0, creates);
            AssertApiIdentity(records.Single(), _apiId, "reused");
            AssertNoPrivateValues(records);
        }

        [TestMethod]
        public async Task MissingLookupDoesNotCreateIdentityAndCanBeFilledOnlyByEligibleCreation()
        {
            int lookups = 0;
            int creates = 0;
            EngineTelemetryIdentity ephemeral = new(_apiId, "ephemeral");
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter,
                createIdentity: path => { Assert.AreEqual(ROOT, path); creates++; return ephemeral; },
                lookupIdentity: path => { Assert.AreEqual(ROOT, path); lookups++; return null; });

            session.ConfigurationCreated(null);
            session.ConfigurationCreated(string.Empty);
            session.ConfigurationCreated(" \t ");
            Assert.AreEqual(0, creates);
            session.ObserveConfiguration(ROOT);
            Assert.AreEqual(1, lookups);
            Assert.AreEqual(0, creates);
            session.ConfigurationCreated(ROOT);
            session.ConfigurationCreated(ROOT);
            session.ObserveConfiguration(ROOT);
            ProductTelemetryLaunchContext? launch = session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.ExportGraphQL);
            Assert.IsNotNull(launch);
            Assert.AreSame(ephemeral, launch.ApiIdentity, "The bridge must retain a same-run ephemeral result, not resolve again in the engine.");
            session.Complete("export", "none", _noOptions, CliTelemetryOutcome.Success);
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);

            Assert.AreEqual(1, creates);
            Assert.AreEqual(1, lookups);
            Assert.IsTrue(records.All(record => record.Name is LAUNCH or COMMAND));
            AssertApiIdentity(Event(records, COMMAND), _apiId, "ephemeral");
            AssertApiIdentity(Event(records, LAUNCH), _apiId, "ephemeral");
            AssertNoPrivateValues(records);
        }

        [TestMethod]
        public async Task ReadOnlyCommandWithNoSavedIdentityDoesNotInventAnEphemeralOne()
        {
            int creates = 0;
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter,
                createIdentity: _ => { creates++; return new(_apiId, "ephemeral"); });
            session.ObserveConfiguration(ROOT);
            session.Complete("update", "none", _noOptions, CliTelemetryOutcome.Success);
            CliTelemetryEvent command = (await DrainAsync(session, exporter)).Single();
            Assert.AreEqual(0, creates);
            AssertNoApiIdentity(command);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" ")]
        [DataRow(OTHER_ROOT)]
        public async Task UnknownOrDifferentObservedTargetsPermanentlySuppressCommandLinkage(string? other)
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter, lookupIdentity: _ => new(_apiId, "reused"));
            session.ObserveConfiguration(ROOT);
            session.ObserveConfiguration(other);
            session.ObserveConfiguration(ROOT);
            session.ConfigurationCreated(ROOT);
            ProductTelemetryLaunchContext? launch = session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb);
            Assert.IsNotNull(launch);
            session.Complete("start", "none", _noOptions, CliTelemetryOutcome.Success);
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);

            AssertNoApiIdentity(Event(records, COMMAND));
            AssertApiIdentity(Event(records, LAUNCH), _apiId, "ephemeral");
            AssertNoPrivateValues(records);
        }

        [DataTestMethod]
        [DataRow((int)CliTelemetryLaunchSource.StartWeb, "start_web")]
        [DataRow((int)CliTelemetryLaunchSource.StartStdio, "start_stdio")]
        [DataRow((int)CliTelemetryLaunchSource.ExportGraphQL, "export_graphql")]
        public async Task ActualLaunchEnqueuesAnImmutableCorrelatedBridgeBeforeReturning(int launchSource, string expectedSource)
        {
            CliTelemetryLaunchSource source = (CliTelemetryLaunchSource)launchSource;
            ManualTimeProvider clock = new();
            CapturingExporter exporter = new();
            CliTelemetryInstallation installation = new(_installationId, "reused");
            EngineTelemetryIdentity identity = new(_apiId, "ephemeral");
            int creates = 0;
            using CliTelemetrySession session = CreateSession(exporter, clock, installation,
                createIdentity: path => { Assert.AreEqual(ROOT, path); creates++; return identity; });
            clock.Advance(TimeSpan.FromMilliseconds(7));
            ProductTelemetryLaunchContext? context = session.BeginEngineLaunch(ROOT, source);
            Assert.IsNotNull(context);
            Assert.AreNotEqual(Guid.Empty, context.EngineSessionId);
            Assert.AreNotEqual(session.SessionId, context.EngineSessionId);
            Assert.AreEqual(session.SessionId, context.ParentCliSessionId);
            Assert.AreSame(installation, context.Installation);
            Assert.AreSame(identity, context.ApiIdentity);
            Assert.AreEqual(source, context.Source);
            Assert.AreEqual(1, clock.TimestampCalls, "Launch does not measure command duration.");

            // Completion happens immediately on return. FIFO sequence proves the launch event
            // was admitted first without requiring transmission to finish before the handoff.
            session.Complete("start", "none", _noOptions, CliTelemetryOutcome.Success);
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);
            session.Dispose();
            CollectionAssert.AreEqual(new[] { LAUNCH, COMMAND }, records.Select(record => record.Name).ToArray());
            Assert.AreEqual(expectedSource, records[0].Properties["launch_source"]);
            Assert.AreEqual(context.EngineSessionId.ToString("D"), records[0].Properties["dab_launched_engine_session_id"]);
            Assert.AreEqual(session.SessionId.ToString("D"), records[0].Properties["dab_parent_cli_session_id"]);
            AssertApiIdentity(records[0], _apiId, "ephemeral");
            AssertApiIdentity(records[1], _apiId, "ephemeral");
            Assert.AreEqual("7", records[1].Properties["duration_ms"]);
            Assert.AreEqual(1, creates);
            Assert.AreSame(identity, context.ApiIdentity, "CLI disposal has no authority over the immutable engine handoff.");
            AssertEnvelope(records, session.SessionId);
            AssertNoPrivateValues(records);
            Assert.IsFalse(JsonSerializer.Serialize(context).Contains(SENTINEL, StringComparison.Ordinal));
        }

        [TestMethod]
        public void LaunchContextHasExactlyFiveDataPropertiesAndNoStaticOrLifetimeState()
        {
            Type context = typeof(ProductTelemetryLaunchContext);
            Assert.IsTrue(context.IsSealed);
            Assert.IsFalse(context.IsPublic);
            CollectionAssert.AreEquivalent(new[]
            {
                nameof(ProductTelemetryLaunchContext.EngineSessionId), nameof(ProductTelemetryLaunchContext.ParentCliSessionId),
                nameof(ProductTelemetryLaunchContext.Installation), nameof(ProductTelemetryLaunchContext.ApiIdentity),
                nameof(ProductTelemetryLaunchContext.Source)
            }, context.GetProperties().Select(property => property.Name).ToArray());
            Assert.AreEqual(0, context.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static).Length);
            Assert.IsFalse(typeof(IDisposable).IsAssignableFrom(context));
            Assert.IsFalse(context.GetProperties().Any(property => property.PropertyType == typeof(string)
                || typeof(Delegate).IsAssignableFrom(property.PropertyType) || property.PropertyType == typeof(CancellationToken)));
        }

        [TestMethod]
        public async Task DifferentLaunchTargetsKeepTheirOwnIdentitiesButNeverPickAnArbitraryCommandId()
        {
            int creates = 0;
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter, createIdentity: path =>
            {
                creates++;
                return path == ROOT ? new(_apiId, "ephemeral") : new(_otherApiId, "reused");
            });
            ProductTelemetryLaunchContext? first = session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb);
            ProductTelemetryLaunchContext? second = session.BeginEngineLaunch(OTHER_ROOT, CliTelemetryLaunchSource.ExportGraphQL);
            ProductTelemetryLaunchContext? third = session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartStdio);
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);
            Assert.IsNotNull(third);
            Assert.AreEqual(_apiId, first.ApiIdentity!.ApiId);
            Assert.AreEqual(_otherApiId, second.ApiIdentity!.ApiId);
            Assert.AreSame(first.ApiIdentity, third.ApiIdentity);
            session.Complete("export", "none", _noOptions, CliTelemetryOutcome.Success);
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);

            Assert.AreEqual(2, creates);
            Assert.AreEqual(3, new[] { first.EngineSessionId, second.EngineSessionId, third.EngineSessionId }.Distinct().Count());
            AssertApiIdentity(records[0], _apiId, "ephemeral");
            AssertApiIdentity(records[1], _otherApiId, "reused");
            AssertApiIdentity(records[2], _apiId, "ephemeral");
            AssertNoApiIdentity(Event(records, COMMAND));
            AssertEnvelope(records, session.SessionId);
        }

        [TestMethod]
        public async Task ConcurrentLaunchesAreBoundedAndEveryAcceptedLaunchGetsANewEngineSession()
        {
            int creates = 0;
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter, createIdentity: _ =>
            {
                Interlocked.Increment(ref creates);
                return new(_apiId, "ephemeral");
            });
            TaskCompletionSource release = Signal();
            Task<ProductTelemetryLaunchContext?>[] pending = Enumerable.Range(0, 48).Select(_ => Task.Run(async () =>
            {
                await release.Task.WaitAsync(_testTimeout);
                return session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb);
            })).ToArray();
            release.TrySetResult();
            ProductTelemetryLaunchContext?[] results = await Task.WhenAll(pending).WaitAsync(_testTimeout);
            session.Complete("start", "none", _noOptions, CliTelemetryOutcome.Success);
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);

            Assert.AreEqual(16, results.Count(result => result is not null));
            Assert.AreEqual(16, results.Where(result => result is not null).Select(result => result!.EngineSessionId).Distinct().Count());
            Assert.AreEqual(16, records.Count(record => record.Name == LAUNCH));
            Assert.AreEqual(1, creates, "Same-target Lazy resolution must run once even across simultaneous handoffs.");
            Assert.AreEqual(17, records.Length);
            AssertEnvelope(records, session.SessionId);
        }

        [TestMethod]
        public async Task CappedLaunchStillPreservesDifferentTargetAmbiguityWithoutMoreIdentityIo()
        {
            int creates = 0;
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter, createIdentity: _ =>
            {
                creates++;
                return new(_apiId, "ephemeral");
            });
            for (int index = 0; index < 16; index++)
            {
                Assert.IsNotNull(session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb));
            }

            Assert.IsNull(session.BeginEngineLaunch(OTHER_ROOT, CliTelemetryLaunchSource.ExportGraphQL));
            session.Complete("export", "none", _noOptions, CliTelemetryOutcome.Success);
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);
            Assert.AreEqual(1, creates);
            Assert.AreEqual(16, records.Count(record => record.Name == LAUNCH));
            AssertNoApiIdentity(Event(records, COMMAND));
        }

        [TestMethod]
        public async Task InvalidSourceAndLateLaunchDoNotReadIdentityOrEmitIntent()
        {
            int creates = 0;
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter, createIdentity: _ =>
            {
                creates++;
                return new(_apiId, "ephemeral");
            });
            Assert.IsNull(session.BeginEngineLaunch(ROOT, (CliTelemetryLaunchSource)int.MaxValue));
            session.Complete("export", "none", _noOptions, CliTelemetryOutcome.Success);
            Assert.IsNull(session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.ExportGraphQL));
            session.ConfigurationCreated(ROOT);
            session.ObserveConfiguration(ROOT);
            session.MarkFailure(CliTelemetryOutcome.ExecutionFailure, CliTelemetryFailureCategory.Initialization);
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);
            Assert.AreEqual(0, creates);
            Assert.IsFalse(session.HasFailure);
            Assert.AreEqual(COMMAND, records.Single().Name);
        }

        [DataTestMethod]
        [DataRow("observe")]
        [DataRow("created")]
        [DataRow("launch")]
        public async Task IdentityCallbackExceptionsFailClosedWithoutApplicationErrors(string operation)
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter,
                createIdentity: _ => throw new IOException(SENTINEL),
                lookupIdentity: _ => throw new IOException(SENTINEL));
            Assert.IsNull(PerformIdentityOperation(session, operation));
            Assert.IsFalse(session.IsEnabled);
            session.Complete("start", "none", _noOptions, CliTelemetryOutcome.Success);
            Assert.AreEqual(0, (await DrainAsync(session, exporter)).Length);
            Assert.AreEqual(0, exporter.Attempts.Count);
        }

        [DataTestMethod]
        [DataRow("observe", "disable")]
        [DataRow("created", "disable")]
        [DataRow("launch", "disable")]
        [DataRow("observe", "stop")]
        [DataRow("created", "stop")]
        [DataRow("launch", "stop")]
        [DataRow("observe", "cancel")]
        [DataRow("created", "cancel")]
        [DataRow("launch", "cancel")]
        [DataRow("observe", "complete")]
        [DataRow("created", "complete")]
        [DataRow("launch", "complete")]
        public async Task PendingIdentityIoCannotHoldTheGateOrReviveAClosedInvocation(string operation, string finish)
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            EngineTelemetryIdentity Resolve(string? path)
            {
                Assert.AreEqual(ROOT, path);
                entered.TrySetResult();
                release.Task.WaitAsync(_testTimeout).GetAwaiter().GetResult();
                return new(_apiId, "ephemeral");
            }

            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter, createIdentity: Resolve, lookupIdentity: Resolve);
            Task<ProductTelemetryLaunchContext?> pending = Task.Run(() => PerformIdentityOperation(session, operation));
            try
            {
                await entered.Task.WaitAsync(_testTimeout);
                await Task.Run(async () =>
                {
                    if (finish == "complete")
                    {
                        session.Complete("export", "none", _noOptions, CliTelemetryOutcome.Success);
                    }
                    else if (finish == "disable")
                    {
                        session.Disable();
                    }
                    else
                    {
                        await session.StopAsync(new CancellationToken(canceled: finish == "cancel"));
                    }
                }).WaitAsync(_testTimeout);
                Assert.IsFalse(pending.IsCompleted, "Stop/disable/completion must not wait for the blocked identity callback.");
            }
            finally
            {
                release.TrySetResult();
                Assert.IsNull(await pending.WaitAsync(_testTimeout));
            }

            session.Complete("export", "none", _noOptions, CliTelemetryOutcome.ExecutionFailure);
            Assert.IsNull(session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.ExportGraphQL));
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);
            Assert.AreEqual(finish == "complete" ? 1 : 0, records.Length);
            if (finish == "complete")
            {
                AssertNoApiIdentity(Event(records, COMMAND));
            }

            Assert.IsFalse(session.IsEnabled);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task LateSameRootLookupCannotReplaceTheLaunchIdentity(bool returnStaleIdentity)
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter, lookupIdentity: _ =>
            {
                entered.TrySetResult();
                release.Task.WaitAsync(_testTimeout).GetAwaiter().GetResult();
                return returnStaleIdentity ? new(_otherApiId, "reused") : null;
            });
            Task lookup = Task.Run(() => session.ObserveConfiguration(ROOT));
            ProductTelemetryLaunchContext? launch;
            try
            {
                await entered.Task.WaitAsync(_testTimeout);
                launch = await Task.Run(() => session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.ExportGraphQL)).WaitAsync(_testTimeout);
                Assert.IsNotNull(launch);
            }
            finally
            {
                release.TrySetResult();
                await lookup.WaitAsync(_testTimeout);
            }

            session.Complete("export", "none", _noOptions, CliTelemetryOutcome.Success);
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);
            Assert.AreEqual(_apiId, launch.ApiIdentity!.ApiId);
            AssertApiIdentity(Event(records, COMMAND), _apiId, "ephemeral");
        }

        [TestMethod]
        public async Task ConcurrentDifferentObservationPreservesAmbiguityWhenTheOlderLookupCompletes()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter, lookupIdentity: path =>
            {
                Assert.AreEqual(ROOT, path);
                entered.TrySetResult();
                release.Task.WaitAsync(_testTimeout).GetAwaiter().GetResult();
                return new(_apiId, "reused");
            });
            Task lookup = Task.Run(() => session.ObserveConfiguration(ROOT));
            try
            {
                await entered.Task.WaitAsync(_testTimeout);
                await Task.Run(() => session.ObserveConfiguration(OTHER_ROOT)).WaitAsync(_testTimeout);
            }
            finally
            {
                release.TrySetResult();
                await lookup.WaitAsync(_testTimeout);
            }

            session.Complete("export", "none", _noOptions, CliTelemetryOutcome.Success);
            AssertNoApiIdentity((await DrainAsync(session, exporter)).Single());
        }

        [TestMethod]
        public async Task PendingDifferentExportLaunchSuppressesPreviousLinkageBeforeCommandCompletion()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter,
                lookupIdentity: _ => new(_apiId, "reused"),
                createIdentity: path =>
                {
                    Assert.AreEqual(OTHER_ROOT, path);
                    entered.TrySetResult();
                    release.Task.WaitAsync(_testTimeout).GetAwaiter().GetResult();
                    return new(_otherApiId, "ephemeral");
                });
            session.ObserveConfiguration(ROOT);
            Task<ProductTelemetryLaunchContext?> pending = Task.Run(() => session.BeginEngineLaunch(OTHER_ROOT, CliTelemetryLaunchSource.ExportGraphQL));
            try
            {
                await entered.Task.WaitAsync(_testTimeout);
                await Task.Run(() => session.Complete("export", "none", _noOptions, CliTelemetryOutcome.Success)).WaitAsync(_testTimeout);
                Assert.IsFalse(pending.IsCompleted);
            }
            finally
            {
                release.TrySetResult();
                Assert.IsNull(await pending.WaitAsync(_testTimeout), "A launch that finishes after Complete must not report late intent.");
            }

            CliTelemetryEvent command = (await DrainAsync(session, exporter)).Single();
            Assert.AreEqual(COMMAND, command.Name);
            AssertNoApiIdentity(command);
        }

        [DataTestMethod]
        [DataRow("empty")]
        [DataRow("unknown_stability")]
        [DataRow("null")]
        public async Task InvalidIdentityEvidenceNeverEntersACommandOrBridge(string invalid)
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter, createIdentity: _ => invalid switch
            {
                "empty" => new(Guid.Empty, "newly_saved"),
                "unknown_stability" => new(_apiId, SENTINEL),
                _ => null!
            });
            ProductTelemetryLaunchContext? launch = session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb);
            Assert.IsNotNull(launch);
            Assert.IsNull(launch.ApiIdentity);
            session.Complete("start", "none", _noOptions, CliTelemetryOutcome.Success);
            CliTelemetryEvent[] records = await DrainAsync(session, exporter);
            Assert.IsTrue(records.Length == 2);
            foreach (CliTelemetryEvent record in records)
            {
                AssertNoApiIdentity(record);
            }

            AssertNoPrivateValues(records);
        }

        [DataTestMethod]
        [DataRow("stop")]
        [DataRow("cancel")]
        [DataRow("disable")]
        [DataRow("dispose")]
        public async Task ClosingWithoutCompleteNeverSynthesizesACommandOrFinalEvent(string mode)
        {
            CapturingExporter exporter = new();
            ManualTimeProvider clock = new();
            using CliTelemetrySession session = CreateSession(exporter, clock);
            session.MarkFailure(CliTelemetryOutcome.Canceled, CliTelemetryFailureCategory.Canceled);
            await CloseAsync(session, mode);
            session.Complete("init", "none", _noOptions, CliTelemetryOutcome.Canceled);
            Assert.IsNull(session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb));
            await session.StopAsync().WaitAsync(_testTimeout);
            session.Dispose();

            Assert.AreEqual(0, exporter.Attempts.Count);
            Assert.AreEqual(0, exporter.DisposeCalls, "An exporter was never acquired.");
            Assert.AreEqual(1, clock.TimestampCalls);
            Assert.IsFalse(session.IsEnabled);
        }

        [DataTestMethod]
        [DataRow("cancel")]
        [DataRow("disable")]
        [DataRow("dispose")]
        public async Task DisableAndCancellationDiscardQueuedEventsWithoutAFlushOrFinalRecord(string mode)
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            CapturingExporter exporter = new(async (_, _) =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false); // Deliberately ignore cancellation while blocked.
                return true;
            });
            using CliTelemetrySession session = CreateSession(exporter);
            try
            {
                Assert.IsNotNull(session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb));
                await entered.Task.WaitAsync(_testTimeout);
                Assert.IsNotNull(session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartStdio));
                session.Complete("start", "none", _noOptions, CliTelemetryOutcome.Success);
                await CloseAsync(session, mode).WaitAsync(_testTimeout);
                Assert.IsFalse(session.IsEnabled);
                Assert.IsTrue(exporter.Tokens.Single().IsCancellationRequested);
                Assert.IsNull(session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb));
            }
            finally
            {
                release.TrySetResult();
                await exporter.Disposed.Task.WaitAsync(_testTimeout);
            }

            Assert.AreEqual(1, exporter.Attempts.Count, "Queued launches/command must be discarded rather than drained.");
            Assert.AreEqual(0, exporter.Records.Count);
            Assert.AreEqual(1, exporter.DisposeCalls);
        }

        [TestMethod]
        public async Task GracefulStopSharesOneTwoSecondDeadlineEvenIfTheExporterNeverHonorsCancellation()
        {
            ManualTimeProvider clock = new();
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            CapturingExporter exporter = new(async (_, _) =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false);
                return true;
            });
            using CliTelemetrySession session = CreateSession(exporter, clock);
            try
            {
                Assert.IsNotNull(session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb));
                await entered.Task.WaitAsync(_testTimeout);
                Task firstStop = session.StopAsync();
                Assert.AreSame(firstStop, session.StopAsync());
                ManualTimer deadline = await clock.NextTimerAsync();
                Assert.AreEqual(TimeSpan.FromSeconds(2), deadline.DueTime);
                Assert.IsFalse(firstStop.IsCompleted);
                deadline.Fire();
                await firstStop.WaitAsync(_testTimeout);

                Assert.IsTrue(exporter.Tokens.Single().IsCancellationRequested);
                Assert.IsFalse(exporter.Disposed.Task.IsCompleted, "The stop deadline must not wait for stuck exporter cleanup.");
                Assert.IsNull(session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb));
                Assert.AreEqual(1, clock.TimestampCalls);
            }
            finally
            {
                release.TrySetResult();
                await exporter.Disposed.Task.WaitAsync(_testTimeout);
            }

            Assert.AreEqual(1, exporter.Attempts.Count);
            Assert.AreEqual(0, exporter.Records.Count);
        }

        [TestMethod]
        public async Task LaterStopCallerCanCancelThePendingGracefulDrain()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            CapturingExporter exporter = new(async (_, token) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token).ConfigureAwait(false);
                return true;
            });
            using CliTelemetrySession session = CreateSession(exporter);
            try
            {
                session.Complete("start", "none", _noOptions, CliTelemetryOutcome.Success);
                await entered.Task.WaitAsync(_testTimeout);
                Task firstStop = session.StopAsync();
                using CancellationTokenSource cancellation = new();
                Task secondStop = session.StopAsync(cancellation.Token);
                cancellation.Cancel();
                await secondStop.WaitAsync(_testTimeout);
                await firstStop.WaitAsync(_testTimeout);
                Assert.IsTrue(exporter.Tokens.Single().IsCancellationRequested);
                Assert.AreEqual(0, exporter.Records.Count);
            }
            finally
            {
                release.TrySetResult();
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task CompletionClockFailuresAreTelemetryOnlyAndFailClosed(bool failUtc)
        {
            ManualTimeProvider clock = new();
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter, clock);
            clock.ThrowTimestamp = !failUtc;
            clock.ThrowUtc = failUtc;
            session.Complete("init", "none", _noOptions, CliTelemetryOutcome.Success);
            Assert.IsFalse(session.IsEnabled);
            Assert.AreEqual(0, (await DrainAsync(session, exporter)).Length);
            Assert.AreEqual(0, exporter.Attempts.Count);
        }

        [TestMethod]
        public async Task FactoryFailureAndCanceledRetryNeverEscapeIntoTheCommand()
        {
            ManualTimeProvider clock = new();
            int factories = 0;
            using CliTelemetrySession session = CliTelemetrySession.Create(() =>
            {
                Interlocked.Increment(ref factories);
                throw new InvalidOperationException(SENTINEL);
            }, true, clock, readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveInstallation: () => new(null, "unavailable"));
            session.Complete("init", "none", _noOptions, CliTelemetryOutcome.Success);
            ManualTimer retry = await clock.NextTimerAsync();
            Assert.AreEqual(TimeSpan.FromMilliseconds(100), retry.DueTime);
            await session.StopAsync(new CancellationToken(canceled: true)).WaitAsync(_testTimeout);
            await session.StopAsync().WaitAsync(_testTimeout);
            Assert.AreEqual(1, factories);
            Assert.IsFalse(session.IsEnabled);
        }

        [TestMethod]
        public async Task SenderDropCountIsAnImmutableInvariantCultureSnapshot()
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter);
            // Generic delivery owns counter behavior; inject evidence here solely to verify
            // the session snapshots that counter instead of retaining mutable sender state.
            FieldInfo deliveryField = typeof(CliTelemetrySession).GetField("_delivery", BindingFlags.NonPublic | BindingFlags.Instance)!;
            ProductTelemetryDelivery<CliTelemetryEvent> delivery = (ProductTelemetryDelivery<CliTelemetryEvent>)deliveryField.GetValue(session)!;
            FieldInfo counter = typeof(ProductTelemetryDelivery<CliTelemetryEvent>).GetField("_droppedEvents", BindingFlags.NonPublic | BindingFlags.Instance)!;
            counter.SetValue(delivery, long.MaxValue);
            session.Complete("validate", "none", _noOptions, CliTelemetryOutcome.Success);
            counter.SetValue(delivery, 0L);
            CliTelemetryEvent command = (await DrainAsync(session, exporter)).Single();
            Assert.AreEqual(long.MaxValue.ToString(CultureInfo.InvariantCulture), command.Properties["sender_dropped_events"]);
        }

        private static CliTelemetrySession CreateSession(CapturingExporter exporter, ManualTimeProvider? clock = null,
            CliTelemetryInstallation? installation = null, Func<string?, EngineTelemetryIdentity>? createIdentity = null,
            Func<string?, EngineTelemetryIdentity?>? lookupIdentity = null)
            => CliTelemetrySession.Create(() => exporter, true, clock ?? new ManualTimeProvider(),
                readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveInstallation: () => installation ?? new(_installationId, "reused"),
                createIdentity: createIdentity ?? (_ => new(_apiId, "ephemeral")),
                lookupIdentity: lookupIdentity ?? (_ => null));

        private static ProductTelemetryLaunchContext? PerformIdentityOperation(CliTelemetrySession session, string operation)
        {
            if (operation == "observe")
            {
                session.ObserveConfiguration(ROOT);
                return null;
            }

            if (operation == "created")
            {
                session.ConfigurationCreated(ROOT);
                return null;
            }

            return session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.ExportGraphQL);
        }

        private static Task CloseAsync(CliTelemetrySession session, string mode)
        {
            if (mode == "stop" || mode == "cancel")
            {
                return session.StopAsync(new CancellationToken(canceled: mode == "cancel"));
            }

            if (mode == "dispose")
            {
                session.Dispose();
            }
            else
            {
                session.Disable();
            }

            return Task.CompletedTask;
        }

        private static async Task ExerciseDisabledAsync(CliTelemetrySession session)
        {
            Assert.IsFalse(session.IsEnabled);
            session.ObserveConfiguration(ROOT);
            session.ObserveConfiguration(null);
            session.ConfigurationCreated(ROOT);
            Assert.IsNull(session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb));
            session.MarkFailure(CliTelemetryOutcome.ExecutionFailure, CliTelemetryFailureCategory.Storage);
            session.Complete(SENTINEL, SENTINEL, null!, CliTelemetryOutcome.Unknown);
            await session.StopAsync().WaitAsync(_testTimeout);
            await session.StopAsync(new CancellationToken(canceled: true)).WaitAsync(_testTimeout);
            session.Disable();
            session.Dispose();
            Assert.IsFalse(session.HasFailure);
            Assert.AreEqual(CliTelemetryOutcome.Unknown, session.FailureOutcome);
            Assert.AreEqual(CliTelemetryFailureCategory.None, session.FailureCategory);
        }

        private static async Task<CliTelemetryEvent[]> DrainAsync(CliTelemetrySession session, CapturingExporter exporter)
        {
            await session.StopAsync().WaitAsync(_testTimeout);
            return exporter.Records.ToArray();
        }

        private static CliTelemetryEvent Event(IEnumerable<CliTelemetryEvent> records, string name)
            => records.Single(record => record.Name == name);

        private static void AssertNoApiIdentity(CliTelemetryEvent record)
        {
            Assert.IsFalse(record.Properties.ContainsKey("dab_api_id"));
            Assert.IsFalse(record.Properties.ContainsKey("dab_api_id_stability"));
        }

        private static void AssertApiIdentity(CliTelemetryEvent record, Guid id, string stability)
        {
            Assert.AreEqual(id.ToString("D"), record.Properties["dab_api_id"]);
            Assert.AreEqual(stability, record.Properties["dab_api_id_stability"]);
        }

        private static void AssertEnvelope(CliTelemetryEvent[] records, Guid sessionId)
        {
            Assert.AreNotEqual(Guid.Empty, sessionId);
            Assert.AreEqual(records.Length, records.Select(record => record.EventId).Distinct().Count());
            for (int index = 0; index < records.Length; index++)
            {
                CliTelemetryEvent record = records[index];
                Assert.AreNotEqual(Guid.Empty, record.EventId);
                Assert.AreEqual(sessionId, record.SessionId);
                Assert.AreEqual(index + 1L, record.Sequence);
                Assert.IsTrue(record.IsSynthetic);
                Assert.AreEqual(TimeSpan.Zero, record.OccurredAt.Offset);
                Assert.AreEqual("0", record.Properties["sender_dropped_events"]);
                Assert.IsFalse(record.Properties.ContainsKey("dab_config_epoch"));
                Assert.IsFalse(record.Properties.ContainsKey("execution_mode"));
                Assert.IsFalse(record.Properties.ContainsKey("launcher"));
            }
        }

        private static void AssertNoPrivateValues(IEnumerable<CliTelemetryEvent> records)
            => Assert.IsFalse(JsonSerializer.Serialize(records).Contains(SENTINEL, StringComparison.OrdinalIgnoreCase));

        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class CapturingExporter : IProductTelemetryExporter<IProductTelemetryEvent>
        {
            private readonly Func<CliTelemetryEvent, CancellationToken, ValueTask<bool>>? _export;
            private int _disposeCalls;

            public CapturingExporter(Func<CliTelemetryEvent, CancellationToken, ValueTask<bool>>? export = null)
            {
                _export = export;
            }

            public ConcurrentQueue<CliTelemetryEvent> Attempts { get; } = new();
            public ConcurrentQueue<CliTelemetryEvent> Records { get; } = new();
            public ConcurrentQueue<CancellationToken> Tokens { get; } = new();
            public TaskCompletionSource Disposed { get; } = Signal();
            public int DisposeCalls => Volatile.Read(ref _disposeCalls);

            public async ValueTask<bool> ExportAsync(IProductTelemetryEvent record, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CliTelemetryEvent cli = (CliTelemetryEvent)record;
                Attempts.Enqueue(cli);
                Tokens.Enqueue(cancellationToken);
                bool exported = _export is null || await _export(cli, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (exported)
                {
                    Records.Enqueue(cli);
                }

                return exported;
            }

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCalls);
                Disposed.TrySetResult();
            }
        }

        /// <summary>Monotonic/UTC clocks and one-shot timer barriers, with no sleeps or timer threads.</summary>
        private sealed class ManualTimeProvider : TimeProvider
        {
            private readonly Channel<ManualTimer> _timers = Channel.CreateUnbounded<ManualTimer>(
                new UnboundedChannelOptions { AllowSynchronousContinuations = false });
            private long _elapsedTicks;
            private long _wallOffset;
            private int _timestampCalls;

            public bool ThrowTimestamp { get; set; }
            public bool ThrowUtc { get; set; }
            public int TimestampCalls => Volatile.Read(ref _timestampCalls);
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            public override long GetTimestamp()
            {
                Interlocked.Increment(ref _timestampCalls);
                return ThrowTimestamp ? throw new InvalidOperationException(SENTINEL) : Interlocked.Read(ref _elapsedTicks);
            }

            public override DateTimeOffset GetUtcNow()
                => ThrowUtc ? throw new InvalidOperationException(SENTINEL)
                    : _start.AddTicks(Interlocked.Read(ref _elapsedTicks) + Interlocked.Read(ref _wallOffset));

            public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _elapsedTicks, elapsed.Ticks);
            public void ShiftWallClock(TimeSpan offset) => Interlocked.Add(ref _wallOffset, offset.Ticks);

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                if (period != Timeout.InfiniteTimeSpan)
                {
                    throw new InvalidOperationException("CLI telemetry must not create periodic timers.");
                }

                ManualTimer timer = new(callback, state, dueTime);
                _timers.Writer.TryWrite(timer);
                return timer;
            }

            public Task<ManualTimer> NextTimerAsync() => _timers.Reader.ReadAsync().AsTask().WaitAsync(_testTimeout);
        }

        private sealed class ManualTimer : ITimer
        {
            private readonly TimerCallback _callback;
            private readonly object? _state;
            private int _finished;

            public ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime)
            {
                _callback = callback;
                _state = state;
                DueTime = dueTime;
            }

            public TimeSpan DueTime { get; }

            public bool Change(TimeSpan dueTime, TimeSpan period) => false;

            public void Fire()
            {
                if (Interlocked.Exchange(ref _finished, 1) == 0)
                {
                    _callback(_state);
                }
            }

            public void Dispose() => Interlocked.Exchange(ref _finished, 1);

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
                return new(SENTINEL);
            }
        }
    }
}
