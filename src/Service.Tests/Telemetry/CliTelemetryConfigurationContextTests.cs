// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// In-memory command snapshots and session admission/versioning. Identity callbacks and
    /// exporters are local; configurations never load files or contact a database or host.
    /// </summary>
    [TestClass]
    [TestCategory("CliTelemetry")]
    [TestCategory("EngineTelemetry")]
    public class CliTelemetryConfigurationContextTests
    {
        private const string SENTINEL = "PRIVATE_CLI_CONTEXT_b6d82";
        private const string ROOT = SENTINEL + "_root";
        private const string OTHER_ROOT = SENTINEL + "_other_root";
        private const string COMMAND = "dab.cli.command";
        private const string FIRST_RUN = "dab.cli.first_run";
        private const string LAUNCH = "dab.cli.engine_launch";
        private static readonly Guid _installationId = new("52803dd2-18c9-45bb-8654-b83bca726522");
        private static readonly Guid _apiId = new("8ad7ac57-b32c-4860-9d51-094a751c8cc9");
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);
        private static readonly TimeSpan _projectionTimeout = TimeSpan.FromSeconds(30);

        [DataTestMethod]
        [DataRow((int)CliTelemetryLaunchSource.StartWeb, false)]
        [DataRow((int)CliTelemetryLaunchSource.StartStdio, true)]
        [DataRow((int)CliTelemetryLaunchSource.ExportGraphQL, false)]
        public async Task OnlyCommandCarriesTheCompleteEngineEquivalentConfigurationContext(int source, bool saved)
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter, firstRun: true);
            RuntimeConfig config = CreateConfig();
            session.ObserveConfigurationDetails(ROOT, config, saved);
            Assert.IsNotNull(session.BeginEngineLaunch(ROOT, (CliTelemetryLaunchSource)source, CliTelemetryDatabaseType.MsSql));
            Complete(session);
            await session.StopAsync().WaitAsync(_timeout);

            CliTelemetryEvent[] records = exporter.Records.ToArray();
            CollectionAssert.AreEqual(new[] { FIRST_RUN, LAUNCH, COMMAND }, records.Select(record => record.Name).ToArray());
            Assert.IsFalse(records[0].Properties.ContainsKey("configuration_context"));
            Assert.IsFalse(records[1].Properties.ContainsKey("configuration_context"));
            Assert.AreEqual("unknown", records[0].Properties["database_type"]);
            AssertMatchesEngine(records[2], EngineTelemetrySnapshotFactory.Create(config), saved);
            Assert.AreEqual(_apiId.ToString("D"), records[2].Properties["dab_api_id"]);
            AssertNoPrivateValues(records);
        }

        [TestMethod]
        public async Task SameRootSavedCloneReplacesLoadedProviderAndContextWithoutIdentityWork()
        {
            CapturingExporter exporter = new();
            int identities = 0;
            using CliTelemetrySession session = CreateSession(exporter,
                createIdentity: _ => { identities++; return new(_apiId, "newly_saved"); },
                lookupIdentity: _ => { identities++; return new(_apiId, "reused"); });
            RuntimeConfig loaded = CreateConfig();
            session.ObserveConfigurationDetails(ROOT, loaded);
            CliTelemetryConfigurationSnapshot? first = StoredSnapshot(session);
            Assert.IsNotNull(first);
            Dictionary<string, string> before = Context(first.Json);
            Assert.AreEqual("loaded", before["observation"]);
            Assert.AreEqual("mssql", before["data_sources.types"]);
            Assert.AreEqual("enabled", before["runtime.rest.effective"]);

            Dictionary<string, Entity> savedEntities = new()
            {
                [SENTINEL] = NewEntity(),
                [SENTINEL + "_view"] = NewEntity(EntitySourceType.View)
            };
            RuntimeConfig saved = loaded with
            {
                DataSource = new(DatabaseType.PostgreSQL, SENTINEL, new() { ["set-session-context"] = false }),
                Runtime = loaded.Runtime! with { Rest = new(Enabled: false) },
                Entities = RawEntities(savedEntities)
            };
            Assert.AreEqual(DatabaseType.MSSQL, saved.GetDataSourceFromDataSourceName(saved.DefaultDataSourceName).DatabaseType,
                "The saved record clone deliberately retains the old default-source map.");
            session.ObserveConfigurationDetails(ROOT, saved, saved: true);
            Assert.AreNotSame(first, StoredSnapshot(session));
            Assert.AreEqual(0, identities, "Details neither look up nor create identity.");
            Assert.AreEqual(0, exporter.Acquisitions, "Observation does not start an exporter.");
            Assert.AreEqual(0, exporter.Records.Count);
            Complete(session);
            await session.StopAsync().WaitAsync(_timeout);

            CliTelemetryEvent command = exporter.Records.Single();
            RuntimeConfig expected = CreateConfig(DatabaseType.PostgreSQL, restEnabled: false, entities: savedEntities);
            AssertMatchesEngine(command, EngineTelemetrySnapshotFactory.Create(expected), saved: true);
            Dictionary<string, string> after = Context(command.Properties["configuration_context"]);
            Assert.AreEqual("postgresql", command.Properties["database_type"]);
            Assert.AreEqual("postgresql", after["data_sources.types"]);
            Assert.AreEqual("disabled", after["runtime.rest.effective"]);
            Assert.AreEqual("2-10", after["scale.entity_count"]);
            Assert.AreEqual(0, identities);
            AssertNoApiIdentity(command);
        }

        [DataTestMethod]
        [DataRow("default_disabled")]
        [DataRow("completed")]
        [DataRow("disabled")]
        [DataRow("stopped")]
        public async Task InactiveCommandsNeverReadHostileModelsOrChangeAnExistingSnapshot(string state)
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = state == "default_disabled" ? CliTelemetrySession.Create() : CreateSession(exporter);
            if (state != "default_disabled")
            {
                Assert.IsTrue(session.CanObserveCommand);
                session.ObserveConfigurationDetails(ROOT, CreateConfig());
            }

            switch (state)
            {
                case "completed":
                    Complete(session);
                    break;
                case "disabled":
                    session.Disable();
                    break;
                case "stopped":
                    await session.StopAsync().WaitAsync(_timeout);
                    break;
            }

            Assert.IsFalse(session.CanObserveCommand);
            Assert.AreEqual(state == "completed", session.IsEnabled);
            CliTelemetryConfigurationSnapshot? retained = StoredSnapshot(session);
            Assert.AreEqual(state == "completed", retained is not null);
            int reads = 0;
            int referenceReads = 0;
            RuntimeConfig hostile = new ObservedRuntimeConfig(CreateConfig(), () =>
            {
                reads++;
                throw new InvalidOperationException("An inactive command must not inspect entities.");
            });
            RuntimeConfig hostileReferences = CreateConfig() with
            {
                DataSourceFiles = new(Enumerable.Repeat(SENTINEL, 1).Select<string, string>(_ =>
                {
                    referenceReads++;
                    throw new InvalidOperationException("An inactive command must not enumerate source declarations.");
                }))
            };
            session.ObserveConfigurationDetails(ROOT, hostile);
            session.ObserveConfigurationDetails(null, hostile, saved: true);
            session.ObserveConfigurationDetails(ROOT, hostileReferences);
            session.ObserveDatabaseType(OTHER_ROOT, CliTelemetryDatabaseType.MySql);
            Complete(session);
            Assert.AreEqual(0, reads, "Catching a hostile getter is not equivalent to skipping it.");
            Assert.AreEqual(0, referenceReads, "Neither provider nor snapshot projection may enumerate a disabled command's model.");
            Assert.AreSame(retained, StoredSnapshot(session));
            await session.StopAsync().WaitAsync(_timeout);
            Assert.IsNull(StoredSnapshot(session));
            Assert.AreEqual(state == "completed" ? 1 : 0, exporter.Records.Count);
            if (state == "completed")
            {
                Assert.AreEqual(retained!.Json, exporter.Records.Single().Properties["configuration_context"]);
                Assert.AreEqual("mssql", exporter.Records.Single().Properties["database_type"]);
            }
        }

        [DataTestMethod]
        [DataRow(null, false)]
        [DataRow("", false)]
        [DataRow(" \t ", false)]
        [DataRow(OTHER_ROOT, false)]
        [DataRow("private_cli_context_b6d82_root", false)]
        [DataRow(null, true)]
        [DataRow("", true)]
        public async Task UnknownOrDifferentRootsPermanentlyClearContextAndLinkageWithoutProjection(string? other, bool unknownFirst)
        {
            CapturingExporter exporter = new();
            int lookups = 0;
            int reads = 0;
            using CliTelemetrySession session = CreateSession(exporter,
                lookupIdentity: _ => { lookups++; return new(_apiId, "reused"); });
            RuntimeConfig hostile = new ObservedRuntimeConfig(CreateConfig(), () =>
            {
                reads++;
                throw new InvalidOperationException("An ambiguous root must not project a model.");
            });
            if (unknownFirst)
            {
                session.ObserveConfigurationDetails(other, hostile);
            }

            session.ObserveConfiguration(ROOT);
            session.ObserveConfigurationDetails(ROOT, CreateConfig());
            if (!unknownFirst)
            {
                Assert.IsNotNull(StoredSnapshot(session));
                session.ObserveConfigurationDetails(other, hostile);
            }

            Assert.IsNull(StoredSnapshot(session));
            session.ObserveConfigurationDetails(ROOT, CreateConfig(DatabaseType.PostgreSQL), saved: true);
            session.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MySql);
            session.ObserveConfiguration(ROOT);
            Complete(session);
            await session.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual(0, reads);
            Assert.AreEqual(unknownFirst ? 0 : 1, lookups);
            CliTelemetryEvent command = exporter.Records.Single();
            AssertUnknownContext(command);
            Assert.AreEqual("unknown", command.Properties["database_type"]);
            AssertNoApiIdentity(command);
        }

        [TestMethod]
        public async Task DifferentRootsWithTheSameSavedIdentityStillInvalidateCommandContext()
        {
            CapturingExporter exporter = new();
            List<string?> created = new();
            using CliTelemetrySession session = CreateSession(exporter, createIdentity: path =>
            {
                created.Add(path);
                return new(_apiId, "reused");
            });
            session.ConfigurationCreated(ROOT);
            session.ObserveConfigurationDetails(ROOT, CreateConfig(), saved: true);
            session.ConfigurationCreated(OTHER_ROOT);
            Assert.IsNull(StoredSnapshot(session));
            session.ObserveConfigurationDetails(ROOT, CreateConfig(), saved: true);
            Complete(session);
            await session.StopAsync().WaitAsync(_timeout);
            CollectionAssert.AreEqual(new[] { ROOT, OTHER_ROOT }, created.ToArray());
            CliTelemetryEvent command = exporter.Records.Single();
            AssertUnknownContext(command);
            Assert.AreEqual("unknown", command.Properties["database_type"]);
            AssertNoApiIdentity(command);
        }

        [DataTestMethod]
        [DataRow("missing")]
        [DataRow("empty_id")]
        [DataRow("invalid_stability")]
        public async Task UnavailableLookupDoesNotDiscardLoadedDetailsOrCreateAnIdentity(string evidence)
        {
            CapturingExporter exporter = new();
            int lookups = 0;
            int creates = 0;
            using CliTelemetrySession session = CreateSession(exporter,
                createIdentity: _ => { creates++; return new(_apiId, "newly_saved"); },
                lookupIdentity: _ =>
                {
                    lookups++;
                    return evidence switch
                    {
                        "empty_id" => new(Guid.Empty, "reused"),
                        "invalid_stability" => new(_apiId, SENTINEL),
                        _ => null
                    };
                });
            RuntimeConfig config = CreateConfig();
            session.ObserveConfigurationDetails(ROOT, config);
            session.ObserveConfiguration(ROOT);
            session.ObserveConfiguration(ROOT);
            Assert.IsTrue(session.CanObserveCommand);
            session.Complete("add", "none", ImmutableDictionary<string, string>.Empty,
                CliTelemetryOutcome.ValidationFailure, CliTelemetryFailureCategory.Configuration);
            await session.StopAsync().WaitAsync(_timeout);
            CliTelemetryEvent command = exporter.Records.Single();
            AssertMatchesEngine(command, EngineTelemetrySnapshotFactory.Create(config));
            Assert.AreEqual("mssql", command.Properties["database_type"]);
            Assert.AreEqual("validation_failure", command.Properties["outcome"]);
            Assert.AreEqual("configuration", command.Properties["failure_category"]);
            AssertNoApiIdentity(command);
            Assert.AreEqual(1, lookups);
            Assert.AreEqual(0, creates);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ThrowingIdentityCallbackDisablesAndForgetsPreviouslyProjectedDetails(bool create)
        {
            CapturingExporter exporter = new();
            int identities = 0;
            int reads = 0;
            EngineTelemetryIdentity Fail(string? path)
            {
                identities++;
                throw new InvalidOperationException(SENTINEL);
            }

            using CliTelemetrySession session = CreateSession(exporter, createIdentity: Fail, lookupIdentity: Fail);
            session.ObserveConfigurationDetails(ROOT, CreateConfig());
            Assert.IsNotNull(StoredSnapshot(session));
            if (create)
            {
                session.ConfigurationCreated(ROOT);
            }
            else
            {
                session.ObserveConfiguration(ROOT);
            }

            Assert.IsFalse(session.CanObserveCommand);
            Assert.IsFalse(session.IsEnabled);
            Assert.IsNull(StoredSnapshot(session));
            session.ObserveConfigurationDetails(ROOT, new ObservedRuntimeConfig(CreateConfig(), () =>
            {
                reads++;
                throw new InvalidOperationException();
            }));
            session.ObserveConfiguration(ROOT);
            session.ConfigurationCreated(ROOT);
            Complete(session);
            await session.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual(1, identities);
            Assert.AreEqual(0, reads);
            Assert.AreEqual(0, exporter.Acquisitions);
            Assert.AreEqual(0, exporter.Records.Count);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task MissingModelOrUnloadedDeclarationsReplaceOldDetailsWithoutInvalidatingAKnownRoot(bool unloaded)
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter);
            RuntimeConfig config = CreateConfig();
            session.ObserveConfiguration(ROOT);
            session.ObserveConfigurationDetails(ROOT, config);
            // A clone attaches declarations without invoking RuntimeConfig's file-loading constructor.
            RuntimeConfig? unavailable = unloaded ? config with { DataSourceFiles = new([SENTINEL + ".json"]) } : null;
            session.ObserveConfigurationDetails(ROOT, unavailable, saved: true);
            Assert.AreSame(CliTelemetryConfigurationSnapshot.Unknown, StoredSnapshot(session));
            Assert.AreEqual(0, config.ChildConfigs.Count);
            Complete(session);
            await session.StopAsync().WaitAsync(_timeout);
            CliTelemetryEvent command = exporter.Records.Single();
            AssertUnknownContext(command);
            Assert.AreEqual("unknown", command.Properties["database_type"]);
            Assert.AreEqual(_apiId.ToString("D"), command.Properties["dab_api_id"], "A missing model is not an unknown root.");
        }

        [TestMethod]
        public async Task RetainedSnapshotAndCompletedCommandAreImmutableAfterCloningAndMutatingTheModel()
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter);
            Dictionary<string, Entity> entities = new() { [SENTINEL] = NewEntity() };
            RuntimeConfig config = CreateConfig(entities: entities);
            ImmutableDictionary<string, string> expected = EngineTelemetrySnapshotFactory.Create(config);
            session.ObserveConfigurationDetails(ROOT, config);
            CliTelemetryConfigurationSnapshot? snapshot = StoredSnapshot(session);
            Assert.IsNotNull(snapshot);
            string json = snapshot.Json;

            RuntimeConfig clone = config with { DataSource = new(DatabaseType.MySQL, SENTINEL) };
            clone.Runtime!.Host!.Mode = HostMode.Production;
            config.DataSource!.Options!["set-session-context"] = true;
            entities.Clear();
            Dictionary<string, string> changed = Context(CliTelemetryConfigurationSnapshot.Create(clone).Json);
            Assert.AreEqual("loaded", changed["observation"]);
            Assert.AreEqual("mysql", changed["data_sources.types"]);
            Assert.AreEqual("production", changed["host.mode.effective"]);
            Assert.AreEqual("0", changed["scale.entity_count"]);
            Assert.AreSame(snapshot, StoredSnapshot(session));
            Assert.AreSame(json, snapshot.Json);
            Complete(session);
            session.ObserveConfigurationDetails(ROOT, clone, saved: true);
            session.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MySql);
            Complete(session);
            Assert.AreSame(snapshot, StoredSnapshot(session));
            await session.StopAsync().WaitAsync(_timeout);

            CliTelemetryEvent command = exporter.Records.Single();
            AssertMatchesEngine(command, expected);
            Assert.AreSame(json, command.Properties["configuration_context"]);
            Assert.AreEqual("mssql", command.Properties["database_type"]);
            Assert.IsNull(StoredSnapshot(session));
            Type snapshotType = typeof(CliTelemetryConfigurationSnapshot);
            Assert.IsTrue(snapshotType.IsSealed);
            FieldInfo[] fields = snapshotType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.AreEqual(1, fields.Length, "No model, JSON document, delegate or raw configuration may be retained.");
            Assert.AreEqual(typeof(string), fields[0].FieldType);
            Assert.IsTrue(fields[0].IsInitOnly);
            PropertyInfo[] properties = snapshotType.GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.AreEqual(1, properties.Length);
            Assert.AreEqual(nameof(CliTelemetryConfigurationSnapshot.Json), properties[0].Name);
            Assert.IsFalse(properties[0].CanWrite);
        }

        [TestMethod]
        public async Task OptionalProjectionFailureReplacesOldContextWithoutFailingTheCommand()
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter);
            session.ObserveConfigurationDetails(ROOT, CreateConfig());
            int reads = 0;
            RuntimeConfig hostile = new ObservedRuntimeConfig(CreateConfig(DatabaseType.PostgreSQL), () =>
            {
                reads++;
                throw new InvalidOperationException(SENTINEL);
            });
            session.ObserveConfigurationDetails(ROOT, hostile, saved: true);
            Assert.AreEqual(1, reads);
            Assert.IsTrue(session.CanObserveCommand);
            Assert.IsFalse(session.HasFailure);
            Assert.AreSame(CliTelemetryConfigurationSnapshot.Unknown, StoredSnapshot(session));
            Complete(session);
            await session.StopAsync().WaitAsync(_timeout);
            CliTelemetryEvent command = exporter.Records.Single();
            AssertUnknownContext(command);
            Assert.AreEqual("postgresql", command.Properties["database_type"], "The independently projected provider remains usable.");
            Assert.AreEqual("success", command.Properties["outcome"]);
        }

        [TestMethod]
        public async Task DirectProviderObservationInvalidatesAnAlreadyStoredConfigurationContext()
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter);
            session.ObserveConfigurationDetails(ROOT, CreateConfig());
            session.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MySql);
            Assert.IsNull(StoredSnapshot(session));
            Complete(session);
            await session.StopAsync().WaitAsync(_timeout);
            CliTelemetryEvent command = exporter.Records.Single();
            Assert.AreEqual("mysql", command.Properties["database_type"]);
            AssertUnknownContext(command);
        }

        [DataTestMethod]
        [DataRow("details")]
        [DataRow("provider")]
        [DataRow("complete")]
        [DataRow("disable")]
        [DataRow("stop")]
        [DataRow("unknown_root")]
        [DataRow("different_root")]
        public async Task PausedOlderProjectionHoldsNoSessionLockAndCannotOverwriteANewerDecision(string decision)
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter);
            session.ObserveConfiguration(ROOT);
            session.ObserveConfigurationDetails(ROOT, CreateConfig(DatabaseType.DWSQL));
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            RuntimeConfig original = CreateConfig();
            RuntimeConfig replacement = CreateConfig(DatabaseType.PostgreSQL, restEnabled: false);
            int reads = 0;
            RuntimeConfig paused = new ObservedRuntimeConfig(original, () =>
            {
                Interlocked.Increment(ref reads);
                entered.TrySetResult();
                // Longer than the competing-operation deadline, but never an unbounded wait.
                release.Task.WaitAsync(_projectionTimeout).GetAwaiter().GetResult();
                return original.Entities;
            });
            Task projection = Task.Run(() => session.ObserveConfigurationDetails(ROOT, paused));
            Task? competing = null;
            CliTelemetryConfigurationSnapshot? winner = null;
            try
            {
                await entered.Task.WaitAsync(_timeout);
                Assert.IsNull(StoredSnapshot(session), "An in-flight replacement must not expose the old snapshot.");
                competing = Task.Run(async () =>
                {
                    switch (decision)
                    {
                        case "details":
                            session.ObserveConfigurationDetails(ROOT, replacement, saved: true);
                            break;
                        case "provider":
                            session.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MySql);
                            break;
                        case "complete":
                            Complete(session);
                            break;
                        case "disable":
                            session.Disable();
                            break;
                        case "stop":
                            await session.StopAsync();
                            break;
                        case "unknown_root":
                            session.ObserveConfiguration(null);
                            break;
                        case "different_root":
                            session.ObserveConfigurationDetails(OTHER_ROOT, replacement);
                            break;
                    }
                });
                await competing.WaitAsync(_timeout);
                Assert.IsFalse(projection.IsCompleted, "A competing operation must finish before the projection is released.");
                winner = StoredSnapshot(session);
                Assert.AreEqual(decision == "details", winner is not null);
            }
            finally
            {
                release.TrySetResult();
                await Task.WhenAll(projection, competing ?? Task.CompletedTask).WaitAsync(_timeout);
            }

            Assert.AreEqual(1, reads);
            Assert.AreSame(winner, StoredSnapshot(session), "A stale generation cannot restore either provider or context.");
            Complete(session);
            await session.StopAsync().WaitAsync(_timeout);
            if (decision is "disable" or "stop")
            {
                Assert.IsFalse(session.CanObserveCommand);
                Assert.IsNull(StoredSnapshot(session));
                Assert.AreEqual(0, exporter.Records.Count);
                return;
            }

            CliTelemetryEvent command = exporter.Records.Single();
            if (decision == "details")
            {
                AssertMatchesEngine(command, EngineTelemetrySnapshotFactory.Create(replacement), saved: true);
                Assert.AreEqual("postgresql", command.Properties["database_type"]);
            }
            else
            {
                AssertUnknownContext(command);
                Assert.AreEqual(decision == "provider" ? "mysql" : "unknown", command.Properties["database_type"]);
            }

            if (decision is "unknown_root" or "different_root")
            {
                AssertNoApiIdentity(command);
            }
            else
            {
                Assert.AreEqual(_apiId.ToString("D"), command.Properties["dab_api_id"]);
            }
        }

        [TestMethod]
        public async Task DisableAfterNormalStopRevokesReservedLaunchAndCannotReviveContext()
        {
            CapturingExporter exporter = new();
            int identities = 0;
            int reads = 0;
            using CliTelemetrySession session = CreateSession(exporter,
                createIdentity: _ => { identities++; return new(_apiId, "reused"); },
                lookupIdentity: _ => { identities++; return new(_apiId, "reused"); });
            session.ObserveConfigurationDetails(ROOT, CreateConfig());
            using CliTelemetryLaunchReservation? reservation = session.ReserveEngineLaunch(CliTelemetryLaunchSource.ExportGraphQL);
            Assert.IsNotNull(reservation);
            await session.StopAsync().WaitAsync(_timeout);
            Assert.IsNull(StoredSnapshot(session));
            Assert.IsFalse(session.CanObserveCommand);
            Assert.IsTrue(session.CanBeginReservedLaunch, "Normal stop alone preserves an already reserved helper.");
            session.Disable();
            Assert.IsFalse(session.CanBeginReservedLaunch);
            Assert.IsNull(reservation.Begin(ROOT));
            session.ObserveConfigurationDetails(ROOT, new ObservedRuntimeConfig(CreateConfig(), () =>
            {
                reads++;
                throw new InvalidOperationException();
            }), saved: true);
            session.ObserveConfiguration(ROOT);
            session.ConfigurationCreated(ROOT);
            Complete(session);
            await session.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual(0, reads);
            Assert.AreEqual(0, identities);
            Assert.AreEqual(0, exporter.Acquisitions);
            Assert.AreEqual(0, exporter.Records.Count);
            Assert.IsNull(StoredSnapshot(session));
        }

        [TestMethod]
        public async Task UnobservedCommandStillCarriesTheWholeUnknownSchema()
        {
            CapturingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter);
            Complete(session);
            await session.StopAsync().WaitAsync(_timeout);
            CliTelemetryEvent command = exporter.Records.Single();
            AssertUnknownContext(command);
            Assert.AreEqual("unknown", command.Properties["database_type"]);
            AssertNoApiIdentity(command);
        }

        private static RuntimeConfig CreateConfig(DatabaseType provider = DatabaseType.MSSQL, bool restEnabled = true,
            Dictionary<string, Entity>? entities = null)
        {
            DataSource source = new(provider, SENTINEL, new() { ["set-session-context"] = false });
            RuntimeOptions runtime = new(new(Enabled: restEnabled), null, null, new(null, null, HostMode.Development));
            return new(SENTINEL, source, runtime, RawEntities(entities ?? new() { [SENTINEL] = NewEntity() }),
                ROOT, new() { [ROOT] = source }, new());
        }

        private static Entity NewEntity(EntitySourceType type = EntitySourceType.Table)
            => new(new(SENTINEL, type, null, null), new(SENTINEL, SENTINEL), null, new(), [], null, null);

        private static RuntimeEntities RawEntities(IReadOnlyDictionary<string, Entity> entities)
            => new(new Dictionary<string, Entity>()) { Entities = entities };

        private static CliTelemetrySession CreateSession(CapturingExporter exporter, bool firstRun = false,
            Func<string?, EngineTelemetryIdentity>? createIdentity = null, Func<string?, EngineTelemetryIdentity?>? lookupIdentity = null)
            => CliTelemetrySession.Create(exporter.Acquire, enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveInstallation: () => new(_installationId, firstRun ? "newly_saved" : "reused"),
                createIdentity: createIdentity ?? (_ => new(_apiId, "reused")),
                lookupIdentity: lookupIdentity ?? (_ => new(_apiId, "reused")));

        private static void Complete(CliTelemetrySession session)
            => session.Complete("configure", "none", ImmutableDictionary<string, string>.Empty, CliTelemetryOutcome.Success);

        // Only inspect the exact retained slot to prove immediate invalidation/forgetting;
        // emitted command assertions independently check the observable wire behavior.
        private static CliTelemetryConfigurationSnapshot? StoredSnapshot(CliTelemetrySession session)
            => (CliTelemetryConfigurationSnapshot?)typeof(CliTelemetrySession)
                .GetField("_configurationSnapshot", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(session);

        private static Dictionary<string, string> Context(string json)
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonProperty[] properties = document.RootElement.EnumerateObject().ToArray();
            Assert.AreEqual(83, properties.Length);
            Assert.IsTrue(properties.All(property => property.Value.ValueKind == JsonValueKind.String));
            Assert.IsTrue(json.Length <= CliTelemetryConfigurationSnapshot.MAX_JSON_CHARACTERS);
            Assert.IsFalse(json.Contains(SENTINEL, StringComparison.Ordinal));
            Dictionary<string, string> context = properties.ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal);
            Assert.AreEqual("cli-configuration-v1", context["snapshot_schema"]);
            CollectionAssert.AreEquivalent(EngineTelemetrySnapshotFactory.Create(CreateConfig()).Keys.Append("observation").ToArray(), context.Keys.ToArray());
            return context;
        }

        private static void AssertMatchesEngine(CliTelemetryEvent command, ImmutableDictionary<string, string> engine, bool saved = false)
        {
            Assert.AreEqual(COMMAND, command.Name);
            Dictionary<string, string> context = Context(command.Properties["configuration_context"]);
            Assert.AreEqual(saved ? "saved" : "loaded", context["observation"]);
            foreach ((string key, string value) in engine)
            {
                Assert.AreEqual(key == "snapshot_schema" ? "cli-configuration-v1" : value, context[key], key);
            }

            AssertNoPrivateValues([command]);
        }

        private static void AssertUnknownContext(CliTelemetryEvent command)
        {
            Dictionary<string, string> context = Context(command.Properties["configuration_context"]);
            Assert.IsTrue(context.Where(pair => pair.Key != "snapshot_schema").All(pair => pair.Value == "unknown"));
        }

        private static void AssertNoApiIdentity(CliTelemetryEvent command)
        {
            Assert.IsFalse(command.Properties.ContainsKey("dab_api_id"));
            Assert.IsFalse(command.Properties.ContainsKey("dab_api_id_stability"));
        }

        private static void AssertNoPrivateValues(IEnumerable<CliTelemetryEvent> records)
            => Assert.IsFalse(JsonSerializer.Serialize(records).Contains(SENTINEL, StringComparison.Ordinal));

        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed record ObservedRuntimeConfig(RuntimeConfig Original, Func<RuntimeEntities> ReadEntities) : RuntimeConfig(Original)
        {
            public override RuntimeEntities Entities
            {
                get => ReadEntities();
                init => base.Entities = value;
            }
        }

        private sealed class CapturingExporter : IProductTelemetryExporter<IProductTelemetryEvent>
        {
            private int _acquisitions;
            internal int Acquisitions => Volatile.Read(ref _acquisitions);
            internal ConcurrentQueue<CliTelemetryEvent> Records { get; } = new();

            internal IProductTelemetryExporter<IProductTelemetryEvent> Acquire()
            {
                Interlocked.Increment(ref _acquisitions);
                return this;
            }

            public ValueTask<bool> ExportAsync(IProductTelemetryEvent record, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Records.Enqueue((CliTelemetryEvent)record);
                return ValueTask.FromResult(true);
            }

            public void Dispose() { }
        }
    }
}
