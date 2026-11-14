// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Net;
using System.Text.RegularExpressions;
using Azure.DataApiBuilder.Config.Converters;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Cli.Constants;
using Cli.Telemetry;

namespace Cli.Tests.Telemetry
{
    /// <summary>
    /// Tests the ordinary-load adapter against the former inline sequence with counted MockFS
    /// operations, and the typed completion rules directly. No host, SDK, database or profile I/O.
    /// </summary>
    [TestClass]
    [TestCategory("CliTelemetry")]
    [DoNotParallelize]
    public class CliTelemetryObservationTests
    {
        private const string PRIVATE_VALUE = "PRIVATE_CLI_OBSERVATION_8d77";
        private const string VARIABLE = "@env('DAB_CLI_OBSERVATION_SYNTHETIC')";

        [DataTestMethod]
        [DataRow("mssql")]
        [DataRow("dwsql")]
        [DataRow("postgresql")]
        [DataRow("mysql")]
        [DataRow("cosmosdb_nosql")]
        [DataRow("cosmosdb_postgresql")]
        public async Task SuccessfulLoadPreservesIoModelProviderAndBoundedContext(string provider)
        {
            string json = Configuration(provider);
            LoadResult baseline = await LoadAsync(json, useHelper: false);
            LoadResult observed = await LoadAsync(json, useHelper: true);

            AssertEquivalent(baseline, observed);
            Assert.IsTrue(observed.Loaded);
            Assert.IsNotNull(observed.Config);
            Assert.AreEqual(VARIABLE, observed.Config.DataSource!.ConnectionString,
                "The ordinary default must not replace variables or inject an Application Name.");
            Assert.AreEqual(1, observed.Reads);
            Assert.AreEqual(3, observed.PathAccesses, "Only the existing root, loader diagnostic and model observations resolve paths.");
            Assert.AreEqual(provider, observed.Command.Properties["database_type"]);
            Assert.AreEqual("success", observed.Command.Properties["outcome"]);
            using JsonDocument context = JsonDocument.Parse(observed.Command.Properties["configuration_context"]);
            Assert.AreEqual("loaded", context.RootElement.GetProperty("observation").GetString());
            Assert.AreEqual(provider, context.RootElement.GetProperty("data_sources.types").GetString());
            Assert.AreEqual("disabled", context.RootElement.GetProperty("runtime.rest.configured").GetString());
            Assert.IsTrue(observed.Command.Properties["configuration_context"].Length <= 8192);
        }

        [DataTestMethod]
        [DataRow("missing", 0, false)]
        [DataRow("empty", 1, false)]
        [DataRow("invalid", 1, true)]
        public async Task FailedLoadPreservesIoDiagnosticsAndUnknownModel(string scenario, int reads, bool parseError)
        {
            string? json = scenario switch { "missing" => null, "empty" => string.Empty, _ => "{" };
            LoadResult baseline = await LoadAsync(json, useHelper: false);
            LoadResult observed = await LoadAsync(json, useHelper: true);

            AssertEquivalent(baseline, observed);
            Assert.IsFalse(observed.Loaded);
            Assert.IsNull(observed.Config);
            Assert.AreEqual(reads, observed.Reads);
            Assert.AreEqual(parseError, observed.ParseError);
            Assert.AreEqual("validation_failure", observed.Command.Properties["outcome"]);
            Assert.AreEqual("configuration", observed.Command.Properties["failure_category"]);
            Assert.AreEqual("unknown", observed.Command.Properties["database_type"]);
            using JsonDocument context = JsonDocument.Parse(observed.Command.Properties["configuration_context"]);
            Assert.AreEqual("unknown", context.RootElement.GetProperty("observation").GetString());
        }

        [DataTestMethod]
        [DataRow(false, false)]
        [DataRow(false, true)]
        [DataRow(true, false)]
        [DataRow(true, true)]
        public async Task ReplacementSettingsAreForwardedWithoutMutation(bool replaceEnvironment, bool replaceKeyVault)
        {
            DeserializationVariableReplacementSettings settings = new(azureKeyVaultOptions: null,
                doReplaceEnvVar: replaceEnvironment, doReplaceAkvVar: replaceKeyVault,
                envFailureMode: EnvironmentVariableReplacementFailureMode.Ignore)
            {
                SkipApplicationNameInjection = true
            };
            int replacements = 0;
            const string CONNECTION_STRING = "Host=unused.invalid;Database=synthetic";
            if (replaceEnvironment)
            {
                // A caller-owned strategy proves that the exact settings reach the real parser,
                // without consulting process environment or creating any Key Vault client.
                Regex pattern = settings.ReplacementStrategies.Keys.Single();
                settings.ReplacementStrategies[pattern] = _ => { replacements++; return CONNECTION_STRING; };
            }

            string json = Configuration("postgresql");
            LoadResult baseline = await LoadAsync(json, useHelper: false, settings);
            int baselineReplacements = replacements;
            LoadResult observed = await LoadAsync(json, useHelper: true, settings);

            AssertEquivalent(baseline, observed);
            Assert.IsTrue(observed.Loaded);
            Assert.AreEqual(replaceEnvironment ? CONNECTION_STRING : VARIABLE, observed.Config!.DataSource!.ConnectionString);
            Assert.AreEqual(baselineReplacements, replacements - baselineReplacements);
            Assert.AreEqual(replaceEnvironment, baselineReplacements > 0);
            Assert.AreEqual(replaceEnvironment, settings.DoReplaceEnvVar);
            Assert.AreEqual(replaceKeyVault, settings.DoReplaceAkvVar);
            Assert.AreEqual(EnvironmentVariableReplacementFailureMode.Ignore, settings.EnvFailureMode);
            Assert.IsTrue(settings.SkipApplicationNameInjection);
            Assert.AreEqual(1, observed.Reads);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void DefaultOffOrNullSessionDoesNotAccessTheObservationFileSystem(bool useNullSession)
        {
            using LoadFixture fixture = new(Configuration("mysql"));
            int senderCreations = 0;
            int environmentReads = 0;
            using CliTelemetrySession disabled = CliTelemetrySession.Create(
                exporterFactory: () => { senderCreations++; return new RecordingExporter(); },
                readEnvironmentVariable: _ => { environmentReads++; return null; });
            Assert.IsFalse(disabled.IsEnabled);
            CliTelemetrySession? session = useNullSession ? null : disabled;
            Mock<IFileSystem> observationFileSystem = new(MockBehavior.Strict);

            using (CliTelemetryHosting.BeginConfigurationCapture(session))
            {
                Assert.IsTrue(CliTelemetryHosting.TryLoadConfiguration(fixture.Loader, observationFileSystem.Object,
                    fixture.Root, session, out RuntimeConfig? config));
                Assert.IsNotNull(config);
                Assert.IsNull(config.TelemetryPresence);
            }

            // The loader still uses its own filesystem once. Even a caught mock exception
            // from an optional path lookup would be an invocation and fail this assertion.
            observationFileSystem.VerifyNoOtherCalls();
            Assert.AreEqual(1, fixture.Reads);
            Assert.AreEqual(1, fixture.PathAccesses, "Only the loader's existing diagnostic resolves a path.");
            Assert.AreEqual(0, senderCreations);
            Assert.AreEqual(0, environmentReads);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void LoadExceptionsKeepTheirOriginalBoundaryAndAreNotClassifiedAsFalse(bool canceled)
        {
            Exception expected = canceled ? new OperationCanceledException(PRIVATE_VALUE) : new UnauthorizedAccessException(PRIVATE_VALUE);
            using LoadFixture baseline = new(Configuration("mysql"), expected);
            using LoadFixture observed = new(Configuration("mysql"), expected);
            using CliTelemetrySession baselineSession = CreateSession(new(), baseline.Operations);
            using CliTelemetrySession observedSession = CreateSession(new(), observed.Operations);

            Exception first = CaptureLoadException(() => LoadPreviously(baseline, baselineSession, out _, null));
            Exception second = CaptureLoadException(() => CliTelemetryHosting.TryLoadConfiguration(
                observed.Loader, observed.FileSystem, observed.Root, observedSession, out _));

            Assert.AreSame(expected, first);
            Assert.AreSame(expected, second);
            Assert.IsFalse(observedSession.HasFailure);
            Assert.AreEqual(1, observed.Reads);
            CollectionAssert.AreEqual(baseline.Operations.ToArray(), observed.Operations.ToArray());
        }

        [DataTestMethod]
        [DataRow(-1)]
        [DataRow(1)]
        [DataRow(int.MinValue)]
        public void NonzeroExitWithoutTypedEvidenceIsUnknownNeverDefaultSuccess(int exitCode)
        {
            using CliTelemetrySession enabled = CreateSession(new());
            using CliTelemetrySession disabled = CliTelemetrySession.Create();
            CliTelemetryCommandResult expected = new(CliTelemetryOutcome.Unknown, CliTelemetryFailureCategory.Unknown);

            // A newly added handler's false/-1 is not enough to invent a cause. In particular,
            // falling back to default(record struct) would incorrectly produce Success/None.
            Assert.AreEqual(expected, CliTelemetryHosting.ClassifyReturnCode(exitCode));
            Assert.AreEqual(expected, CliTelemetryHosting.ClassifyReturnCode(exitCode, disabled));
            Assert.AreEqual(expected, CliTelemetryHosting.ClassifyReturnCode(exitCode, enabled));
            Assert.AreNotEqual(default(CliTelemetryCommandResult), expected);
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(-1)]
        [DataRow(1)]
        [DataRow(int.MinValue)]
        public void ReturnCodeTablePreservesEveryFirstFailureEnumAndCategory(int exitCode)
        {
            foreach (CliTelemetryOutcome outcome in Enum.GetValues<CliTelemetryOutcome>())
            {
                foreach (CliTelemetryFailureCategory category in Enum.GetValues<CliTelemetryFailureCategory>())
                {
                    using CliTelemetrySession session = CreateSession(new());
                    session.MarkFailure(outcome, category);
                    CliTelemetryCommandResult expected = exitCode == CliReturnCode.SUCCESS
                        ? new(CliTelemetryOutcome.Success, CliTelemetryFailureCategory.None)
                        : outcome == CliTelemetryOutcome.Success
                            ? new(CliTelemetryOutcome.Unknown, CliTelemetryFailureCategory.Unknown)
                            : new(outcome, category);

                    Assert.AreEqual(expected, CliTelemetryHosting.ClassifyReturnCode(exitCode, session),
                        $"Exit {exitCode}, first observation {outcome}/{category}.");
                }
            }
        }

        [DataTestMethod]
        [DataRow(0)]
        [DataRow(-1)]
        public void TerminalExportResultWinsOverAttemptsAndStartupObservations(int exitCode)
        {
            using CliTelemetrySession session = CreateSession(new());
            session.MarkFailure(CliTelemetryOutcome.ExecutionFailure, CliTelemetryFailureCategory.Storage);
            session.ObserveEngineStartupFailure();
            CliTelemetryCommandResult[] terminalResults =
            [
                new(CliTelemetryOutcome.Success, CliTelemetryFailureCategory.None),
                new(CliTelemetryOutcome.ExecutionFailure, CliTelemetryFailureCategory.Execution)
            ];

            foreach (CliTelemetryCommandResult terminal in terminalResults)
            {
                Assert.AreEqual(terminal, CliTelemetryHosting.ClassifyReturnCode(exitCode, session, terminal, isStartCommand: true));
            }

            Assert.AreEqual(CliTelemetryFailureCategory.Storage, session.FailureCategory,
                "Choosing the terminal result must not rewrite the original attempt evidence.");
        }

        [TestMethod]
        public void ObservedStartInitializationFailureOverridesLegacyZeroButNotOtherCommands()
        {
            using CliTelemetrySession session = CreateSession(new());
            session.ObserveEngineStartupFailure();

            Assert.AreEqual(new CliTelemetryCommandResult(CliTelemetryOutcome.ExecutionFailure, CliTelemetryFailureCategory.Initialization),
                CliTelemetryHosting.ClassifyReturnCode(CliReturnCode.SUCCESS, session, isStartCommand: true));
            Assert.AreEqual(new CliTelemetryCommandResult(CliTelemetryOutcome.Success, CliTelemetryFailureCategory.None),
                CliTelemetryHosting.ClassifyReturnCode(CliReturnCode.SUCCESS, session));
        }

        [TestMethod]
        public void ExceptionMappingUsesTypesAndPreservesEveryBoundaryCategory()
        {
            foreach (CliTelemetryFailureCategory category in Enum.GetValues<CliTelemetryFailureCategory>())
            {
                AssertMapping(new OperationCanceledException(PRIVATE_VALUE), CliTelemetryOutcome.Canceled, CliTelemetryFailureCategory.Canceled);
                AssertMapping(new TaskCanceledException(PRIVATE_VALUE), CliTelemetryOutcome.Canceled, CliTelemetryFailureCategory.Canceled);
                AssertMapping(new IOException(PRIVATE_VALUE), CliTelemetryOutcome.ExecutionFailure, CliTelemetryFailureCategory.Storage);
                AssertMapping(new UnauthorizedAccessException(PRIVATE_VALUE), CliTelemetryOutcome.ExecutionFailure, CliTelemetryFailureCategory.Storage);
                AssertMapping(new InvalidOperationException(PRIVATE_VALUE), CliTelemetryOutcome.ExecutionFailure, category);
                AssertMapping(new DataApiBuilderException(PRIVATE_VALUE, HttpStatusCode.BadRequest,
                    DataApiBuilderException.SubStatusCodes.ConfigValidationError),
                    category == CliTelemetryFailureCategory.Execution ? CliTelemetryOutcome.ValidationFailure : CliTelemetryOutcome.ExecutionFailure,
                    category == CliTelemetryFailureCategory.Execution ? CliTelemetryFailureCategory.Configuration : category);
                AssertMapping(new DataApiBuilderException(PRIVATE_VALUE, HttpStatusCode.ServiceUnavailable,
                    DataApiBuilderException.SubStatusCodes.ErrorInInitialization), CliTelemetryOutcome.ExecutionFailure, category);
                AssertMapping(new AggregateException(new IOException(PRIVATE_VALUE), new OperationCanceledException(PRIVATE_VALUE)),
                    CliTelemetryOutcome.ExecutionFailure, category);

                void AssertMapping(Exception exception, CliTelemetryOutcome outcome, CliTelemetryFailureCategory failureCategory)
                {
                    CliTelemetryCommandResult expected = new(outcome, failureCategory);
                    Assert.AreEqual(expected, CliTelemetryHosting.ClassifyException(exception, category));
                    Assert.AreEqual(expected, CliTelemetryHosting.ClassifyException(new AggregateException(new AggregateException(exception)), category));
                }
            }
        }

        [TestMethod]
        public void ExceptionMarkersPreserveTheFirstPreciseFailure()
        {
            using CliTelemetrySession session = CreateSession(new());
            session.MarkFailure(CliTelemetryOutcome.ParseFailure, CliTelemetryFailureCategory.Arguments);
            CliTelemetryHosting.MarkException(session, new IOException(PRIVATE_VALUE));

            Assert.AreEqual(new CliTelemetryCommandResult(CliTelemetryOutcome.ParseFailure, CliTelemetryFailureCategory.Arguments),
                CliTelemetryHosting.ClassifyReturnCode(CliReturnCode.GENERAL_ERROR, session));
        }

        private static Exception CaptureLoadException(Func<bool> load)
        {
            try
            {
                load();
            }
            catch (Exception exception)
            {
                return exception;
            }

            throw new AssertFailedException("The original loader exception must escape, not become a false return.");
        }

        private static string Configuration(string provider) => $$"""
            {
              "data-source": { "database-type": "{{provider}}", "connection-string": "{{VARIABLE}}" },
              "runtime": { "rest": { "enabled": false }, "graphql": { "enabled": true }, "mcp": { "enabled": false } },
              "entities": { "Books": { "source": "{{PRIVATE_VALUE}}", "permissions": [] } }
            }
            """;

        private static async Task<LoadResult> LoadAsync(string? json, bool useHelper,
            DeserializationVariableReplacementSettings? settings = null)
        {
            using LoadFixture fixture = new(json);
            RecordingExporter exporter = new();
            using CliTelemetrySession session = CreateSession(exporter, fixture.Operations);
            bool loaded;
            RuntimeConfig? config;
            using (CliTelemetryHosting.BeginConfigurationCapture(session))
            {
                loaded = useHelper
                    ? CliTelemetryHosting.TryLoadConfiguration(fixture.Loader, fixture.FileSystem, fixture.Root, session, out config, settings)
                    : LoadPreviously(fixture, session, out config, settings);
            }

            Assert.AreEqual(fixture.Root, fixture.Loader.ConfigFilePath);
            Assert.AreSame(config, fixture.Loader.RuntimeConfig);
            Assert.AreSame(config, fixture.Loader.LastValidRuntimeConfig);
            Assert.AreEqual(json, fixture.Files.File.Exists(fixture.Root) ? fixture.Files.File.ReadAllText(fixture.Root) : null);
            Assert.AreEqual(json is null ? 0 : 1, fixture.Files.AllFiles.Count(), "No extra configuration or identity files may appear.");
            Assert.IsFalse(TelemetryConfigurationPresence.IsCaptureEnabled());
            Assert.IsTrue(fixture.Operations[0].StartsWith("lookup:", StringComparison.Ordinal), "Observe the root before the load, even on failure.");
            Assert.AreEqual(1, fixture.Operations.Count(operation => operation.StartsWith("exists:", StringComparison.Ordinal)), "Exactly one normal load probe.");
            CliTelemetryCommandResult result = CliTelemetryHosting.ClassifyReturnCode(loaded ? CliReturnCode.SUCCESS : CliReturnCode.GENERAL_ERROR, session);
            session.Complete("configure", "none", ImmutableDictionary<string, string>.Empty, result.Outcome, result.FailureCategory);
            await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            CliTelemetryEvent command = exporter.Events.Single();
            Assert.IsFalse(JsonSerializer.Serialize(command).Contains(PRIVATE_VALUE, StringComparison.Ordinal));
            return new(loaded, config, fixture.Operations.ToArray(), fixture.Reads, fixture.PathAccesses,
                fixture.Logger.Entries.ToArray(), fixture.Loader.IsParseErrorEmitted, command);
        }

        private static bool LoadPreviously(LoadFixture fixture, CliTelemetrySession session, out RuntimeConfig? config,
            DeserializationVariableReplacementSettings? settings)
        {
            // Independent reference: the exact sequence removed from the ordinary command sites.
            CliTelemetryHosting.ObserveConfiguration(session, fixture.FileSystem, fixture.Root);
            if (!fixture.Loader.TryLoadConfig(fixture.Root, out config, replacementSettings: settings))
            {
                session.MarkFailure(CliTelemetryOutcome.ValidationFailure, CliTelemetryFailureCategory.Configuration);
                return false;
            }

            CliTelemetryHosting.ObserveConfigurationDetails(session, fixture.FileSystem, fixture.Root, config);
            return true;
        }

        private static void AssertEquivalent(LoadResult baseline, LoadResult observed)
        {
            Assert.AreEqual(baseline.Loaded, observed.Loaded);
            Assert.AreEqual(baseline.Reads, observed.Reads);
            Assert.AreEqual(baseline.PathAccesses, observed.PathAccesses);
            Assert.AreEqual(baseline.ParseError, observed.ParseError);
            CollectionAssert.AreEqual(baseline.Operations, observed.Operations);
            CollectionAssert.AreEqual(baseline.Diagnostics, observed.Diagnostics);
            foreach (string key in new[] { "database_type", "configuration_context", "outcome", "failure_category" })
            {
                Assert.AreEqual(baseline.Command.Properties[key], observed.Command.Properties[key], key);
            }
        }

        private static CliTelemetrySession CreateSession(RecordingExporter exporter, List<string>? operations = null)
        {
            CliTelemetrySession session = CliTelemetrySession.Create(() => exporter, enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null, showNotice: () => { }, resolveInstallation: () => new(null, "unavailable"),
                createIdentity: _ => throw new AssertFailedException("An ordinary load must never create an identity."),
                lookupIdentity: path => { operations?.Add("lookup:" + path); return null; });
            Assert.IsTrue(session.IsEnabled);
            return session;
        }

        private sealed record LoadResult(bool Loaded, RuntimeConfig? Config, string[] Operations, int Reads,
            int PathAccesses, string[] Diagnostics, bool ParseError, CliTelemetryEvent Command);

        private sealed class LoadFixture : IDisposable
        {
            public MockFileSystem Files { get; } = new();
            public string Root { get; }
            public IFileSystem FileSystem { get; }
            public FileSystemRuntimeConfigLoader Loader { get; }
            public RecordingLogger Logger { get; } = new();
            public List<string> Operations { get; } = new();
            public int Reads { get; private set; }
            public int PathAccesses { get; private set; }

            public LoadFixture(string? json, Exception? readFailure = null)
            {
                Root = Files.Path.GetFullPath(Files.Path.Combine("cli-observation-offline", PRIVATE_VALUE + ".json"));
                if (json is not null)
                {
                    Files.AddFile(Root, new MockFileData(json));
                }

                Mock<IFile> file = new(MockBehavior.Strict);
                file.Setup(item => item.Exists(It.IsAny<string>())).Returns((string path) =>
                {
                    Operations.Add("exists:" + path);
                    return Files.File.Exists(path);
                });
                file.Setup(item => item.ReadAllText(It.IsAny<string>())).Returns((string path) =>
                {
                    Reads++;
                    Operations.Add("read:" + path);
                    if (readFailure is not null)
                    {
                        throw readFailure;
                    }

                    return Files.File.ReadAllText(path);
                });
                Mock<IFileSystem> fileSystem = new(MockBehavior.Strict);
                fileSystem.SetupGet(item => item.File).Returns(file.Object);
                fileSystem.SetupGet(item => item.Path).Returns(() => { PathAccesses++; return Files.Path; });
                FileSystem = fileSystem.Object;
                Loader = new(FileSystem, baseConfigFilePath: Root, isCliLoader: true, logger: Logger);
            }

            public void Dispose() => Loader.Dispose();
        }

        private sealed class RecordingLogger : ILogger<FileSystemRuntimeConfigLoader>
        {
            public List<string> Entries { get; } = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => Entries.Add(formatter(state, exception));
        }

        private sealed class RecordingExporter : IProductTelemetryExporter<IProductTelemetryEvent>
        {
            public ConcurrentQueue<CliTelemetryEvent> Events { get; } = new();
            public ValueTask<bool> ExportAsync(IProductTelemetryEvent record, CancellationToken cancellationToken)
            {
                Events.Enqueue((CliTelemetryEvent)record);
                return ValueTask.FromResult(true);
            }

            public void Dispose() { }
        }
    }
}
