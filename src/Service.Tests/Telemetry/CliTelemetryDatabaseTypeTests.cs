// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Globalization;
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
    /// Offline Core-only provider projection and immutable CLI event tests. File declarations
    /// are attached with record clones, never the file-loading RuntimeConfig constructor.
    /// No CLI options/handlers, config files, environment mutation, identity store, database,
    /// host or network exporter is used. Every exporter and identity callback is local.
    /// </summary>
    [TestClass]
    [TestCategory("CliTelemetry")]
    [TestCategory("EngineTelemetry")]
    public class CliTelemetryDatabaseTypeTests
    {
        private const string PRIVATE_VALUE = "PRIVATE_CLI_PROVIDER_2e871";
        private const string ROOT = PRIVATE_VALUE + "_root";
        private const string OTHER_ROOT = PRIVATE_VALUE + "_other_root";
        private const string CHILD_FILE = PRIVATE_VALUE + "_child.json";
        private const string FIRST_RUN = "dab.cli.first_run";
        private const string COMMAND = "dab.cli.command";
        private const string LAUNCH = "dab.cli.engine_launch";
        private static readonly Guid _installationId = new("ee126532-3854-44da-a71d-e5196f6d2f1e");
        private static readonly Guid _apiId = new("fa989ed3-4b0c-4097-a187-ecce354eb642");
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

        [DataTestMethod]
        [DataRow((int)CliTelemetryDatabaseType.Unknown, "unknown")]
        [DataRow((int)CliTelemetryDatabaseType.MsSql, "mssql")]
        [DataRow((int)CliTelemetryDatabaseType.DwSql, "dwsql")]
        [DataRow((int)CliTelemetryDatabaseType.PostgreSql, "postgresql")]
        [DataRow((int)CliTelemetryDatabaseType.MySql, "mysql")]
        [DataRow((int)CliTelemetryDatabaseType.CosmosDbNoSql, "cosmosdb_nosql")]
        [DataRow((int)CliTelemetryDatabaseType.CosmosDbPostgreSql, "cosmosdb_postgresql")]
        [DataRow((int)CliTelemetryDatabaseType.Multiple, "multiple")]
        [DataRow(-1, "unknown")]
        [DataRow(int.MinValue, "unknown")]
        [DataRow(int.MaxValue, "unknown")]
        public void WireUsesOnlyFixedLiterals(int value, string expected)
        {
            Assert.AreEqual(expected, CliTelemetryDatabaseTypeFormatter.Wire((CliTelemetryDatabaseType)value));
            Assert.AreEqual("unknown", CliTelemetryDatabaseTypeFormatter.Wire(default));
            Assert.AreEqual(0, (int)CliTelemetryDatabaseType.Unknown);
        }

        [DataTestMethod]
        [DataRow(DatabaseType.MSSQL, (int)CliTelemetryDatabaseType.MsSql)]
        [DataRow(DatabaseType.DWSQL, (int)CliTelemetryDatabaseType.DwSql)]
        [DataRow(DatabaseType.PostgreSQL, (int)CliTelemetryDatabaseType.PostgreSql)]
        [DataRow(DatabaseType.MySQL, (int)CliTelemetryDatabaseType.MySql)]
        [DataRow(DatabaseType.CosmosDB_NoSQL, (int)CliTelemetryDatabaseType.CosmosDbNoSql)]
        [DataRow(DatabaseType.CosmosDB_PostgreSQL, (int)CliTelemetryDatabaseType.CosmosDbPostgreSql)]
        public void ActualTypedProvidersAreProjectedWithoutInspectingValues(DatabaseType provider, int expected)
        {
            RuntimeConfig config = CreateConfig(provider);
            Assert.AreEqual((CliTelemetryDatabaseType)expected, CliTelemetryDatabaseTypeFormatter.FromConfiguration(config));
            Assert.AreEqual((CliTelemetryDatabaseType)expected,
                CliTelemetryDatabaseTypeFormatter.FromConfiguration(config with { DataSourceFiles = new() }));
            Assert.AreEqual((CliTelemetryDatabaseType)expected,
                CliTelemetryDatabaseTypeFormatter.FromConfiguration(config with { DataSourceFiles = new([]) }));
        }

        [TestMethod]
        public void DatabaseEnumDefaultIsCosmosButMissingEvidenceIsUnknown()
        {
            Assert.AreEqual(CliTelemetryDatabaseType.CosmosDbNoSql,
                CliTelemetryDatabaseTypeFormatter.FromConfiguration(CreateConfig(default(DatabaseType))));
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, CliTelemetryDatabaseTypeFormatter.FromConfiguration(null));
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, CliTelemetryDatabaseTypeFormatter.FromConfiguration(CreateConfig(null)));
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, CliTelemetryDatabaseTypeFormatter.FromConfiguration(CreateMappedConfig(new())));
        }

        [DataTestMethod]
        [DataRow(-1)]
        [DataRow(int.MinValue)]
        [DataRow(int.MaxValue)]
        public void InvalidTypedProviderInvalidatesEvenAnOtherwiseKnownMixedMap(int value)
        {
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown,
                CliTelemetryDatabaseTypeFormatter.FromConfiguration(CreateConfig((DatabaseType)value)));
            RuntimeConfig config = Merge(CreateSource(DatabaseType.MSSQL),
                (CHILD_FILE, CreateConfig(DatabaseType.MySQL)),
                (PRIVATE_VALUE + "_invalid.json", CreateConfig((DatabaseType)value)));
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, CliTelemetryDatabaseTypeFormatter.FromConfiguration(config),
                "Do not return multiple before checking every source for invalid evidence.");
        }

        [TestMethod]
        public void NullMergedSourceIsUnknownRatherThanAPartialProvider()
        {
            Dictionary<string, DataSource> sources = new() { [PRIVATE_VALUE] = CreateSource(DatabaseType.MSSQL) };
            RuntimeConfig config = CreateMappedConfig(sources);
            // Inject after construction: RuntimeConfig's own setup dereferences source values.
            sources[OTHER_ROOT] = null!;
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, CliTelemetryDatabaseTypeFormatter.FromConfiguration(config));
        }

        [DataTestMethod]
        [DataRow(false, false, (int)CliTelemetryDatabaseType.PostgreSql)]
        [DataRow(true, false, (int)CliTelemetryDatabaseType.PostgreSql)]
        [DataRow(false, true, (int)CliTelemetryDatabaseType.Multiple)]
        [DataRow(true, true, (int)CliTelemetryDatabaseType.Multiple)]
        public void LoadedChildrenUseDistinctProvidersNotSourceCounts(bool rootSource, bool mixed, int expected)
        {
            RuntimeConfig config = Merge(rootSource ? CreateSource(DatabaseType.PostgreSQL) : null,
                (CHILD_FILE, CreateConfig(DatabaseType.PostgreSQL)),
                (PRIVATE_VALUE + "_second.json", CreateConfig(mixed ? DatabaseType.MySQL : DatabaseType.PostgreSQL)));
            Assert.AreEqual((CliTelemetryDatabaseType)expected, CliTelemetryDatabaseTypeFormatter.FromConfiguration(config));
        }

        [TestMethod]
        public void ExplicitMergedMapDoesNotRequireChildRecordsWhenThereAreNoDeclarations()
        {
            Dictionary<string, DataSource> sources = new()
            {
                [ROOT] = CreateSource(DatabaseType.MSSQL),
                [OTHER_ROOT] = CreateSource(DatabaseType.MSSQL)
            };
            RuntimeConfig config = CreateMappedConfig(sources);
            Assert.AreEqual(0, config.ChildConfigs.Count);
            Assert.IsNull(config.DataSourceFiles);
            Assert.AreEqual(CliTelemetryDatabaseType.MsSql, CliTelemetryDatabaseTypeFormatter.FromConfiguration(config));
            sources[OTHER_ROOT] = CreateSource(DatabaseType.DWSQL);
            Assert.AreEqual(CliTelemetryDatabaseType.Multiple, CliTelemetryDatabaseTypeFormatter.FromConfiguration(config));
        }

        [TestMethod]
        public void RootRecordCloneOverridesTheStaleDefaultMapWithoutMutatingIt()
        {
            RuntimeConfig original = CreateConfig(DatabaseType.MSSQL);
            RuntimeConfig updated = original with { DataSource = CreateSource(DatabaseType.PostgreSQL) };
            Assert.AreEqual(DatabaseType.MSSQL, updated.GetDataSourceFromDataSourceName(updated.DefaultDataSourceName).DatabaseType);
            Assert.AreEqual(CliTelemetryDatabaseType.PostgreSql, CliTelemetryDatabaseTypeFormatter.FromConfiguration(updated));
            Assert.AreEqual(CliTelemetryDatabaseType.MsSql, CliTelemetryDatabaseTypeFormatter.FromConfiguration(original));
            Assert.AreEqual(DatabaseType.MSSQL, updated.GetDataSourceFromDataSourceName(updated.DefaultDataSourceName).DatabaseType);
        }

        [TestMethod]
        public void RootCloneCanMakeMixedSourcesUniformOrUniformSourcesMixed()
        {
            RuntimeConfig mixed = Merge(CreateSource(DatabaseType.MSSQL), (CHILD_FILE, CreateConfig(DatabaseType.PostgreSQL)));
            RuntimeConfig uniform = mixed with { DataSource = CreateSource(DatabaseType.PostgreSQL) };
            Assert.AreEqual(CliTelemetryDatabaseType.Multiple, CliTelemetryDatabaseTypeFormatter.FromConfiguration(mixed));
            Assert.AreEqual(CliTelemetryDatabaseType.PostgreSql, CliTelemetryDatabaseTypeFormatter.FromConfiguration(uniform));
            Assert.AreEqual(CliTelemetryDatabaseType.Multiple,
                CliTelemetryDatabaseTypeFormatter.FromConfiguration(uniform with { DataSource = CreateSource(DatabaseType.MySQL) }));
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown,
                CliTelemetryDatabaseTypeFormatter.FromConfiguration(uniform with { DataSource = CreateSource((DatabaseType)(-1)) }));
        }

        [TestMethod]
        public void ValidRootSupersedesEvenAnInvalidStaleDefaultEntry()
        {
            RuntimeConfig original = CreateConfig((DatabaseType)(-1));
            RuntimeConfig saved = original with { DataSource = CreateSource(DatabaseType.DWSQL) };
            Assert.AreEqual(CliTelemetryDatabaseType.DwSql, CliTelemetryDatabaseTypeFormatter.FromConfiguration(saved));
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, CliTelemetryDatabaseTypeFormatter.FromConfiguration(original));
        }

        [TestMethod]
        public void UnloadedReferencesOnARecordCloneAreUnknownWithoutLoadingThem()
        {
            RuntimeConfig original = CreateConfig(DatabaseType.MSSQL);
            RuntimeConfig withUnloadedReference = original with { DataSourceFiles = new([CHILD_FILE]) };
            Assert.AreEqual(0, withUnloadedReference.ChildConfigs.Count);
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, CliTelemetryDatabaseTypeFormatter.FromConfiguration(withUnloadedReference));
            Assert.AreEqual(CliTelemetryDatabaseType.MsSql, CliTelemetryDatabaseTypeFormatter.FromConfiguration(original));
        }

        [DataTestMethod]
        [DataRow("changed")]
        [DataRow("added")]
        [DataRow("empty")]
        [DataRow("null_list")]
        [DataRow("removed")]
        public void ChangedFileDeclarationsCannotReuseStaleLoadedChildren(string change)
        {
            RuntimeConfig loaded = Merge(CreateSource(DatabaseType.MSSQL), (CHILD_FILE, CreateConfig(DatabaseType.MySQL)));
            RuntimeConfig changed = loaded with
            {
                DataSourceFiles = change switch
                {
                    "changed" => new([OTHER_ROOT]),
                    "added" => new([CHILD_FILE, OTHER_ROOT]),
                    "empty" => new([]),
                    "null_list" => new(),
                    _ => null
                }
            };
            Assert.AreSame(loaded.ChildConfigs, changed.ChildConfigs, "The record clone retains the old loaded evidence.");
            Assert.AreEqual(CliTelemetryDatabaseType.Multiple, CliTelemetryDatabaseTypeFormatter.FromConfiguration(loaded));
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, CliTelemetryDatabaseTypeFormatter.FromConfiguration(changed));
        }

        [TestMethod]
        public void ReorderedDeclarationsDoNotMatchTheOriginalLoaderOrder()
        {
            RuntimeConfig loaded = Merge(null,
                (CHILD_FILE, CreateConfig(DatabaseType.MSSQL)),
                (OTHER_ROOT, CreateConfig(DatabaseType.MySQL)));
            RuntimeConfig changed = loaded with { DataSourceFiles = new([OTHER_ROOT, CHILD_FILE]) };
            Assert.AreEqual(CliTelemetryDatabaseType.Multiple, CliTelemetryDatabaseTypeFormatter.FromConfiguration(loaded));
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, CliTelemetryDatabaseTypeFormatter.FromConfiguration(changed));
        }

        [DataTestMethod]
        [DataRow("null_config")]
        [DataRow("null_name")]
        [DataRow("blank_name")]
        [DataRow("mismatched_name")]
        [DataRow("extra_child")]
        public void InvalidOrUnmatchedLoadedChildEvidenceIsUnknown(string invalid)
        {
            RuntimeConfig child = CreateConfig(DatabaseType.MSSQL);
            RuntimeConfig config = Merge(CreateSource(DatabaseType.MSSQL), (CHILD_FILE, child));
            if (invalid == "extra_child")
            {
                config.ChildConfigs.Add((OTHER_ROOT, CreateConfig(DatabaseType.MySQL)));
            }
            else
            {
                config.ChildConfigs[0] = invalid switch
                {
                    "null_config" => (CHILD_FILE, null!),
                    "null_name" => (null!, child),
                    "blank_name" => (" \t ", child),
                    _ => (OTHER_ROOT, child)
                };
            }

            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, CliTelemetryDatabaseTypeFormatter.FromConfiguration(config));
        }

        [TestMethod]
        public void NestedDeclarationsMustAlsoMatchTheActuallyLoadedChildren()
        {
            RuntimeConfig nested = Merge(null, (CHILD_FILE, CreateConfig(DatabaseType.CosmosDB_NoSQL)));
            RuntimeConfig root = Merge(null, (OTHER_ROOT, nested));
            Assert.AreEqual(CliTelemetryDatabaseType.CosmosDbNoSql, CliTelemetryDatabaseTypeFormatter.FromConfiguration(root));
            root.ChildConfigs[0] = (OTHER_ROOT, nested with { DataSourceFiles = new([CHILD_FILE, PRIVATE_VALUE + "_missing.json"]) });
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, CliTelemetryDatabaseTypeFormatter.FromConfiguration(root));
        }

        [TestMethod]
        public void CyclicConfigReferencesFailClosedWithoutRecursiveTraversal()
        {
            RuntimeConfig cyclic = CreateConfig(DatabaseType.MSSQL) with { DataSourceFiles = new([CHILD_FILE]) };
            cyclic.ChildConfigs.Add((CHILD_FILE, cyclic));
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, CliTelemetryDatabaseTypeFormatter.FromConfiguration(cyclic));
        }

        [DataTestMethod]
        [DataRow("get_enumerator")]
        [DataRow("move_next")]
        [DataRow("current")]
        [DataRow("dispose")]
        public void OptionalProjectionEnumerationErrorsReturnUnknown(string failure)
        {
            RuntimeConfig config = Merge(CreateSource(DatabaseType.MSSQL), (CHILD_FILE, CreateConfig(DatabaseType.MSSQL))) with
            {
                DataSourceFiles = new(new FaultingReferences(failure))
            };
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, CliTelemetryDatabaseTypeFormatter.FromConfiguration(config));
        }

        [DataTestMethod]
        [DataRow(255, false, true)]
        [DataRow(255, true, true)]
        [DataRow(256, false, false)]
        [DataRow(256, true, false)]
        [DataRow(1024, false, false)]
        public void SourceBudgetIncludesTheRootConfigButDoesNotDoubleCountItsDefault(int sourceCount, bool rootSource, bool known)
        {
            Dictionary<string, DataSource> sources = Enumerable.Range(0, sourceCount)
                .ToDictionary(index => index.ToString(CultureInfo.InvariantCulture), _ => CreateSource(DatabaseType.MSSQL));
            RuntimeConfig config = CreateMappedConfig(sources, rootSource ? sources["0"] : null, defaultName: "0");
            Assert.AreEqual(known ? CliTelemetryDatabaseType.MsSql : CliTelemetryDatabaseType.Unknown,
                CliTelemetryDatabaseTypeFormatter.FromConfiguration(config), "The shared budget is 256 config/source nodes.");
        }

        [DataTestMethod]
        [DataRow(255, true)]
        [DataRow(256, false)]
        [DataRow(1024, false)]
        public void ConfigDepthSharesTheBudgetWithTheOneMergedSource(int configCount, bool known)
        {
            RuntimeConfig config = CreateConfig(DatabaseType.MSSQL);
            for (int index = 1; index < configCount; index++)
            {
                config = Merge(null, (CHILD_FILE, config));
            }

            Assert.AreEqual(known ? CliTelemetryDatabaseType.MsSql : CliTelemetryDatabaseType.Unknown,
                CliTelemetryDatabaseTypeFormatter.FromConfiguration(config));
        }

        [DataTestMethod]
        [DataRow(127, true)]
        [DataRow(128, false)]
        [DataRow(257, false)]
        public void ConfigBreadthAndSourcesShareOneBudget(int childCount, bool known)
        {
            (string, RuntimeConfig)[] children = Enumerable.Range(0, childCount)
                .Select(index => (index.ToString(CultureInfo.InvariantCulture), CreateConfig(DatabaseType.MSSQL))).ToArray();
            RuntimeConfig config = Merge(null, children);
            Assert.AreEqual(known ? CliTelemetryDatabaseType.MsSql : CliTelemetryDatabaseType.Unknown,
                CliTelemetryDatabaseTypeFormatter.FromConfiguration(config));
        }

        [TestMethod]
        public void AnUnboundedDeclarationSequenceIsNotMaterializedOrExhaustivelyWalked()
        {
            int moves = 0;
            int disposals = 0;
            IEnumerable<string> References()
            {
                try
                {
                    while (true)
                    {
                        moves++;
                        yield return CHILD_FILE;
                    }
                }
                finally
                {
                    disposals++;
                }
            }

            RuntimeConfig config = CreateConfig(DatabaseType.MSSQL) with { DataSourceFiles = new(References()) };
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, CliTelemetryDatabaseTypeFormatter.FromConfiguration(config));
            Assert.AreEqual(1, moves, "One unmatched reference already proves incompleteness.");
            Assert.AreEqual(1, disposals);
        }

        [TestMethod]
        public async Task FirstRunRemainsUnknownWhileTheLaterCommandUsesLoadedEvidence()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            RecordingFactory factory = new(async (record, token) =>
            {
                if (record.Name == FIRST_RUN)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                }

                return true;
            });
            using CliTelemetrySession cli = CreateSession(factory, installation: new(_installationId, "newly_saved"));
            try
            {
                await entered.Task.WaitAsync(_timeout);
                cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseTypeFormatter.FromConfiguration(CreateConfig(DatabaseType.PostgreSQL)));
                Complete(cli);
                cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MySql);
            }
            finally
            {
                release.TrySetResult();
            }

            await cli.StopAsync().WaitAsync(_timeout);
            CliTelemetryEvent[] records = factory.Records.ToArray();
            CollectionAssert.AreEqual(new[] { FIRST_RUN, COMMAND }, records.Select(record => record.Name).ToArray());
            Assert.AreEqual("unknown", records[0].Properties["database_type"]);
            Assert.AreEqual("postgresql", records[1].Properties["database_type"]);
            AssertPrivateValuesAbsent(records);
        }

        [TestMethod]
        public async Task ObservationDoesNoIdentityClockEnvironmentOrExporterWorkAndSameRootCanChange()
        {
            RecordingFactory factory = new();
            CountingClock clock = new();
            int environments = 0;
            int creates = 0;
            int lookups = 0;
            using CliTelemetrySession cli = CliTelemetrySession.Create(factory.Acquire, true, clock,
                readEnvironmentVariable: _ => { environments++; return null; }, showNotice: () => { },
                resolveInstallation: () => new(_installationId, "reused"),
                createIdentity: _ => { creates++; return new(_apiId, "ephemeral"); },
                lookupIdentity: _ => { lookups++; return new(_apiId, "reused"); });
            int environmentCalls = environments;
            int clockCalls = clock.Calls;
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MsSql);
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.DwSql);

            Assert.AreEqual(0, creates);
            Assert.AreEqual(0, lookups);
            Assert.AreEqual(0, factory.Senders.Count);
            Assert.AreEqual(0, factory.Records.Count);
            Assert.AreEqual(environmentCalls, environments);
            Assert.AreEqual(clockCalls, clock.Calls);
            Assert.AreEqual(CliTelemetryDatabaseType.DwSql, StoredProvider(cli));

            cli.ObserveConfiguration(ROOT);
            cli.ObserveConfiguration(ROOT);
            Complete(cli);
            await cli.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual(1, lookups, "Provider observation neither resolves nor primes identity lookup.");
            Assert.AreEqual(0, creates, "A lookup-only command stays lookup-only.");
            Assert.AreEqual("dwsql", factory.Records.Single().Properties["database_type"]);
            Assert.AreEqual(_apiId.ToString("D"), factory.Records.Single().Properties["dab_api_id"]);
        }

        [TestMethod]
        public async Task SameRootIncompleteSavedCloneReplacesThePreviousKnownProvider()
        {
            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateSession(factory);
            RuntimeConfig original = CreateConfig(DatabaseType.MSSQL);
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseTypeFormatter.FromConfiguration(original));
            Assert.AreEqual(CliTelemetryDatabaseType.MsSql, StoredProvider(cli));
            RuntimeConfig saved = original with { DataSourceFiles = new([CHILD_FILE]) };
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseTypeFormatter.FromConfiguration(saved));
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, StoredProvider(cli));
            Assert.IsTrue(cli.IsEnabled);
            Complete(cli);
            await cli.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual("unknown", factory.Records.Single().Properties["database_type"]);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow(" \t ")]
        [DataRow(OTHER_ROOT)]
        public async Task AmbiguousProviderObservationsClearAndCannotRestoreTheCommandProvider(string? other)
        {
            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateSession(factory);
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MsSql);
            cli.ObserveDatabaseType(other, CliTelemetryDatabaseType.MySql);
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, StoredProvider(cli));
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.PostgreSql);
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, StoredProvider(cli));
            Complete(cli);
            await cli.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual("unknown", factory.Records.Single().Properties["database_type"]);
        }

        [TestMethod]
        public async Task NullFirstRootAndCaseDifferentRootsFollowTheExistingExactTargetPolicy()
        {
            foreach (string? firstRoot in new[] { null, ROOT.ToLowerInvariant() })
            {
                RecordingFactory factory = new();
                using CliTelemetrySession cli = CreateSession(factory);
                cli.ObserveDatabaseType(firstRoot, CliTelemetryDatabaseType.MySql);
                cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MySql);
                Complete(cli);
                await cli.StopAsync().WaitAsync(_timeout);
                Assert.AreEqual("unknown", factory.Records.Single().Properties["database_type"]);
            }
        }

        [DataTestMethod]
        [DataRow("lookup")]
        [DataRow("created")]
        [DataRow("launch")]
        [DataRow("reserved")]
        [DataRow("capped_launch")]
        public async Task ASecondTargetClearsProviderEvenBeforeAnySecondConfigIsParsed(string operation)
        {
            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateSession(factory);
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MsSql);
            ProductTelemetryLaunchContext? reservedLaunch = null;
            switch (operation)
            {
                case "lookup":
                    cli.ObserveConfiguration(OTHER_ROOT);
                    break;
                case "created":
                    cli.ConfigurationCreated(OTHER_ROOT);
                    break;
                case "launch":
                    Assert.IsNotNull(cli.BeginEngineLaunch(OTHER_ROOT, CliTelemetryLaunchSource.StartWeb));
                    break;
                case "reserved":
                    using (CliTelemetryLaunchReservation reservation = Reserve(cli))
                    {
                        reservedLaunch = reservation.Begin(OTHER_ROOT);
                        Assert.IsNotNull(reservedLaunch);
                    }

                    break;
                default:
                    for (int index = 0; index < 16; index++)
                    {
                        Assert.IsNotNull(cli.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb));
                    }

                    Assert.IsNull(cli.BeginEngineLaunch(OTHER_ROOT, CliTelemetryLaunchSource.StartWeb));
                    break;
            }

            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, StoredProvider(cli));
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.PostgreSql);
            Complete(cli);
            await cli.StopAsync().WaitAsync(_timeout);
            if (reservedLaunch is not null)
            {
                await factory.WaitAsync(LAUNCH, reservedLaunch.EngineSessionId);
                await factory.WaitForDisposalAsync();
            }

            Assert.AreEqual("unknown", factory.Records.Single(record => record.Name == COMMAND).Properties["database_type"]);
        }

        [DataTestMethod]
        [DataRow(false, true, false)]
        [DataRow(true, false, false)]
        [DataRow(true, true, true)]
        public async Task DisabledCreationGatesPrecedeProviderStateAndDependencies(bool synthetic, bool hasFactory, bool optedOut)
        {
            RecordingFactory factory = new();
            CountingClock clock = new(throwOnUse: true);
            int environments = 0;
            int dependencies = 0;
            Func<IProductTelemetryExporter<IProductTelemetryEvent>>? exporterFactory = hasFactory ? factory.Acquire : null;
            using CliTelemetrySession cli = CliTelemetrySession.Create(exporterFactory, synthetic, clock,
                readEnvironmentVariable: _ => { environments++; return optedOut ? "1" : null; },
                showNotice: () => dependencies++,
                resolveInstallation: () => { dependencies++; return new(_installationId, "newly_saved"); },
                createIdentity: _ => { dependencies++; return new(_apiId, "ephemeral"); },
                lookupIdentity: _ => { dependencies++; return new(_apiId, "reused"); });
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MsSql);
            cli.ObserveDatabaseType(null, (CliTelemetryDatabaseType)int.MaxValue);
            Assert.IsNull(cli.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb, CliTelemetryDatabaseType.MsSql));
            Complete(cli);
            await cli.StopAsync().WaitAsync(_timeout);
            Assert.IsFalse(cli.IsEnabled);
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, StoredProvider(cli));
            Assert.AreEqual(optedOut ? 1 : 0, environments);
            Assert.AreEqual(0, dependencies);
            Assert.AreEqual(0, clock.Calls);
            Assert.AreEqual(0, factory.Senders.Count);
            Assert.AreEqual(0, factory.Records.Count);
        }

        [TestMethod]
        public async Task DisableForgetsTheProviderAndIgnoresAllLaterObservations()
        {
            RecordingFactory factory = new();
            int identities = 0;
            using CliTelemetrySession cli = CreateSession(factory,
                createIdentity: _ => { identities++; return new(_apiId, "ephemeral"); },
                lookupIdentity: _ => { identities++; return new(_apiId, "reused"); });
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MsSql);
            using CliTelemetryLaunchReservation reservation = Reserve(cli);
            cli.Disable();
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, StoredProvider(cli));
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MySql);
            cli.ObserveDatabaseType(OTHER_ROOT, CliTelemetryDatabaseType.PostgreSql);
            Assert.IsNull(reservation.Begin(ROOT, CliTelemetryDatabaseType.DwSql));
            Complete(cli);
            await cli.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, StoredProvider(cli));
            Assert.AreEqual(0, identities);
            Assert.AreEqual(0, factory.Senders.Count);
            Assert.AreEqual(0, factory.Records.Count);
        }

        [DataTestMethod]
        [DataRow((int)CliTelemetryDatabaseType.Unknown, "unknown")]
        [DataRow((int)CliTelemetryDatabaseType.MsSql, "mssql")]
        [DataRow((int)CliTelemetryDatabaseType.DwSql, "dwsql")]
        [DataRow((int)CliTelemetryDatabaseType.PostgreSql, "postgresql")]
        [DataRow((int)CliTelemetryDatabaseType.MySql, "mysql")]
        [DataRow((int)CliTelemetryDatabaseType.CosmosDbNoSql, "cosmosdb_nosql")]
        [DataRow((int)CliTelemetryDatabaseType.CosmosDbPostgreSql, "cosmosdb_postgresql")]
        [DataRow((int)CliTelemetryDatabaseType.Multiple, "multiple")]
        [DataRow(-1, "unknown")]
        [DataRow(int.MinValue, "unknown")]
        [DataRow(int.MaxValue, "unknown")]
        public async Task CommandAndBothLaunchPathsNormalizeAllProviderValues(int value, string expected)
        {
            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateSession(factory);
            CliTelemetryDatabaseType databaseType = (CliTelemetryDatabaseType)value;
            cli.ObserveDatabaseType(ROOT, databaseType);
            Assert.AreEqual(expected == "unknown" ? CliTelemetryDatabaseType.Unknown : databaseType, StoredProvider(cli));
            Assert.IsNotNull(cli.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb, databaseType));
            using CliTelemetryLaunchReservation reservation = Reserve(cli);
            ProductTelemetryLaunchContext? reserved = reservation.Begin(ROOT, databaseType);
            Assert.IsNotNull(reserved);
            Complete(cli);
            await cli.StopAsync().WaitAsync(_timeout);
            await factory.WaitAsync(LAUNCH, reserved.EngineSessionId);
            await factory.WaitForDisposalAsync();
            Assert.AreEqual(3, factory.Records.Count);
            Assert.IsTrue(factory.Records.All(record => record.Properties["database_type"] == expected));
            AssertPrivateValuesAbsent(factory.Records);
        }

        [TestMethod]
        public async Task MissingTypedEvidenceIsExplicitUnknownEvenWhenTheOptionWasPresent()
        {
            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateSession(factory, installation: new(_installationId, "newly_saved"));
            Assert.IsNotNull(cli.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb));
            cli.Complete("init", "none", ImmutableDictionary<string, string>.Empty.Add("option_database_type", "true"),
                CliTelemetryOutcome.Success);
            await cli.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual(3, factory.Records.Count);
            Assert.IsTrue(factory.Records.All(record => record.Properties["database_type"] == "unknown"));
            Assert.AreEqual("true", factory.Records.Single(record => record.Name == COMMAND).Properties["option_database_type"]);
        }

        [TestMethod]
        public async Task DefaultLaunchArgumentsNeverFallBackToAKnownCommandProvider()
        {
            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateSession(factory);
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MsSql);
            Assert.IsNotNull(cli.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb));
            using CliTelemetryLaunchReservation reservation = Reserve(cli);
            Complete(cli);
            await cli.StopAsync().WaitAsync(_timeout);
            cli.Dispose();
            ProductTelemetryLaunchContext? deferred = reservation.Begin(ROOT);
            Assert.IsNotNull(deferred);
            await factory.WaitAsync(LAUNCH, deferred.EngineSessionId);
            await factory.WaitForDisposalAsync();
            Assert.AreEqual("mssql", factory.Records.Single(record => record.Name == COMMAND).Properties["database_type"]);
            Assert.AreEqual(2, factory.Records.Count(record => record.Name == LAUNCH));
            Assert.IsTrue(factory.Records.Where(record => record.Name == LAUNCH).All(record => record.Properties["database_type"] == "unknown"));
        }

        [TestMethod]
        public async Task LaunchOnlyEvidenceDoesNotInventACommandProviderObservation()
        {
            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateSession(factory);
            Assert.IsNotNull(cli.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb, CliTelemetryDatabaseType.PostgreSql));
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, StoredProvider(cli));
            Complete(cli);
            await cli.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual("postgresql", factory.Records.Single(record => record.Name == LAUNCH).Properties["database_type"]);
            Assert.AreEqual("unknown", factory.Records.Single(record => record.Name == COMMAND).Properties["database_type"]);
        }

        [DataTestMethod]
        [DataRow("lookup", false)]
        [DataRow("created", false)]
        [DataRow("launch", false)]
        [DataRow("launch", true)]
        public async Task InFlightIdentityCompletionCannotRewriteACompletedCommand(string operation, bool differentRoot)
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            EngineTelemetryIdentity Resolve(string? path)
            {
                Assert.AreEqual(differentRoot ? OTHER_ROOT : ROOT, path);
                entered.TrySetResult();
                release.Task.WaitAsync(_timeout).GetAwaiter().GetResult();
                return new(_apiId, "reused");
            }

            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateSession(factory, createIdentity: Resolve, lookupIdentity: Resolve);
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MsSql);
            Task<ProductTelemetryLaunchContext?> pending = Task.Run(() =>
            {
                if (operation == "lookup")
                {
                    cli.ObserveConfiguration(ROOT);
                }
                else if (operation == "created")
                {
                    cli.ConfigurationCreated(ROOT);
                }
                else
                {
                    return cli.BeginEngineLaunch(differentRoot ? OTHER_ROOT : ROOT,
                        CliTelemetryLaunchSource.StartWeb, CliTelemetryDatabaseType.PostgreSql);
                }

                return null;
            });
            CliTelemetryEvent command;
            try
            {
                await entered.Task.WaitAsync(_timeout);
                Complete(cli);
                command = await factory.WaitAsync(COMMAND);
                cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MySql);
                cli.ObserveDatabaseType(OTHER_ROOT, CliTelemetryDatabaseType.CosmosDbNoSql);
                await cli.StopAsync().WaitAsync(_timeout);
                Assert.IsFalse(pending.IsCompleted, "Completion and normal shutdown do not join identity I/O.");
                Assert.AreEqual(CliTelemetryDatabaseType.Unknown, StoredProvider(cli), "Normal stop forgets the mutable provider.");
            }
            finally
            {
                release.TrySetResult();
                Assert.IsNull(await pending.WaitAsync(_timeout));
            }

            Assert.AreSame(command, factory.Records.Single());
            Assert.AreEqual(differentRoot ? "unknown" : "mssql", command.Properties["database_type"]);
        }

        [TestMethod]
        public async Task OrdinaryLaunchesCaptureTheirOwnProvidersBeforeTheWorkerRuns()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            RecordingFactory factory = new(async (record, token) =>
            {
                if (record.Name == LAUNCH)
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(token).ConfigureAwait(false);
                }

                return true;
            });
            using CliTelemetrySession cli = CreateSession(factory);
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MsSql);
            ProductTelemetryLaunchContext? first = cli.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb, CliTelemetryDatabaseType.PostgreSql);
            Assert.IsNotNull(first);
            ProductTelemetryLaunchContext? second;
            try
            {
                await entered.Task.WaitAsync(_timeout);
                cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MySql);
                second = cli.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartStdio, CliTelemetryDatabaseType.DwSql);
                Assert.IsNotNull(second);
                cli.ObserveConfiguration(OTHER_ROOT);
                Complete(cli);
            }
            finally
            {
                release.TrySetResult();
            }

            await cli.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual("postgresql", (await factory.WaitAsync(LAUNCH, first.EngineSessionId)).Properties["database_type"]);
            Assert.AreEqual("dwsql", (await factory.WaitAsync(LAUNCH, second.EngineSessionId)).Properties["database_type"]);
            Assert.AreEqual("unknown", factory.Records.Single(record => record.Name == COMMAND).Properties["database_type"]);
            AssertPrivateValuesAbsent(factory.Records);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task DeferredLaunchAfterCompleteStopDisposeUsesItsOwnPreflightProvider(bool ambiguousCommand)
        {
            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateSession(factory);
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MsSql);
            using CliTelemetryLaunchReservation first = Reserve(cli);
            using CliTelemetryLaunchReservation second = Reserve(cli);
            // An unrelated earlier helper/config write must not become either launch's category.
            cli.ObserveDatabaseType(ambiguousCommand ? OTHER_ROOT : ROOT, CliTelemetryDatabaseType.MySql);
            Complete(cli);
            await cli.StopAsync().WaitAsync(_timeout);
            cli.Dispose();
            CliTelemetryEvent command = factory.Records.Single();
            Assert.AreEqual(CliTelemetryDatabaseType.Unknown, StoredProvider(cli));
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.PostgreSql);
            ProductTelemetryLaunchContext? firstLaunch = first.Begin(ROOT, CliTelemetryDatabaseType.CosmosDbNoSql);
            ProductTelemetryLaunchContext? secondLaunch = second.Begin(OTHER_ROOT, CliTelemetryDatabaseType.CosmosDbPostgreSql);
            Assert.IsNotNull(firstLaunch);
            Assert.IsNotNull(secondLaunch);
            Assert.AreEqual("cosmosdb_nosql", (await factory.WaitAsync(LAUNCH, firstLaunch.EngineSessionId)).Properties["database_type"]);
            Assert.AreEqual("cosmosdb_postgresql", (await factory.WaitAsync(LAUNCH, secondLaunch.EngineSessionId)).Properties["database_type"]);
            await factory.WaitForDisposalAsync();
            Assert.AreSame(command, factory.Records.Single(record => record.Name == COMMAND));
            Assert.AreEqual(ambiguousCommand ? "unknown" : "mysql", command.Properties["database_type"]);
            Assert.AreEqual(3, factory.Senders.Count, "Each late launch owns a new worker lease after the ordinary sender was disposed.");
            Assert.AreEqual(5, typeof(ProductTelemetryLaunchContext).GetProperties().Length, "No provider or path was added to the engine bridge.");
            AssertPrivateValuesAbsent(factory.Records);
        }

        [TestMethod]
        public async Task InFlightReservedLaunchKeepsItsArgumentAcrossNormalShutdown()
        {
            TaskCompletionSource entered = Signal();
            TaskCompletionSource release = Signal();
            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateSession(factory, createIdentity: _ =>
            {
                entered.TrySetResult();
                release.Task.WaitAsync(_timeout).GetAwaiter().GetResult();
                return new(_apiId, "ephemeral");
            });
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.MsSql);
            using CliTelemetryLaunchReservation reservation = Reserve(cli);
            Task<ProductTelemetryLaunchContext?> pending = Task.Run(() => reservation.Begin(OTHER_ROOT, CliTelemetryDatabaseType.DwSql));
            try
            {
                await entered.Task.WaitAsync(_timeout);
                Assert.AreEqual(CliTelemetryDatabaseType.Unknown, StoredProvider(cli));
                Complete(cli);
                await cli.StopAsync().WaitAsync(_timeout);
                cli.Dispose();
                Assert.IsFalse(pending.IsCompleted);
            }
            finally
            {
                release.TrySetResult();
            }

            ProductTelemetryLaunchContext? launch = await pending.WaitAsync(_timeout);
            Assert.IsNotNull(launch);
            Assert.AreEqual("dwsql", (await factory.WaitAsync(LAUNCH, launch.EngineSessionId)).Properties["database_type"]);
            await factory.WaitForDisposalAsync();
            Assert.AreEqual("unknown", factory.Records.Single(record => record.Name == COMMAND).Properties["database_type"]);
        }

        [TestMethod]
        public async Task LaunchEvidenceIsIndependentEvenWhenTheLocalRootIsUnknown()
        {
            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateSession(factory);
            cli.ObserveDatabaseType(null, CliTelemetryDatabaseType.MySql);
            Assert.IsNotNull(cli.BeginEngineLaunch(null, CliTelemetryLaunchSource.StartWeb, CliTelemetryDatabaseType.MySql));
            Complete(cli);
            await cli.StopAsync().WaitAsync(_timeout);
            Assert.AreEqual("mysql", factory.Records.Single(record => record.Name == LAUNCH).Properties["database_type"]);
            Assert.AreEqual("unknown", factory.Records.Single(record => record.Name == COMMAND).Properties["database_type"]);
        }

        [TestMethod]
        public async Task MaximumApprovedOptionCountStillFitsThe128PropertyEnvelope()
        {
            // Core's approved keys only, independent of CLI option classes or handler wiring.
            string[] keys =
            [
                "config", "database_type", "connection_string", "cosmosdb_nosql_database", "cosmosdb_nosql_container",
                "graphql_schema", "set_session_context", "host_mode", "cors_origin", "auth_provider", "auth_audience",
                "auth_issuer", "rest_path", "runtime_base_route", "rest_disabled", "graphql_path", "graphql_disabled",
                "mcp_path", "mcp_disabled", "rest_enabled", "graphql_enabled", "mcp_enabled", "rest_request_body_strict",
                "graphql_multiple_mutations_create_enabled", "mcp_aggregate_records_query_timeout", "source", "permissions",
                "source_type", "source_params", "source_key_fields", "rest", "rest_methods", "graphql", "graphql_operation",
                "fields_include", "fields_exclude", "policy_request", "policy_database", "cache_enabled", "cache_ttl_seconds",
                "cache_level", "health_enabled", "description", "parameters_name", "parameters_description", "parameters_required",
                "parameters_default", "fields_name", "fields_alias", "fields_description", "fields_primary_key", "mcp_dml_tools",
                "mcp_custom_tool", "relationship", "cardinality", "target_entity", "linking_object", "linking_source_fields",
                "linking_target_fields", "relationship_fields", "map", "verbose", "log_level", "no_https_redirect", "mcp_stdio",
                "output", "graphql_schema_file", "generate", "sampling_mode", "sampling_count", "sampling_partition_key_path",
                "sampling_days", "sampling_group_count", "app_insights_conn_string", "app_insights_enabled", "otel_endpoint",
                "otel_enabled", "otel_headers", "otel_protocol", "otel_service_name", "data_source_database_type",
                "data_source_connection_string", "data_source_options_database", "data_source_options_container",
                "data_source_options_schema", "data_source_options_set_session_context", "data_source_health_name",
                "data_source_user_delegated_auth_enabled", "data_source_user_delegated_auth_database_audience",
                "data_source_user_delegated_auth_provider", "data_source_health_enabled", "data_source_health_threshold_ms",
                "data_source_files", "runtime_graphql_depth_limit", "runtime_graphql_enabled", "runtime_graphql_path"
            ];
            ImmutableDictionary<string, string> options = keys.ToImmutableDictionary(key => "option_" + key, _ => "true");
            Assert.AreEqual(96, options.Count);
            RecordingFactory factory = new();
            using CliTelemetrySession cli = CreateSession(factory, installation: new(_installationId, "newly_saved"));
            cli.ObserveDatabaseType(ROOT, CliTelemetryDatabaseType.Multiple);
            cli.ConfigurationCreated(ROOT);
            Assert.IsNotNull(cli.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb, CliTelemetryDatabaseType.Multiple));
            cli.Complete("configure", "none", options, CliTelemetryOutcome.Success);
            await cli.StopAsync().WaitAsync(_timeout);
            CliTelemetryEvent command = factory.Records.Single(record => record.Name == COMMAND);
            Assert.AreEqual(96, command.Properties.Keys.Count(key => key.StartsWith("option_", StringComparison.Ordinal)));
            Assert.AreEqual("multiple", command.Properties["database_type"]);
            Assert.IsTrue(command.Properties.ContainsKey("dab_installation_id"));
            Assert.IsTrue(command.Properties.ContainsKey("dab_api_id"));
            Assert.IsTrue(factory.Records.All(record => record.Properties.Count <= 128));
            AssertPrivateValuesAbsent(factory.Records);
        }

        private static DataSource CreateSource(DatabaseType provider)
            => new(provider, "@env('" + PRIVATE_VALUE + "')", new() { [PRIVATE_VALUE] = new UninspectableValue() });

        private static RuntimeConfig CreateConfig(DatabaseType? provider)
            => new(Schema: PRIVATE_VALUE, DataSource: provider is DatabaseType value ? CreateSource(value) : null,
                Entities: new(new Dictionary<string, Entity>()));

        private static RuntimeConfig CreateMappedConfig(Dictionary<string, DataSource> sources, DataSource? root = null, string defaultName = ROOT)
            => new(Schema: PRIVATE_VALUE, DataSource: root!, Runtime: null!, Entities: new(new Dictionary<string, Entity>()),
                DefaultDataSourceName: defaultName, DataSourceNameToDataSource: sources, EntityNameToDataSourceName: new());

        private static RuntimeConfig Merge(DataSource? root, params (string FileName, RuntimeConfig Config)[] children)
        {
            Dictionary<string, DataSource> sources = new();
            if (root is not null)
            {
                sources.Add(ROOT, root);
            }

            foreach ((string _, RuntimeConfig child) in children)
            {
                foreach ((string name, DataSource source) in child.GetDataSourceNamesToDataSourcesIterator())
                {
                    sources.Add(name, source);
                }
            }

            RuntimeConfig config = CreateMappedConfig(sources, root) with
            {
                // Never pass these declarations to the file-loading constructor.
                DataSourceFiles = new(children.Select(child => child.FileName).ToArray())
            };
            config.ChildConfigs.AddRange(children);
            return config;
        }

        private static CliTelemetrySession CreateSession(RecordingFactory factory, CliTelemetryInstallation? installation = null,
            Func<string?, EngineTelemetryIdentity>? createIdentity = null, Func<string?, EngineTelemetryIdentity?>? lookupIdentity = null)
            => CliTelemetrySession.Create(factory.Acquire, true, readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveInstallation: () => installation ?? new(_installationId, "reused"),
                createIdentity: createIdentity ?? (_ => new(_apiId, "ephemeral")), lookupIdentity: lookupIdentity ?? (_ => null));

        private static CliTelemetryLaunchReservation Reserve(CliTelemetrySession cli)
        {
            CliTelemetryLaunchReservation? reservation = cli.ReserveEngineLaunch(CliTelemetryLaunchSource.ExportGraphQL);
            Assert.IsNotNull(reservation);
            return reservation;
        }

        private static void Complete(CliTelemetrySession cli)
            => cli.Complete("configure", "none", ImmutableDictionary<string, string>.Empty, CliTelemetryOutcome.Success);

        // Inspect immediate invalidation/forgetting, not an alternate production path. Every
        // caller observes this after the synchronous state transition, outside identity I/O.
        private static CliTelemetryDatabaseType StoredProvider(CliTelemetrySession cli)
            => (CliTelemetryDatabaseType)typeof(CliTelemetrySession).GetField("_databaseType", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(cli)!;

        private static void AssertPrivateValuesAbsent(IEnumerable<CliTelemetryEvent> records)
            => Assert.IsFalse(JsonSerializer.Serialize(records).Contains(PRIVATE_VALUE, StringComparison.Ordinal));

        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class UninspectableValue(string message = "Provider projection must not serialize or inspect options.")
        {
            public string Value => throw new InvalidOperationException(message);
            public override string ToString() => throw new InvalidOperationException("Provider projection must not format config values.");
        }

        private sealed class FaultingReferences(string failure) : IEnumerable<string>
        {
            public IEnumerator<string> GetEnumerator()
                => failure == "get_enumerator" ? throw new InvalidOperationException(PRIVATE_VALUE) : new Enumerator(failure);

            IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

            private sealed class Enumerator(string failure) : IEnumerator<string>
            {
                private bool _moved;
                public string Current => failure == "current" ? throw new InvalidOperationException(PRIVATE_VALUE) : CHILD_FILE;
                object IEnumerator.Current => Current;

                public bool MoveNext()
                {
                    if (failure == "move_next")
                    {
                        throw new InvalidOperationException(PRIVATE_VALUE);
                    }

                    bool result = !_moved;
                    _moved = true;
                    return result;
                }

                public void Reset() => throw new NotSupportedException();

                public void Dispose()
                {
                    if (failure == "dispose")
                    {
                        throw new InvalidOperationException(PRIVATE_VALUE);
                    }
                }
            }
        }

        private sealed class RecordingFactory(Func<CliTelemetryEvent, CancellationToken, ValueTask<bool>>? export = null)
        {
            private readonly ConcurrentDictionary<(string Name, Guid Engine), TaskCompletionSource<CliTelemetryEvent>> _signals = new();
            internal ConcurrentQueue<CliTelemetryEvent> Records { get; } = new();
            internal ConcurrentQueue<RecordingExporter> Senders { get; } = new();

            internal IProductTelemetryExporter<IProductTelemetryEvent> Acquire()
            {
                RecordingExporter sender = new(this);
                Senders.Enqueue(sender);
                return sender;
            }

            internal async ValueTask<bool> ExportAsync(IProductTelemetryEvent record, CancellationToken token)
            {
                token.ThrowIfCancellationRequested();
                CliTelemetryEvent cli = (CliTelemetryEvent)record;
                bool accepted = export is null || await export(cli, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (accepted)
                {
                    Records.Enqueue(cli);
                    EventSignal(cli.Name, cli.Name == LAUNCH ? Guid.Parse(cli.Properties["dab_launched_engine_session_id"]) : Guid.Empty)
                        .TrySetResult(cli);
                }

                return accepted;
            }

            internal Task<CliTelemetryEvent> WaitAsync(string name, Guid engine = default)
                => EventSignal(name, engine).Task.WaitAsync(_timeout);

            // Call after the expected events have arrived, so their workers have acquired leases.
            internal Task WaitForDisposalAsync() => Task.WhenAll(Senders.Select(sender => sender.Disposed.Task)).WaitAsync(_timeout);

            private TaskCompletionSource<CliTelemetryEvent> EventSignal(string name, Guid engine)
                => _signals.GetOrAdd((name, engine), _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        private sealed class RecordingExporter(RecordingFactory owner) : IProductTelemetryExporter<IProductTelemetryEvent>
        {
            internal TaskCompletionSource Disposed { get; } = Signal();

            public ValueTask<bool> ExportAsync(IProductTelemetryEvent record, CancellationToken cancellationToken)
            {
                ObjectDisposedException.ThrowIf(Disposed.Task.IsCompleted, this);
                return owner.ExportAsync(record, cancellationToken);
            }

            public void Dispose() => Disposed.TrySetResult();
        }

        private sealed class CountingClock(bool throwOnUse = false) : TimeProvider
        {
            private int _calls;
            internal int Calls => Volatile.Read(ref _calls);
            public override long TimestampFrequency
            {
                get
                {
                    Count();
                    return TimeSpan.TicksPerSecond;
                }
            }

            public override long GetTimestamp()
            {
                Count();
                return 0;
            }

            public override DateTimeOffset GetUtcNow()
            {
                Count();
                return new(2026, 9, 29, 12, 0, 0, TimeSpan.Zero);
            }

            public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
            {
                Count();
                return base.CreateTimer(callback, state, dueTime, period);
            }

            private void Count()
            {
                Interlocked.Increment(ref _calls);
                if (throwOnUse)
                {
                    throw new InvalidOperationException(PRIVATE_VALUE);
                }
            }
        }
    }
}
