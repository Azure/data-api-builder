// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Offline reservation, shutdown and revocation races through the real Core sessions and
    /// delivery workers. Identity callbacks provide store evidence; the pool test uses only an
    /// isolated in-memory sender. No host, database, SDK, filesystem or environment is changed.
    /// The CLI host fixture separately exercises Exporter's actual delayed-preflight handoff.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    [TestCategory("CliTelemetry")]
    public class CliTelemetryDeferredLaunchTests
    {
        private const string ROOT = "PRIVATE_DEFERRED_LAUNCH_ROOT_7c926";
        private const string OTHER_ROOT = "PRIVATE_DEFERRED_OTHER_ROOT_8536a";
        private const string COMMAND = "dab.cli.command";
        private const string LAUNCH = "dab.cli.engine_launch";
        private const string PROCESS_STARTED = "dab.engine.process_started";
        private static readonly Guid _installationId = new("ed4b9a97-7976-4c67-9c50-9307425cda11");
        private static readonly Guid _apiId = new("22a93634-6686-4dd7-815b-bfd54507d4ae");
        private static readonly Guid _otherApiId = new("dc9a3b80-8bb2-41f9-8541-d827ce7e3d9f");
        private static readonly DateTimeOffset _start = new(2026, 9, 25, 12, 0, 0, TimeSpan.Zero);
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

        [TestMethod]
        public async Task ReserveAndAbandonDoNoIdentityFactoryClockOrEventWork()
        {
            RecordingFactory factory = new();
            ManualClock clock = new();
            int identities = 0;
            using CliTelemetrySession cli = CreateCli(factory.Acquire, clock,
                createIdentity: _ => { identities++; return new(_apiId, "ephemeral"); },
                lookupIdentity: _ => { identities++; return new(_apiId, "reused"); });
            int timestampCalls = clock.TimestampCalls;
            int utcCalls = clock.UtcCalls;
            using CliTelemetryLaunchReservation reservation = Reserve(cli);

            Assert.IsTrue(reservation.IsAvailable);
            Assert.AreEqual(0, factory.Senders.Count);
            Assert.AreEqual(0, factory.Attempts.Count);
            Assert.AreEqual(0, identities);
            Assert.AreEqual(timestampCalls, clock.TimestampCalls);
            Assert.AreEqual(utcCalls, clock.UtcCalls);
            Assert.AreEqual(0, clock.TimerCount, "A ticket has no expiry timer or background work.");
            Assert.AreEqual(0, PendingDeliveries(cli).Length);
            CollectionAssert.AreEquivalent(new[] { typeof(CliTelemetrySession), typeof(CliTelemetryLaunchSource) },
                typeof(CliTelemetryLaunchReservation).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
                    .Select(field => field.FieldType).ToArray(), "A ticket stores only its owner and source, never an engine ID or root.");

            reservation.Dispose();
            reservation.Dispose();
            Assert.IsFalse(reservation.IsAvailable);
            Assert.IsNull(reservation.Begin(ROOT));
            await cli.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual(0, identities);
            Assert.AreEqual(0, factory.Senders.Count);
            Assert.AreEqual(0, factory.Records.Count);
        }

        [DataTestMethod]
        [DataRow("default")]
        [DataRow("opt_out")]
        [DataRow("complete")]
        [DataRow("stop")]
        [DataRow("disable")]
        [DataRow("dispose")]
        public async Task DisabledAndClosedInvocationsCannotReserveNewHelpers(string state)
        {
            RecordingFactory factory = new();
            int identities = 0;
            using CliTelemetrySession cli = CliTelemetrySession.Create(factory.Acquire,
                enableSyntheticCollection: state != "default", readEnvironmentVariable: _ => state == "opt_out" ? "1" : null,
                showNotice: () => { }, resolveInstallation: () => new(_installationId, "reused"),
                createIdentity: _ => { identities++; return new(_apiId, "ephemeral"); });
            if (state == "complete")
            {
                Complete(cli);
            }
            else if (state == "stop")
            {
                await cli.StopAsync().WaitAsync(_timeout);
            }
            else if (state == "disable")
            {
                cli.Disable();
            }
            else if (state == "dispose")
            {
                cli.Dispose();
            }

            Assert.IsNull(cli.ReserveEngineLaunch(CliTelemetryLaunchSource.ExportGraphQL));
            Assert.IsNull(cli.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.ExportGraphQL),
                "The ordinary API must continue rejecting completed/stopped invocations.");
            await cli.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual(0, identities);
            Assert.AreEqual(state == "complete" ? 1 : 0, factory.Records.Count);
            Assert.IsFalse(factory.Records.Any(record => record.Name == LAUNCH));
        }

        [TestMethod]
        public async Task InvalidSourcesDoNotConsumeTheSixteenReservationBudget()
        {
            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateCli(factory.Acquire);
            Assert.IsNull(cli.ReserveEngineLaunch((CliTelemetryLaunchSource)(-1)));
            Assert.IsNull(cli.ReserveEngineLaunch((CliTelemetryLaunchSource)int.MaxValue));
            for (int index = 0; index < 16; index++)
            {
                using CliTelemetryLaunchReservation reservation = Reserve(cli);
            }

            Assert.IsNull(cli.ReserveEngineLaunch(CliTelemetryLaunchSource.ExportGraphQL),
                "Abandoned tickets still consume the lifetime budget.");
            await cli.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual(0, factory.Senders.Count);
            Assert.AreEqual(0, factory.Records.Count);
        }

        [DataTestMethod]
        [DataRow((int)CliTelemetryLaunchSource.StartWeb, "start_web")]
        [DataRow((int)CliTelemetryLaunchSource.StartStdio, "start_stdio")]
        [DataRow((int)CliTelemetryLaunchSource.ExportGraphQL, "export_graphql")]
        public async Task NormalCompleteStopAndDisposePreserveTheOriginalReservedBridge(int source, string wireSource)
        {
            RecordingFactory factory = new();
            ManualClock clock = new();
            ConcurrentQueue<string?> creates = new();
            CliTelemetryInstallation installation = new(_installationId, "reused");
            EngineTelemetryIdentity identity = new(_apiId, "reused");
            using CliTelemetrySession cli = CreateCli(factory.Acquire, clock, installation,
                createIdentity: path => { creates.Enqueue(path); return identity; }, lookupIdentity: _ => identity);
            cli.ObserveConfiguration(ROOT);
            using CliTelemetryLaunchReservation reservation = Reserve(cli, (CliTelemetryLaunchSource)source);
            clock.Advance(TimeSpan.FromMilliseconds(7));
            Complete(cli);
            await cli.StopAsync().WaitAsync(_timeout);
            cli.Dispose();
            CliTelemetryEvent command = (CliTelemetryEvent)factory.Records.Single();
            Assert.AreEqual(COMMAND, command.Name);
            Assert.AreEqual("7", command.Properties["duration_ms"]);
            Assert.AreEqual(0, creates.Count);
            Assert.IsTrue(reservation.IsAvailable);
            Assert.IsFalse(cli.IsEnabled);
            Assert.AreEqual(1, factory.Senders.Single().DisposeCalls);
            Assert.IsNull(cli.BeginEngineLaunch(ROOT, (CliTelemetryLaunchSource)source));

            clock.Advance(TimeSpan.FromMilliseconds(11));
            ProductTelemetryLaunchContext? launch = reservation.Begin(ROOT);
            Assert.IsNotNull(launch);
            Assert.AreSame(installation, launch.Installation);
            Assert.AreSame(identity, launch.ApiIdentity);
            Assert.AreEqual((CliTelemetryLaunchSource)source, launch.Source);
            Assert.AreEqual(cli.SessionId, launch.ParentCliSessionId);
            Assert.AreNotEqual(cli.SessionId, launch.EngineSessionId);
            Assert.AreNotEqual(Guid.Empty, launch.EngineSessionId);
            Assert.IsFalse(reservation.IsAvailable);
            Assert.IsNull(reservation.Begin(OTHER_ROOT));
            CliTelemetryEvent record = (CliTelemetryEvent)await factory.WaitAsync(cli.SessionId, LAUNCH);
            await WaitForDeferredDrainAsync(cli);

            AssertLaunch(record, launch, wireSource);
            Assert.AreEqual(command.Sequence + 1, record.Sequence);
            Assert.AreEqual(_start.AddMilliseconds(18), record.OccurredAt);
            Assert.AreSame(command, factory.Records.Single(item => item.Name == COMMAND));
            CollectionAssert.AreEqual(new[] { ROOT }, creates.ToArray());
            Assert.AreEqual(2, factory.Senders.Count, "The late launch must acquire a new worker-owned lease, not a disposed CLI sender.");
            Assert.IsTrue(factory.Senders.All(sender => sender.DisposeCalls == 1));
            AssertPrivateValuesAbsent(factory.Records);
        }

        [TestMethod]
        public async Task ReservedDefaultIdentityResolutionUsesCapturedOptOutPolicyAfterShutdown()
        {
            RecordingFactory factory = new();
            ConcurrentQueue<string> reads = new();
            string? optOut = null;
            using CliTelemetrySession cli = CliTelemetrySession.Create(factory.Acquire, true,
                readEnvironmentVariable: name =>
                {
                    reads.Enqueue(name);
                    return name == ProductTelemetryPolicy.OPT_OUT_ENV_VAR ? optOut : null;
                }, showNotice: () => { }, resolveInstallation: () => new(_installationId, "reused"));
            using CliTelemetryLaunchReservation reservation = Reserve(cli);
            await cli.StopAsync().WaitAsync(_timeout);
            cli.Dispose();
            optOut = "true"; // Injected reader only; never change a process variable.

            // Null exercises the actual default store's captured policy without a filesystem target.
            ProductTelemetryLaunchContext? launch = reservation.Begin(null);
            Assert.IsNotNull(launch);
            Assert.AreEqual("ephemeral", launch.ApiIdentity!.Stability);
            await factory.WaitAsync(cli.SessionId, LAUNCH);
            await WaitForDeferredDrainAsync(cli);
            Assert.AreEqual(1, reads.Count(name => name == ProductTelemetryPolicy.OPT_OUT_ENV_VAR));
            Assert.AreEqual(1, factory.Records.Count);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task SameRootReusesRetainedCreationOrResolvesSavedEvidenceOnceAfterStop(bool stopFirst)
        {
            RecordingFactory factory = new();
            int creates = 0;
            EngineTelemetryIdentity saved = new(_apiId, "newly_saved");
            EngineTelemetryIdentity reused = new(_apiId, "reused");
            using CliTelemetrySession cli = CreateCli(factory.Acquire, createIdentity: path =>
            {
                Assert.AreEqual(ROOT, path);
                return Interlocked.Increment(ref creates) == 1 ? saved : reused;
            });
            cli.ConfigurationCreated(ROOT);
            using CliTelemetryLaunchReservation reservation = Reserve(cli);
            Complete(cli);
            if (stopFirst)
            {
                await cli.StopAsync().WaitAsync(_timeout);
                cli.Dispose();
            }

            ProductTelemetryLaunchContext? launch = reservation.Begin(ROOT);
            Assert.IsNotNull(launch);
            Assert.AreSame(stopFirst ? reused : saved, launch.ApiIdentity);
            Assert.IsNull(reservation.Begin(ROOT));
            await cli.StopAsync().WaitAsync(_timeout);
            await factory.WaitAsync(cli.SessionId, LAUNCH);
            await WaitForDeferredDrainAsync(cli);
            Assert.AreEqual(stopFirst ? 2 : 1, creates);
            Assert.IsTrue(factory.Records.All(record => record.Properties["dab_api_id"] == _apiId.ToString("D")));
        }

        [TestMethod]
        public async Task RetainedSameRootEphemeralCreationIsNotReplacedAfterComplete()
        {
            RecordingFactory factory = new();
            int creates = 0;
            EngineTelemetryIdentity ephemeral = new(_apiId, "ephemeral");
            using CliTelemetrySession cli = CreateCli(factory.Acquire, createIdentity: _ =>
            {
                Interlocked.Increment(ref creates);
                return ephemeral;
            });
            cli.ConfigurationCreated(ROOT);
            using CliTelemetryLaunchReservation reservation = Reserve(cli);
            Complete(cli);
            ProductTelemetryLaunchContext? launch = reservation.Begin(ROOT);
            Assert.IsNotNull(launch);
            Assert.AreSame(ephemeral, launch.ApiIdentity);
            await cli.StopAsync().WaitAsync(_timeout);
            await factory.WaitAsync(cli.SessionId, LAUNCH);
            await WaitForDeferredDrainAsync(cli);
            Assert.AreEqual(1, creates);
            Assert.IsTrue(factory.Records.All(record => record.Properties["dab_api_id_stability"] == "ephemeral"));
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task DifferentActualTargetSuppressesOnlyAnUncompletedCommandsLinkage(bool completeFirst)
        {
            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateCli(factory.Acquire,
                createIdentity: path => { Assert.AreEqual(OTHER_ROOT, path); return new(_otherApiId, "ephemeral"); },
                lookupIdentity: _ => new(_apiId, "reused"));
            cli.ObserveConfiguration(ROOT);
            using CliTelemetryLaunchReservation reservation = Reserve(cli);
            CliTelemetryEvent? original = null;
            if (completeFirst)
            {
                Complete(cli);
                original = (CliTelemetryEvent)await factory.WaitAsync(cli.SessionId, COMMAND);
            }

            ProductTelemetryLaunchContext? launch = reservation.Begin(OTHER_ROOT);
            Assert.IsNotNull(launch);
            Assert.AreEqual(_otherApiId, launch.ApiIdentity!.ApiId);
            Complete(cli);
            await cli.StopAsync().WaitAsync(_timeout);
            await factory.WaitAsync(cli.SessionId, LAUNCH);
            await WaitForDeferredDrainAsync(cli);
            CliTelemetryEvent command = (CliTelemetryEvent)factory.Records.Single(record => record.Name == COMMAND);
            Assert.AreEqual(completeFirst, command.Properties.ContainsKey("dab_api_id"));
            if (completeFirst)
            {
                Assert.AreSame(original, command, "A late launch cannot repair or retarget an already emitted command.");
                Assert.AreEqual(_apiId.ToString("D"), command.Properties["dab_api_id"]);
            }
        }

        [TestMethod]
        public async Task NormalShutdownDoesNotWaitForOrRevokeIdentityAlreadyInProgress()
        {
            RecordingFactory factory = new();
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            using CliTelemetrySession cli = CreateCli(factory.Acquire, createIdentity: path =>
            {
                Assert.AreEqual(ROOT, path);
                entered.TrySetResult();
                release.Task.WaitAsync(_timeout).GetAwaiter().GetResult();
                return new(_apiId, "ephemeral");
            });
            using CliTelemetryLaunchReservation reservation = Reserve(cli);
            Task<ProductTelemetryLaunchContext?> pending = Task.Run(() => reservation.Begin(ROOT));
            try
            {
                await entered.Task.WaitAsync(_timeout);
                await Task.Run(async () =>
                {
                    Complete(cli);
                    await cli.StopAsync();
                    cli.Dispose();
                }).WaitAsync(_timeout);
                Assert.IsFalse(pending.IsCompleted, "Identity I/O must not hold the shutdown gate.");
                Assert.AreEqual(COMMAND, factory.Records.Single().Name);
                Assert.IsFalse(factory.Records.Single().Properties.ContainsKey("dab_api_id"));
            }
            finally
            {
                release.TrySetResult();
            }

            ProductTelemetryLaunchContext? launch = await pending.WaitAsync(_timeout);
            Assert.IsNotNull(launch);
            CliTelemetryEvent record = (CliTelemetryEvent)await factory.WaitAsync(cli.SessionId, LAUNCH);
            await WaitForDeferredDrainAsync(cli);
            AssertLaunch(record, launch, "export_graphql");
            Assert.AreEqual(2L, record.Sequence);
        }

        [DataTestMethod]
        [DataRow("disable")]
        [DataRow("dispose")]
        [DataRow("canceled_stop")]
        [DataRow("disable_after_stop")]
        public async Task RevocationBeforeBeginPreventsIdentityAndNewSender(string close)
        {
            RecordingFactory factory = new();
            int creates = 0;
            using CliTelemetrySession cli = CreateCli(factory.Acquire,
                createIdentity: _ => { creates++; return new(_apiId, "ephemeral"); });
            using CliTelemetryLaunchReservation first = Reserve(cli);
            using CliTelemetryLaunchReservation second = Reserve(cli);
            if (close == "dispose")
            {
                cli.Dispose();
            }
            else if (close == "canceled_stop")
            {
                await cli.StopAsync(new CancellationToken(canceled: true));
            }
            else
            {
                if (close == "disable_after_stop")
                {
                    await cli.StopAsync().WaitAsync(_timeout);
                    cli.Dispose();
                }

                cli.Disable();
            }

            Assert.IsFalse(first.IsAvailable);
            Assert.IsFalse(second.IsAvailable);
            Assert.IsNull(first.Begin(ROOT));
            Assert.IsNull(second.Begin(OTHER_ROOT));
            await cli.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual(0, creates);
            Assert.AreEqual(0, factory.Senders.Count);
            Assert.AreEqual(0, factory.Records.Count);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task DisableDuringIdentityDoesNotWaitAndPreventsLateIntentAndSender(bool stopFirst)
        {
            RecordingFactory factory = new();
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            int creates = 0;
            using CliTelemetrySession cli = CreateCli(factory.Acquire, createIdentity: _ =>
            {
                Interlocked.Increment(ref creates);
                entered.TrySetResult();
                release.Task.WaitAsync(_timeout).GetAwaiter().GetResult();
                return new(_apiId, "ephemeral");
            });
            using CliTelemetryLaunchReservation reservation = Reserve(cli);
            if (stopFirst)
            {
                await cli.StopAsync().WaitAsync(_timeout);
                cli.Dispose();
            }

            Task<ProductTelemetryLaunchContext?> pending = Task.Run(() => reservation.Begin(ROOT));
            try
            {
                await entered.Task.WaitAsync(_timeout);
                await Task.Run(cli.Disable).WaitAsync(_timeout);
                Assert.IsFalse(pending.IsCompleted);
                Assert.AreEqual(0, factory.Senders.Count);
            }
            finally
            {
                release.TrySetResult();
            }

            Assert.IsNull(await pending.WaitAsync(_timeout));
            Assert.IsNull(reservation.Begin(ROOT));
            await cli.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual(1, creates);
            Assert.AreEqual(0, factory.Senders.Count);
            Assert.AreEqual(0, factory.Records.Count);
            Assert.AreEqual(0, PendingDeliveries(cli).Length);
        }

        [TestMethod]
        public async Task IdentityFailureIsTelemetryOnlyAndDoesNotCreateASender()
        {
            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateCli(factory.Acquire, createIdentity: _ => throw new IOException(ROOT));
            using CliTelemetryLaunchReservation first = Reserve(cli);
            using CliTelemetryLaunchReservation second = Reserve(cli);
            Assert.IsNull(first.Begin(ROOT));
            Assert.IsFalse(cli.IsEnabled);
            Assert.IsFalse(second.IsAvailable);
            Assert.IsNull(second.Begin(ROOT));
            await cli.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual(0, factory.Senders.Count);
            Assert.AreEqual(0, factory.Records.Count);
        }

        [DataTestMethod]
        [DataRow("rejected")]
        [DataRow("throws")]
        [DataRow("pre_canceled")]
        public async Task AbandonedHelperTicketDoesNotCreateIntentOrDisableOtherReservations(string mode)
        {
            RecordingFactory factory = new();
            int creates = 0;
            int helperCalls = 0;
            using CliTelemetrySession cli = CreateCli(factory.Acquire,
                createIdentity: _ => { creates++; return new(_apiId, "ephemeral"); });
            using CliTelemetryLaunchReservation abandoned = Reserve(cli);
            using CliTelemetryLaunchReservation survivor = Reserve(cli);
            try
            {
                await Task.Run(() =>
                {
                    Interlocked.Increment(ref helperCalls);
                    if (mode == "throws")
                    {
                        throw new IOException(ROOT);
                    }

                    // Preflight rejection: do not call Begin. Cancellation prevents this
                    // delegate from running at all, including any ID/identity creation.
                }, new CancellationToken(canceled: mode == "pre_canceled")).WaitAsync(_timeout);
            }
            catch (IOException) when (mode == "throws")
            {
            }
            catch (OperationCanceledException) when (mode == "pre_canceled")
            {
            }
            finally
            {
                abandoned.Dispose();
            }

            Assert.AreEqual(mode == "pre_canceled" ? 0 : 1, helperCalls);
            Assert.IsNull(abandoned.Begin(ROOT));
            Assert.IsTrue(survivor.IsAvailable);
            Assert.IsTrue(cli.IsEnabled);
            Assert.AreEqual(0, creates);
            Assert.AreEqual(0, factory.Senders.Count);
            Assert.AreEqual(0, factory.Records.Count);
            Assert.IsNotNull(survivor.Begin(ROOT));
            await cli.StopAsync().WaitAsync(_timeout);
            await factory.WaitAsync(cli.SessionId, LAUNCH);
            await WaitForDeferredDrainAsync(cli);
            Assert.AreEqual(1, creates);
            Assert.AreEqual(1, factory.Records.Count);
        }

        [TestMethod]
        public async Task ConcurrentReservationAdmissionCannotExceedSixteen()
        {
            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateCli(factory.Acquire);
            TaskCompletionSource release = Signal();
            Task<CliTelemetryLaunchReservation?>[] requests = Enumerable.Range(0, 48).Select(_ => Task.Run(async () =>
            {
                await release.Task.WaitAsync(_timeout);
                return cli.ReserveEngineLaunch(CliTelemetryLaunchSource.ExportGraphQL);
            })).ToArray();
            release.TrySetResult();
            CliTelemetryLaunchReservation?[] reservations = await Task.WhenAll(requests).WaitAsync(_timeout);
            foreach (CliTelemetryLaunchReservation? reservation in reservations)
            {
                reservation?.Dispose();
            }

            Assert.AreEqual(16, reservations.Count(reservation => reservation is not null));
            Assert.IsNull(cli.ReserveEngineLaunch(CliTelemetryLaunchSource.ExportGraphQL));
            await cli.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual(0, factory.Senders.Count);
            Assert.AreEqual(0, factory.Records.Count);
        }

        [TestMethod]
        public async Task OrdinaryAndReservedLaunchesShareTheSameLifetimeBudget()
        {
            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateCli(factory.Acquire);
            using CliTelemetryLaunchReservation reservation = Reserve(cli);
            for (int index = 0; index < 15; index++)
            {
                Assert.IsNotNull(cli.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb));
            }

            Assert.IsNull(cli.ReserveEngineLaunch(CliTelemetryLaunchSource.ExportGraphQL));
            Assert.IsNull(cli.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb));
            Assert.IsNotNull(reservation.Begin(ROOT), "Its slot was already reserved, not a seventeenth launch.");
            await cli.StopAsync().WaitAsync(_timeout);
            await WaitForDeferredDrainAsync(cli);
            Assert.AreEqual(16, factory.Records.Count);
            Assert.AreEqual(16, factory.Records.Select(record => record.EventId).Distinct().Count());
            Assert.AreEqual(2, factory.Senders.Count);
        }

        [TestMethod]
        public async Task ConcurrentBeginOnOneTicketResolvesAndEmitsOnlyOnce()
        {
            RecordingFactory factory = new();
            int creates = 0;
            using CliTelemetrySession cli = CreateCli(factory.Acquire, createIdentity: _ =>
            {
                Interlocked.Increment(ref creates);
                return new(_apiId, "ephemeral");
            });
            using CliTelemetryLaunchReservation reservation = Reserve(cli);
            TaskCompletionSource release = Signal();
            Task<ProductTelemetryLaunchContext?>[] beginnings = Enumerable.Range(0, 32).Select(_ => Task.Run(async () =>
            {
                await release.Task.WaitAsync(_timeout);
                return reservation.Begin(ROOT);
            })).ToArray();
            release.TrySetResult();
            ProductTelemetryLaunchContext?[] results = await Task.WhenAll(beginnings).WaitAsync(_timeout);
            await cli.StopAsync().WaitAsync(_timeout);
            await factory.WaitAsync(cli.SessionId, LAUNCH);
            await WaitForDeferredDrainAsync(cli);
            Assert.AreEqual(1, results.Count(result => result is not null));
            Assert.AreEqual(1, creates);
            Assert.AreEqual(1, factory.Senders.Count);
            Assert.AreEqual(1, factory.Records.Count);
            Assert.IsNull(reservation.Begin(OTHER_ROOT));
        }

        [TestMethod]
        public async Task ConcurrentReservedHandoffsAndCompletionUseOneOriginalCliSequence()
        {
            RecordingFactory factory = new();
            int creates = 0;
            using CliTelemetrySession cli = CreateCli(factory.Acquire, createIdentity: _ =>
            {
                Interlocked.Increment(ref creates);
                return new(_apiId, "ephemeral");
            });
            cli.ConfigurationCreated(ROOT);
            CliTelemetryLaunchReservation[] reservations = Enumerable.Range(0, 16).Select(_ => Reserve(cli)).ToArray();
            TaskCompletionSource release = Signal();
            Task<ProductTelemetryLaunchContext?>[] beginnings = reservations.Select(reservation => Task.Run(async () =>
            {
                await release.Task.WaitAsync(_timeout);
                return reservation.Begin(ROOT);
            })).ToArray();
            Task[] completions = Enumerable.Range(0, 16).Select(_ => Task.Run(async () =>
            {
                await release.Task.WaitAsync(_timeout);
                Complete(cli);
            })).ToArray();
            release.TrySetResult();
            ProductTelemetryLaunchContext?[] launches = await Task.WhenAll(beginnings).WaitAsync(_timeout);
            await Task.WhenAll(completions).WaitAsync(_timeout);
            await cli.StopAsync().WaitAsync(_timeout);
            await WaitForDeferredDrainAsync(cli);

            Assert.IsTrue(launches.All(launch => launch is not null));
            Assert.AreEqual(1, creates);
            Assert.AreEqual(16, launches.Select(launch => launch!.EngineSessionId).Distinct().Count());
            IProductTelemetryEvent[] records = factory.Records.OrderBy(record => record.Sequence).ToArray();
            Assert.AreEqual(17, records.Length);
            Assert.AreEqual(1, records.Count(record => record.Name == COMMAND));
            Assert.AreEqual(16, records.Count(record => record.Name == LAUNCH));
            Assert.AreEqual(17, records.Select(record => record.EventId).Distinct().Count());
            CollectionAssert.AreEqual(Enumerable.Range(1, 17).Select(sequence => (long)sequence).ToArray(),
                records.Select(record => record.Sequence).ToArray(), "Network arrival is independent; the original owner's sequence is serialized.");
            Assert.IsTrue(records.All(record => record.SessionId == cli.SessionId && record.IsSynthetic));
            AssertPrivateValuesAbsent(records);
        }

        [TestMethod]
        public async Task DeferredRetriesReuseTheExactEnvelopeAndDoNotCaptureCallerContext()
        {
            ManualClock clock = new();
            int attempts = 0;
            AsyncLocal<string?> callerContext = new() { Value = ROOT };
            ConcurrentQueue<string?> factoryContexts = new();
            RecordingFactory factory = new((_, _) => ValueTask.FromResult(Interlocked.Increment(ref attempts) >= 3));
            using CliTelemetrySession cli = CreateCli(() =>
            {
                factoryContexts.Enqueue(callerContext.Value);
                return factory.Acquire();
            }, clock);
            using CliTelemetryLaunchReservation reservation = Reserve(cli);
            ProductTelemetryLaunchContext? launch = reservation.Begin(ROOT);
            Assert.IsNotNull(launch);
            Assert.IsNull(reservation.Begin(OTHER_ROOT));
            (await clock.NextTimerAsync(TimeSpan.FromMilliseconds(100))).Fire();
            (await clock.NextTimerAsync(TimeSpan.FromMilliseconds(200))).Fire();
            CliTelemetryEvent delivered = (CliTelemetryEvent)await factory.WaitAsync(cli.SessionId, LAUNCH);
            await WaitForDeferredDrainAsync(cli);
            await cli.StopAsync().WaitAsync(_timeout);

            Assert.AreEqual(3, factory.Attempts.Count);
            Assert.IsTrue(factory.Attempts.All(attempt => ReferenceEquals(delivered, attempt.Record)));
            AssertLaunch(delivered, launch, "export_graphql");
            Assert.AreEqual(1L, delivered.Sequence);
            Assert.AreEqual(1, factory.Senders.Count);
            Assert.AreEqual(1, factory.Senders.Single().DisposeCalls);
            Assert.IsNull(factoryContexts.Single(), "The dedicated worker must suppress the caller's execution context.");
        }

        [TestMethod]
        public async Task FailedDeferredFactoryIsBoundedAndDropEvidenceDoesNotInventAnotherEvent()
        {
            ManualClock clock = new();
            RecordingFactory factory = new();
            int failures = 0;
            bool fail = true;
            using CliTelemetrySession cli = CreateCli(() =>
            {
                if (Volatile.Read(ref fail))
                {
                    Interlocked.Increment(ref failures);
                    throw new InvalidOperationException(ROOT);
                }

                return factory.Acquire();
            }, clock);
            using CliTelemetryLaunchReservation reservation = Reserve(cli);
            Assert.IsNotNull(reservation.Begin(ROOT), "An asynchronously failing sender must not become an application failure.");
            (await clock.NextTimerAsync(TimeSpan.FromMilliseconds(100))).Fire();
            (await clock.NextTimerAsync(TimeSpan.FromMilliseconds(200))).Fire();
            await WaitForDeferredDrainAsync(cli);
            Assert.AreEqual(3, failures);
            Assert.AreEqual(0, factory.Records.Count);
            Assert.AreEqual(0, factory.Senders.Count);

            Volatile.Write(ref fail, false);
            Complete(cli);
            await cli.StopAsync().WaitAsync(_timeout);
            CliTelemetryEvent command = (CliTelemetryEvent)factory.Records.Single();
            Assert.AreEqual(COMMAND, command.Name);
            Assert.AreEqual(2L, command.Sequence, "A dropped launch leaves a sequence gap, not fabricated history.");
            Assert.AreEqual("1", command.Properties["sender_dropped_events"]);
            Assert.AreEqual(0, PendingDeliveries(cli).Length);
        }

        [TestMethod]
        public async Task NormalStopAndDisposeDoNotJoinDeferredNetworkAndDeadlineReleasesTracking()
        {
            ManualClock clock = new();
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            RecordingFactory factory = new(async (_, _) =>
            {
                entered.TrySetResult();
                await release.Task.ConfigureAwait(false); // Deliberately ignore cancellation.
                return true;
            });
            using CliTelemetrySession cli = CreateCli(factory.Acquire, clock);
            using CliTelemetryLaunchReservation reservation = Reserve(cli);
            try
            {
                Assert.IsNotNull(await Task.Run(() => reservation.Begin(ROOT)).WaitAsync(_timeout));
                await entered.Task.WaitAsync(_timeout);
                ManualTimer deadline = await clock.NextTimerAsync(TimeSpan.FromSeconds(2));
                await cli.StopAsync().WaitAsync(_timeout);
                cli.Dispose();
                Assert.IsFalse(factory.Attempts.Single().Token.IsCancellationRequested);
                Assert.AreEqual(1, PendingDeliveries(cli).Length);
                Assert.AreEqual(0, factory.Senders.Single().DisposeCalls);

                deadline.Fire();
                await WaitForDeferredDrainAsync(cli);
                Assert.IsTrue(factory.Attempts.Single().Token.IsCancellationRequested);
                Assert.AreEqual(0, factory.Senders.Single().DisposeCalls,
                    "Only the worker can dispose its exporter after the blocked call actually exits.");
                Assert.AreEqual(0, factory.Records.Count);
            }
            finally
            {
                cli.Disable();
                release.TrySetResult();
                if (factory.Senders.TryPeek(out RecordingExporter? sender))
                {
                    await sender.Disposed.Task.WaitAsync(_timeout);
                }
            }

            Assert.AreEqual(1, factory.Attempts.Count);
            Assert.AreEqual(0, factory.Records.Count, "Cancellation abandons the attempt without a final event.");
        }

        [TestMethod]
        public async Task DisablePendingDeferredDeliveryCancelsOnlyItsLeaseNotTheLinkedEngine()
        {
            ManualClock clock = new();
            TaskCompletionSource launchEntered = Signal();
            TaskCompletionSource releaseLaunch = Signal();
            TaskCompletionSource engineAcquired = Signal();
            RecordingFactory underlying = new(async (record, token) =>
            {
                if (record.Name == LAUNCH)
                {
                    launchEntered.TrySetResult();
                    await releaseLaunch.Task.WaitAsync(token).ConfigureAwait(false);
                }

                return true;
            });
            Assert.IsTrue(ApplicationInsightsTelemetryDestination.TryParse(
                "InstrumentationKey=01234567-89ab-cdef-0123-456789abcdef;IngestionEndpoint=https://deferred-launch.synthetic.invalid/",
                out ApplicationInsightsTelemetryDestination? destination));
            using ProductTelemetrySenderPool pool = new(_ => underlying.Acquire());
            ConcurrentQueue<TrackedLease> cliLeases = new();
            using CliTelemetrySession cli = CreateCli(() =>
            {
                TrackedLease lease = new(pool.AcquireLease(destination));
                cliLeases.Enqueue(lease);
                return lease;
            }, clock);
            using CliTelemetryLaunchReservation reservation = Reserve(cli);
            Complete(cli);
            await cli.StopAsync().WaitAsync(_timeout);
            cli.Dispose();
            Assert.AreEqual(1, cliLeases.Single().DisposeCalls);
            ProductTelemetryLaunchContext? launch = reservation.Begin(ROOT);
            Assert.IsNotNull(launch);
            using EngineTelemetrySession engine = EngineTelemetrySession.Create(() =>
            {
                EngineExporterAdapter lease = new(pool.AcquireLease(destination));
                engineAcquired.TrySetResult();
                return lease;
            }, enableSyntheticCollection: true, configPath: ROOT, launchContext: launch,
                readEnvironmentVariable: _ => null, showNotice: () => { }, startTimer: false);
            try
            {
                await launchEntered.Task.WaitAsync(_timeout);
                await engineAcquired.Task.WaitAsync(_timeout);
                Assert.AreEqual(2, cliLeases.Count, "The pending deferred sender is independent of the already released main sender.");
                await Task.Run(cli.Disable).WaitAsync(_timeout);
                Assert.IsTrue(underlying.Attempts.Single(attempt => attempt.Record.Name == LAUNCH).Token.IsCancellationRequested);
                EngineTelemetryEvent started = (EngineTelemetryEvent)await underlying.WaitAsync(launch.EngineSessionId, PROCESS_STARTED);
                Assert.IsTrue(engine.IsEnabled);
                Assert.AreEqual(cli.SessionId.ToString("D"), started.Properties["dab_parent_cli_session_id"]);
                Assert.AreEqual(_apiId.ToString("D"), started.Properties["dab_api_id"]);
                Assert.IsFalse(underlying.Attempts.Single(attempt => attempt.Record.Name == PROCESS_STARTED).Token.IsCancellationRequested);
                await WaitForDeferredDrainAsync(cli);
                Assert.IsTrue(cliLeases.All(lease => lease.DisposeCalls == 1));
                Assert.AreEqual(0, underlying.Senders.Single().DisposeCalls, "Neither CLI lease may close the engine's pooled sender.");

                engine.StartupFailed(TelemetryFailureStage.Configuration);
                await engine.StopAsync().WaitAsync(_timeout);
                CollectionAssert.AreEqual(new[] { PROCESS_STARTED, "dab.engine.startup_failed", "dab.engine.stopped" },
                    underlying.Records.OfType<EngineTelemetryEvent>().Select(record => record.Name).ToArray());
                Assert.AreEqual(COMMAND, underlying.Records.OfType<CliTelemetryEvent>().Single().Name,
                    "Disable must discard the pending launch, not flush it or manufacture another command.");
                Assert.AreEqual(1, underlying.Senders.Count);
                pool.Dispose();
                Assert.AreEqual(1, underlying.Senders.Single().DisposeCalls);
            }
            finally
            {
                cli.Disable();
                releaseLaunch.TrySetResult();
                engine.Disable();
                await engine.StopAsync().WaitAsync(_timeout);
                await WaitForDeferredDrainAsync(cli);
            }
        }

        private static CliTelemetrySession CreateCli(Func<IProductTelemetryExporter<IProductTelemetryEvent>> factory,
            TimeProvider? clock = null, CliTelemetryInstallation? installation = null,
            Func<string?, EngineTelemetryIdentity>? createIdentity = null,
            Func<string?, EngineTelemetryIdentity?>? lookupIdentity = null)
            => CliTelemetrySession.Create(factory, true, clock, readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveInstallation: () => installation ?? new(_installationId, "reused"),
                createIdentity: createIdentity ?? (_ => new(_apiId, "ephemeral")), lookupIdentity: lookupIdentity ?? (_ => null));

        private static CliTelemetryLaunchReservation Reserve(CliTelemetrySession cli,
            CliTelemetryLaunchSource source = CliTelemetryLaunchSource.ExportGraphQL)
        {
            CliTelemetryLaunchReservation? reservation = cli.ReserveEngineLaunch(source);
            Assert.IsNotNull(reservation);
            return reservation;
        }

        private static void Complete(CliTelemetrySession cli)
            => cli.Complete("export", "none", ImmutableDictionary<string, string>.Empty, CliTelemetryOutcome.Success);

        private static void AssertLaunch(CliTelemetryEvent record, ProductTelemetryLaunchContext launch, string source)
        {
            Assert.AreEqual(LAUNCH, record.Name);
            Assert.AreEqual(launch.ParentCliSessionId, record.SessionId);
            Assert.AreEqual(launch.EngineSessionId.ToString("D"), record.Properties["dab_launched_engine_session_id"]);
            Assert.AreEqual(launch.ParentCliSessionId.ToString("D"), record.Properties["dab_parent_cli_session_id"]);
            Assert.AreEqual(launch.ApiIdentity!.ApiId.ToString("D"), record.Properties["dab_api_id"]);
            Assert.AreEqual(launch.ApiIdentity.Stability, record.Properties["dab_api_id_stability"]);
            Assert.AreEqual(launch.Installation.InstallationId!.Value.ToString("D"), record.Properties["dab_installation_id"]);
            Assert.AreEqual(launch.Installation.Stability, record.Properties["dab_installation_id_stability"]);
            Assert.AreEqual(source, record.Properties["launch_source"]);
            Assert.AreEqual("0", record.Properties["sender_dropped_events"]);
            Assert.IsTrue(record.IsSynthetic);
            Assert.IsFalse(record.Properties.ContainsKey("dab_config_epoch"));
        }

        private static void AssertPrivateValuesAbsent(IEnumerable<IProductTelemetryEvent> records)
        {
            string json = JsonSerializer.Serialize(records);
            Assert.IsFalse(json.Contains(ROOT, StringComparison.Ordinal));
            Assert.IsFalse(json.Contains(OTHER_ROOT, StringComparison.Ordinal));
        }

        // Inspect only worker tracking, not an alternate production path. Polling is limited
        // to eventual cleanup; all behavioral races use explicit preflight/export/timer barriers.
        private static ProductTelemetryDelivery<CliTelemetryEvent>[] PendingDeliveries(CliTelemetrySession cli)
        {
            object gate = typeof(CliTelemetrySession).GetField("_sync", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cli)!;
            lock (gate)
            {
                HashSet<ProductTelemetryDelivery<CliTelemetryEvent>>? pending =
                    (HashSet<ProductTelemetryDelivery<CliTelemetryEvent>>?)typeof(CliTelemetrySession)
                        .GetField("_deferredDeliveries", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cli);
                return pending?.ToArray() ?? [];
            }
        }

        private static async Task WaitForDeferredDrainAsync(CliTelemetrySession cli)
        {
            using CancellationTokenSource deadline = new(_timeout);
            while (PendingDeliveries(cli).Length != 0)
            {
                await Task.Delay(1, deadline.Token);
            }
        }

        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed record Attempt(IProductTelemetryEvent Record, CancellationToken Token);

        private sealed class RecordingFactory(Func<IProductTelemetryEvent, CancellationToken, ValueTask<bool>>? export = null)
        {
            private readonly ConcurrentDictionary<(Guid Session, string Name), TaskCompletionSource<IProductTelemetryEvent>> _signals = new();
            internal ConcurrentQueue<RecordingExporter> Senders { get; } = new();
            internal ConcurrentQueue<Attempt> Attempts { get; } = new();
            internal ConcurrentQueue<IProductTelemetryEvent> Records { get; } = new();

            internal IProductTelemetryExporter<IProductTelemetryEvent> Acquire()
            {
                RecordingExporter sender = new(this);
                Senders.Enqueue(sender);
                return sender;
            }

            internal async ValueTask<bool> ExportAsync(IProductTelemetryEvent record, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                Attempts.Enqueue(new(record, token));
                bool accepted = export is null || await export(record, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (accepted)
                {
                    Records.Enqueue(record);
                    EventSignal(record.SessionId, record.Name).TrySetResult(record);
                }

                return accepted;
            }

            internal Task<IProductTelemetryEvent> WaitAsync(Guid session, string name)
                => EventSignal(session, name).Task.WaitAsync(_timeout);

            private TaskCompletionSource<IProductTelemetryEvent> EventSignal(Guid session, string name)
                => _signals.GetOrAdd((session, name), _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        private sealed class RecordingExporter(RecordingFactory owner) : IProductTelemetryExporter<IProductTelemetryEvent>
        {
            private int _disposeCalls;
            internal int DisposeCalls => Volatile.Read(ref _disposeCalls);
            internal TaskCompletionSource Disposed { get; } = Signal();

            public ValueTask<bool> ExportAsync(IProductTelemetryEvent record, CancellationToken cancellationToken)
            {
                ObjectDisposedException.ThrowIf(DisposeCalls != 0, this);
                return owner.ExportAsync(record, cancellationToken);
            }

            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCalls);
                Disposed.TrySetResult();
            }
        }

        private sealed class TrackedLease(IProductTelemetryExporter<IProductTelemetryEvent> lease) : IProductTelemetryExporter<IProductTelemetryEvent>
        {
            private int _disposeCalls;
            internal int DisposeCalls => Volatile.Read(ref _disposeCalls);
            public ValueTask<bool> ExportAsync(IProductTelemetryEvent record, CancellationToken cancellationToken)
                => lease.ExportAsync(record, cancellationToken);
            public void Dispose()
            {
                Interlocked.Increment(ref _disposeCalls);
                lease.Dispose();
            }
        }

        private sealed class EngineExporterAdapter(IProductTelemetryExporter<IProductTelemetryEvent> lease) : IEngineTelemetryExporter
        {
            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
                => lease.ExportAsync(record, cancellationToken);
            public void Dispose() => lease.Dispose();
        }

        private sealed class ManualClock : TimeProvider
        {
            private readonly Channel<ManualTimer> _timers = Channel.CreateUnbounded<ManualTimer>();
            private long _ticks;
            private int _timestampCalls;
            private int _utcCalls;
            private int _timerCount;
            internal int TimestampCalls => Volatile.Read(ref _timestampCalls);
            internal int UtcCalls => Volatile.Read(ref _utcCalls);
            internal int TimerCount => Volatile.Read(ref _timerCount);
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;

            public override long GetTimestamp()
            {
                Interlocked.Increment(ref _timestampCalls);
                return Interlocked.Read(ref _ticks);
            }

            public override DateTimeOffset GetUtcNow()
            {
                Interlocked.Increment(ref _utcCalls);
                return _start.AddTicks(Interlocked.Read(ref _ticks));
            }

            internal void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                Assert.AreEqual(Timeout.InfiniteTimeSpan, period, "Reservations/delivery must not add a periodic timer.");
                Interlocked.Increment(ref _timerCount);
                ManualTimer timer = new(callback, state, dueTime);
                _timers.Writer.TryWrite(timer);
                return timer;
            }

            internal async Task<ManualTimer> NextTimerAsync(TimeSpan dueTime)
            {
                using CancellationTokenSource deadline = new(_timeout);
                while (true)
                {
                    ManualTimer timer = await _timers.Reader.ReadAsync(deadline.Token);
                    if (timer.DueTime == dueTime)
                    {
                        return timer;
                    }
                }
            }
        }

        private sealed class ManualTimer(TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
        {
            private int _finished;
            internal TimeSpan DueTime { get; } = dueTime;
            public bool Change(TimeSpan dueTime, TimeSpan period) => false;
            internal void Fire()
            {
                if (Interlocked.Exchange(ref _finished, 1) == 0)
                {
                    callback(state);
                }
            }

            public void Dispose() => Interlocked.Exchange(ref _finished, 1);
            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }
}
