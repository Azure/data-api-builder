// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Collections.Immutable;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Cli.Constants;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cli.Tests.Telemetry
{
    /// <summary>
    /// Exercises Program.Execute's real parser and handlers without a host, database, SDK sender,
    /// real config files, or user-profile identity storage. No test calls Main.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    [TestCategory("CliTelemetry")]
    [TestCategory("EngineTelemetry")]
    public class CliTelemetryExecutionTests
    {
        private const string SENTINEL = "NEVER_COLLECT_cli_12dc_path_entity_connection_description";
        private const string ROOT = SENTINEL + ".json";
        private MockFileSystem _fileSystem = null!;
        private ILoggerFactory _originalLoggerFactory = null!;
        private TextWriter _originalOut = null!;
        private StringWriter _stdout = null!;

        [TestInitialize]
        public void Initialize()
        {
            _fileSystem = new MockFileSystem();
            string directory = _fileSystem.Path.GetFullPath("cli-telemetry-offline");
            _fileSystem.AddDirectory(directory);
            _fileSystem.Directory.SetCurrentDirectory(directory);
            string schemaPath = _fileSystem.Path.Combine(
                _fileSystem.Path.GetDirectoryName(typeof(FileSystemRuntimeConfigLoader).Assembly.Location)!,
                "dab.draft.schema.json");
            _fileSystem.AddFile(schemaPath, new MockFileData("{ \"$id\": \"https://example.invalid/dab-schema.json\" }"));

            _originalLoggerFactory = Utils.LoggerFactoryForCli;
            Utils.LoggerFactoryForCli = NullLoggerFactory.Instance;
            Utils.SetCliUtilsLogger(NullLogger<Utils>.Instance);
            ConfigGenerator.SetLoggerForCliConfigGenerator(NullLogger<ConfigGenerator>.Instance);
            _originalOut = Console.Out;
            _stdout = new StringWriter();
            Console.SetOut(_stdout);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Console.SetOut(_originalOut);
            _stdout.Dispose();
            Utils.LoggerFactoryForCli = _originalLoggerFactory;
            Utils.SetCliUtilsLogger(_originalLoggerFactory.CreateLogger<Utils>());
            ConfigGenerator.SetLoggerForCliConfigGenerator(_originalLoggerFactory.CreateLogger<ConfigGenerator>());
        }

        [TestMethod]
        public async Task InitCreatesIdentityOnlyAfterTheActualWriteAndCompletesOnce()
        {
            using RecordingScope scope = new(_fileSystem, newlySavedInstallation: true);
            scope.CreationElapsed = TimeSpan.FromMilliseconds(37);
            string relativePath = _fileSystem.Path.Combine(".", ROOT);

            int result = Execute(["init", "--database-type", "mssql", "--connection-string", SENTINEL,
                "--config", relativePath, "--rest.enabled", "false"], scope.Session);

            Assert.AreEqual(CliReturnCode.SUCCESS, result);
            CollectionAssert.AreEqual(new[] { _fileSystem.Path.GetFullPath(relativePath) }, scope.CreatedPaths.ToArray());
            CollectionAssert.AreEqual(new[] { true }, scope.FileExistedAtCreation.ToArray());
            Assert.AreEqual(0, scope.LookupPaths.Count);
            Assert.IsTrue(_fileSystem.File.ReadAllText(relativePath).Contains(SENTINEL, StringComparison.Ordinal));

            // Execute completes, but does not stop the caller's session. Redundant completion
            // and stopping must not manufacture another event or change the recorded duration.
            Assert.IsTrue(scope.Session.IsEnabled);
            scope.Clock.Advance(TimeSpan.FromHours(1));
            scope.Session.Complete("init", "none", ImmutableDictionary<string, string>.Empty,
                CliTelemetryOutcome.ExecutionFailure, CliTelemetryFailureCategory.Storage);
            CliTelemetryEvent[] events = await scope.DrainAsync();
            await scope.Session.StopAsync();

            CollectionAssert.AreEqual(new[] { "dab.cli.first_run", "dab.cli.command" }, events.Select(record => record.Name).ToArray());
            Assert.AreEqual(1L, events[0].Sequence);
            Assert.AreEqual(2L, events[1].Sequence);
            CliTelemetryEvent command = Command(events, "init", "success", "none");
            Assert.AreEqual("37", command.Properties["duration_ms"]);
            Assert.AreEqual(scope.ApiId.ToString("D"), command.Properties["dab_api_id"]);
            Assert.AreEqual("ephemeral", command.Properties["dab_api_id_stability"]);
            Assert.AreEqual("true", command.Properties["option_connection_string"]);
            Assert.AreEqual("true", command.Properties["option_config"]);
            Assert.AreEqual("true", command.Properties["option_rest_enabled"]);
            Assert.IsFalse(command.Properties.ContainsKey("option_host_mode"), "Defaults are not supplied options.");
            Assert.AreEqual(1, scope.Notices);
            Assert.AreEqual(1, scope.Installations);
            AssertPrivateValuesAbsent(events);
        }

        [TestMethod]
        public async Task InitExistingFileLooksUpButDoesNotCreateAnIdentity()
        {
            _fileSystem.AddFile(ROOT, new MockFileData(INITIAL_CONFIG));
            using RecordingScope scope = new(_fileSystem, existingIdentity: true);

            int result = Execute(["init", "--database-type", "mssql", "--config", ROOT], scope.Session);

            Assert.AreEqual(CliReturnCode.GENERAL_ERROR, result);
            Assert.AreEqual(INITIAL_CONFIG, _fileSystem.File.ReadAllText(ROOT));
            Assert.AreEqual(0, scope.CreatedPaths.Count);
            CollectionAssert.AreEqual(new[] { _fileSystem.Path.GetFullPath(ROOT) }, scope.LookupPaths.ToArray());
            CliTelemetryEvent command = Command(await scope.DrainAsync(), "init", "validation_failure", "configuration");
            Assert.AreEqual(scope.ApiId.ToString("D"), command.Properties["dab_api_id"]);
        }

        [TestMethod]
        public async Task InitRejectedConfigurationDoesNotCreateAnIdentity()
        {
            using RecordingScope scope = new(_fileSystem);

            int result = Execute(["init", "--database-type", "cosmosdb_nosql", "--config", ROOT], scope.Session);

            Assert.AreEqual(CliReturnCode.GENERAL_ERROR, result);
            Assert.IsFalse(_fileSystem.File.Exists(ROOT));
            Assert.AreEqual(0, scope.CreatedPaths.Count);
            Assert.AreEqual(0, scope.LookupPaths.Count);
            AssertNoApiIdentity(Command(await scope.DrainAsync(), "init", "validation_failure", "configuration"));
        }

        [TestMethod]
        public async Task InitWriteFailureDoesNotCreateAnIdentityOrReportSuccess()
        {
            using RecordingScope scope = new(_fileSystem);
            IFileSystem failingFileSystem = ThrowOnWrite(new IOException(SENTINEL));

            int result = Execute(["init", "--database-type", "mssql", "--config", ROOT], scope.Session, failingFileSystem);

            Assert.AreEqual(CliReturnCode.GENERAL_ERROR, result);
            Assert.IsFalse(_fileSystem.File.Exists(ROOT));
            Assert.AreEqual(0, scope.CreatedPaths.Count);
            Assert.AreEqual(0, scope.LookupPaths.Count);
            AssertNoApiIdentity(Command(await scope.DrainAsync(), "init", "execution_failure", "storage"));
        }

        [DataTestMethod]
        [DynamicData(nameof(ParserCases), DynamicDataSourceType.Method)]
        public async Task ActualParserControlsAndFailuresKeepExitCodesAndOutput(
            string[] args, int expectedCode, string name, string control, string outcome, string category)
        {
            // Compare the instrumented call to the unchanged public disabled entry point.
            using FileSystemRuntimeConfigLoader baselineLoader = CreateLoader(_fileSystem);
            int baseline = Program.Execute(args, NullLogger.Instance, _fileSystem, baselineLoader);
            string baselineOutput = _stdout.ToString();
            _stdout.GetStringBuilder().Clear();
            using RecordingScope scope = new(_fileSystem);

            int result = Execute(args, scope.Session);

            Assert.AreEqual(expectedCode, baseline);
            Assert.AreEqual(baseline, result);
            Assert.AreEqual(baselineOutput, _stdout.ToString(), "Inspection must not parse a second time or print additional help.");
            Assert.AreEqual(0, scope.CreatedPaths.Count);
            Assert.AreEqual(0, scope.LookupPaths.Count);
            CliTelemetryEvent[] events = await scope.DrainAsync();
            CliTelemetryEvent command = Command(events, name, outcome, category);
            Assert.AreEqual(control, command.Properties["control"]);
            Assert.AreEqual(1, events.Length);
            AssertNoApiIdentity(command);
            AssertPrivateValuesAbsent(events);
        }

        public static IEnumerable<object[]> ParserCases()
        {
            yield return new object[] { new[] { SENTINEL, "--config", SENTINEL }, -1, "unknown", "none", "parse_failure", "arguments" };
            yield return new object[] { new[] { "init", "--database-type", SENTINEL }, -1, "init", "none", "parse_failure", "arguments" };
            yield return new object[] { new[] { "init", "--database-type" }, -1, "init", "none", "parse_failure", "arguments" };
            yield return new object[] { new[] { "--help" }, 0, "unknown", "help", "success", "none" };
            yield return new object[] { new[] { "help", "start" }, 0, "start", "help", "success", "none" };
            yield return new object[] { new[] { "start", "--help", "--config", SENTINEL }, 0, "start", "help", "success", "none" };
            yield return new object[] { new[] { "--version" }, 0, "unknown", "version", "success", "none" };
            yield return new object[] { new[] { "start", "--version" }, 0, "start", "version", "success", "none" };
            yield return new object[] { new[] { "start", "--config", "--help" }, -1, "start", "none", "parse_failure", "arguments" };
        }

        [DataTestMethod]
        [DynamicData(nameof(MissingConfigCases), DynamicDataSourceType.Method)]
        public async Task ConfigUsingHandlersClassifyTheirActualLoadFailure(string[] args)
        {
            using RecordingScope scope = new(_fileSystem);

            int result = Execute(args, scope.Session);

            Assert.AreEqual(CliReturnCode.GENERAL_ERROR, result);
            Assert.AreEqual(0, scope.CreatedPaths.Count);
            CollectionAssert.AreEqual(new[] { _fileSystem.Path.GetFullPath(ROOT) }, scope.LookupPaths.ToArray());
            CliTelemetryEvent[] events = await scope.DrainAsync();
            Assert.AreEqual(1, events.Length, "A rejected start/export is not an engine launch.");
            AssertNoApiIdentity(Command(events, args[0], "validation_failure", "configuration"));
            AssertPrivateValuesAbsent(events);
        }

        public static IEnumerable<object[]> MissingConfigCases()
        {
            yield return new object[] { new[] { "add", SENTINEL, "--source", SENTINEL, "--permissions", "anonymous:read", "--config", ROOT } };
            yield return new object[] { new[] { "update", SENTINEL, "--config", ROOT } };
            yield return new object[] { new[] { "start", "--config", ROOT } };
            yield return new object[] { new[] { "start", "--mcp-stdio", "--config", ROOT } };
            yield return new object[] { new[] { "validate", "--config", ROOT } };
            yield return new object[] { new[] { "export", "--graphql", "--output", SENTINEL, "--config", ROOT } };
            yield return new object[] { new[] { "add-telemetry", "--config", ROOT } };
            yield return new object[] { new[] { "configure", "--config", ROOT } };
            yield return new object[] { new[] { "configure", "--show-effective-permissions", "--config", ROOT } };
            yield return new object[] { new[] { "auto-config", SENTINEL, "--config", ROOT } };
            yield return new object[] { new[] { "auto-config-simulate", "--config", ROOT } };
            yield return new object[] { new[] { "appname", "--config", ROOT } };
        }

        [TestMethod]
        public async Task MissingPositionalEntityIsValidationRatherThanAnUnknownFailure()
        {
            using RecordingScope scope = new(_fileSystem);

            Assert.AreEqual(CliReturnCode.GENERAL_ERROR, Execute(["update", "--config", ROOT], scope.Session));

            Assert.AreEqual(0, scope.LookupPaths.Count, "The handler rejects the entity before selecting a config.");
            AssertNoApiIdentity(Command(await scope.DrainAsync(), "update", "validation_failure", "arguments"));
        }

        [TestMethod]
        public async Task ConfigureRejectionIsValidationAndDoesNotCreateIdentity()
        {
            _fileSystem.AddFile(ROOT, new MockFileData(INITIAL_CONFIG));
            using RecordingScope scope = new(_fileSystem);

            int result = Execute(["configure", "--config", ROOT, "--runtime.graphql.depth-limit", "0"], scope.Session);

            Assert.AreEqual(CliReturnCode.GENERAL_ERROR, result);
            Assert.AreEqual(INITIAL_CONFIG, _fileSystem.File.ReadAllText(ROOT));
            Assert.AreEqual(0, scope.CreatedPaths.Count);
            AssertNoApiIdentity(Command(await scope.DrainAsync(), "configure", "validation_failure", "arguments"));
        }

        [TestMethod]
        public async Task ExplicitConfigurationValidationExceptionIsStillThrownAndClassified()
        {
            _fileSystem.AddFile(ROOT, new MockFileData(INITIAL_CONFIG));
            using RecordingScope scope = new(_fileSystem);

            DataApiBuilderException exception = Assert.ThrowsException<DataApiBuilderException>(() =>
                Execute(["configure", "--config", ROOT, "--runtime.pagination.max-page-size", "0"], scope.Session));

            Assert.AreEqual(DataApiBuilderException.SubStatusCodes.ConfigValidationError, exception.SubStatusCode);
            Assert.AreEqual(0, scope.CreatedPaths.Count);
            AssertNoApiIdentity(Command(await scope.DrainAsync(), "configure", "validation_failure", "configuration"));
        }

        [TestMethod]
        public async Task SuccessfulUpdateOnlyLooksUpItsSelectedRoot()
        {
            _fileSystem.AddFile(ROOT, new MockFileData(INITIAL_CONFIG));
            using RecordingScope scope = new(_fileSystem, existingIdentity: true);

            int result = Execute(["configure", "--config", ROOT, "--data-source.connection-string", SENTINEL], scope.Session);

            Assert.AreEqual(CliReturnCode.SUCCESS, result);
            Assert.AreEqual(0, scope.CreatedPaths.Count);
            CollectionAssert.AreEqual(new[] { _fileSystem.Path.GetFullPath(ROOT) }, scope.LookupPaths.ToArray());
            CliTelemetryEvent[] events = await scope.DrainAsync();
            CliTelemetryEvent command = Command(events, "configure", "success", "none");
            Assert.AreEqual(scope.ApiId.ToString("D"), command.Properties["dab_api_id"]);
            AssertPrivateValuesAbsent(events);
        }

        [TestMethod]
        public async Task UpdateWriteFailureKeepsLookupIdentityAndStorageClassification()
        {
            _fileSystem.AddFile(ROOT, new MockFileData(INITIAL_CONFIG));
            using RecordingScope scope = new(_fileSystem, existingIdentity: true);

            int result = Execute(["configure", "--config", ROOT, "--data-source.connection-string", SENTINEL],
                scope.Session, ThrowOnWrite(new UnauthorizedAccessException(SENTINEL)));

            Assert.AreEqual(CliReturnCode.GENERAL_ERROR, result);
            Assert.AreEqual(0, scope.CreatedPaths.Count);
            CliTelemetryEvent command = Command(await scope.DrainAsync(), "configure", "execution_failure", "storage");
            Assert.AreEqual(scope.ApiId.ToString("D"), command.Properties["dab_api_id"]);
        }

        [TestMethod]
        public async Task InvalidStartConnectionFailsBeforeAnyLaunch()
        {
            _fileSystem.AddFile(ROOT, new MockFileData(INVALID_INTIAL_CONFIG));
            using RecordingScope scope = new(_fileSystem);

            Assert.AreEqual(CliReturnCode.GENERAL_ERROR, Execute(["start", "--config", ROOT], scope.Session));

            Assert.AreEqual(0, scope.CreatedPaths.Count);
            CliTelemetryEvent[] events = await scope.DrainAsync();
            Assert.AreEqual(1, events.Length);
            Command(events, "start", "validation_failure", "configuration");
        }

        [TestMethod]
        public async Task AppNameDecodeDoesNotObserveEvenAnExplicitConfigOption()
        {
            using RecordingScope scope = new(_fileSystem);

            int result = Execute(["appname", "--decode=" + SENTINEL, "--config", ROOT, "--output", SENTINEL + ".txt"], scope.Session);

            Assert.AreEqual(CliReturnCode.SUCCESS, result);
            Assert.AreEqual(0, scope.CreatedPaths.Count);
            Assert.AreEqual(0, scope.LookupPaths.Count);
            CliTelemetryEvent[] events = await scope.DrainAsync();
            CliTelemetryEvent command = Command(events, "appname", "success", "none");
            Assert.AreEqual("true", command.Properties["option_decode"]);
            Assert.AreEqual("true", command.Properties["option_config"]);
            AssertNoApiIdentity(command);
            AssertPrivateValuesAbsent(events);
        }

        [TestMethod]
        public async Task AppNameGenerationObservesOnlyTheFinalMergedRoot()
        {
            string? previousEnvironment = Environment.GetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME);
            try
            {
                Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, "SyntheticCliTelemetry");
                _fileSystem.AddFile(DEFAULT_CONFIG_FILE_NAME, new MockFileData(INITIAL_CONFIG));
                _fileSystem.AddFile("dab-config.SyntheticCliTelemetry.json", new MockFileData("{ \"runtime\": { \"rest\": { \"enabled\": false } } }"));
                using RecordingScope scope = new(_fileSystem, existingIdentity: true);

                int result = Execute(["appname", "--output", SENTINEL + ".txt"], scope.Session);

                string finalPath = _fileSystem.Path.GetFullPath("dab-config.SyntheticCliTelemetry.merged.json");
                Assert.AreEqual(CliReturnCode.SUCCESS, result);
                Assert.IsTrue(_fileSystem.File.Exists(finalPath));
                CollectionAssert.AreEqual(new[] { finalPath }, scope.LookupPaths.ToArray());
                Assert.AreEqual(0, scope.CreatedPaths.Count, "Writing a merged file is not init or an engine handoff.");
                CliTelemetryEvent command = Command(await scope.DrainAsync(), "appname", "success", "none");
                Assert.AreEqual(scope.ApiId.ToString("D"), command.Properties["dab_api_id"]);
            }
            finally
            {
                Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, previousEnvironment);
            }
        }

        [TestMethod]
        public async Task SuccessfulCommandOverridesAnEarlierRecoverableFailure()
        {
            using RecordingScope scope = new(_fileSystem);
            scope.Session.MarkFailure(CliTelemetryOutcome.ExecutionFailure, CliTelemetryFailureCategory.Execution);

            Assert.AreEqual(CliReturnCode.SUCCESS,
                Execute(["appname", "--decode", SENTINEL, "--output", SENTINEL + ".txt"], scope.Session));

            Command(await scope.DrainAsync(), "appname", "success", "none");
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ThrownWriteFailureOrCancellationCompletesAndRethrowsTheOriginal(bool cancel)
        {
            using RecordingScope scope = new(_fileSystem);
            Exception expected = cancel ? new OperationCanceledException(SENTINEL) : new IOException(SENTINEL);
            Exception? actual = null;
            try
            {
                Execute(["appname", "--decode", SENTINEL, "--output", SENTINEL + ".txt"], scope.Session, ThrowOnWrite(expected));
            }
            catch (Exception exception)
            {
                actual = exception;
            }

            Assert.AreSame(expected, actual);
            CliTelemetryEvent[] events = await scope.DrainAsync();
            Command(events, "appname", cancel ? "canceled" : "execution_failure", cancel ? "canceled" : "storage");
            AssertPrivateValuesAbsent(events);
        }

        [TestMethod]
        public void PublicExecuteRemainsDisabledWhenSyntheticTestModeIsSet()
        {
            const string destinationVariable = "DAB_PRODUCT_TELEMETRY_CONNECTION_STRING";
            string? previousTestMode = Environment.GetEnvironmentVariable(ProductTelemetryPolicy.TEST_MODE_ENV_VAR);
            string? previousDestination = Environment.GetEnvironmentVariable(destinationVariable);
            try
            {
                Environment.SetEnvironmentVariable(ProductTelemetryPolicy.TEST_MODE_ENV_VAR, "true");
                // Do not allow even a regressed bootstrap to contact a real destination or touch
                // a profile. The public overload itself must always pass a null session.
                Environment.SetEnvironmentVariable(destinationVariable, null);
                using FileSystemRuntimeConfigLoader loader = CreateLoader(_fileSystem);

                int result = Program.Execute(["init", "--database-type", "mssql", "--config", ROOT],
                    NullLogger.Instance, _fileSystem, loader);

                Assert.AreEqual(CliReturnCode.SUCCESS, result);
                Assert.AreEqual(2, _fileSystem.AllFiles.Count(), "Only the schema fixture and config may be written.");
                Assert.IsFalse(_fileSystem.AllFiles.Any(path => path.Contains(".dab-telemetry", StringComparison.Ordinal)));
                Assert.AreEqual(string.Empty, _stdout.ToString());
            }
            finally
            {
                Environment.SetEnvironmentVariable(ProductTelemetryPolicy.TEST_MODE_ENV_VAR, previousTestMode);
                Environment.SetEnvironmentVariable(destinationVariable, previousDestination);
            }
        }

        private int Execute(string[] args, CliTelemetrySession session, IFileSystem? fileSystem = null)
        {
            IFileSystem selectedFileSystem = fileSystem ?? _fileSystem;
            using FileSystemRuntimeConfigLoader loader = CreateLoader(selectedFileSystem);
            return Program.Execute(args, NullLogger.Instance, selectedFileSystem, loader, session);
        }

        private static FileSystemRuntimeConfigLoader CreateLoader(IFileSystem fileSystem)
            => new(fileSystem, isCliLoader: true, logger: NullLogger<FileSystemRuntimeConfigLoader>.Instance);

        private IFileSystem ThrowOnWrite(Exception exception)
        {
            Mock<IFile> file = new(MockBehavior.Strict);
            file.Setup(item => item.Exists(It.IsAny<string>())).Returns((string path) => _fileSystem.File.Exists(path));
            file.Setup(item => item.ReadAllText(It.IsAny<string>())).Returns((string path) => _fileSystem.File.ReadAllText(path));
            file.Setup(item => item.WriteAllText(It.IsAny<string>(), It.IsAny<string>())).Throws(exception);
            Mock<IFileSystem> fileSystem = new(MockBehavior.Strict);
            fileSystem.SetupGet(item => item.Path).Returns(_fileSystem.Path);
            fileSystem.SetupGet(item => item.Directory).Returns(_fileSystem.Directory);
            fileSystem.SetupGet(item => item.File).Returns(file.Object);
            return fileSystem.Object;
        }

        private static CliTelemetryEvent Command(CliTelemetryEvent[] events, string name, string outcome, string category)
        {
            CliTelemetryEvent command = events.Single(record => record.Name == "dab.cli.command");
            Assert.AreEqual(name, command.Properties["command"]);
            Assert.AreEqual(outcome, command.Properties["outcome"]);
            Assert.AreEqual(category, command.Properties["failure_category"]);
            Assert.AreNotEqual(Guid.Empty, command.SessionId);
            Assert.IsTrue(command.IsSynthetic);
            Assert.IsTrue(long.Parse(command.Properties["duration_ms"], System.Globalization.CultureInfo.InvariantCulture) >= 0);
            Assert.IsFalse(command.Properties.ContainsKey("dab_config_epoch"));
            AssertPrivateValuesAbsent(events);
            return command;
        }

        private static void AssertNoApiIdentity(CliTelemetryEvent record)
        {
            Assert.IsFalse(record.Properties.ContainsKey("dab_api_id"));
            Assert.IsFalse(record.Properties.ContainsKey("dab_api_id_stability"));
        }

        private static void AssertPrivateValuesAbsent(CliTelemetryEvent[] events)
            => Assert.IsFalse(JsonSerializer.Serialize(events).Contains(SENTINEL, StringComparison.OrdinalIgnoreCase));

        private sealed class RecordingScope : IDisposable
        {
            private readonly RecordingExporter _exporter = new();
            public Guid ApiId { get; } = Guid.NewGuid();
            public ManualClock Clock { get; } = new();
            public CliTelemetrySession Session { get; }
            public List<string?> CreatedPaths { get; } = new();
            public List<string?> LookupPaths { get; } = new();
            public List<bool> FileExistedAtCreation { get; } = new();
            public TimeSpan CreationElapsed { get; set; }
            public int Notices { get; private set; }
            public int Installations { get; private set; }

            public RecordingScope(IFileSystem fileSystem, bool newlySavedInstallation = false, bool existingIdentity = false)
            {
                Session = CliTelemetrySession.Create(() => _exporter, enableSyntheticCollection: true,
                    clock: Clock, readEnvironmentVariable: _ => null, showNotice: () => Notices++,
                    resolveInstallation: () =>
                    {
                        Installations++;
                        return new(Guid.NewGuid(), newlySavedInstallation ? "newly_saved" : "reused");
                    },
                    createIdentity: path =>
                    {
                        CreatedPaths.Add(path);
                        FileExistedAtCreation.Add(path is not null && fileSystem.File.Exists(path));
                        Clock.Advance(CreationElapsed);
                        return new(ApiId, "ephemeral");
                    },
                    lookupIdentity: path =>
                    {
                        LookupPaths.Add(path);
                        return existingIdentity ? new(ApiId, "reused") : null;
                    });
                Assert.IsTrue(Session.IsEnabled, "The test must exercise an enabled injected session.");
            }

            public async Task<CliTelemetryEvent[]> DrainAsync()
            {
                await Session.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
                return _exporter.Records.ToArray();
            }

            public void Dispose() => Session.Dispose();
        }

        private sealed class RecordingExporter : IProductTelemetryExporter<IProductTelemetryEvent>
        {
            public ConcurrentQueue<CliTelemetryEvent> Records { get; } = new();

            public ValueTask<bool> ExportAsync(IProductTelemetryEvent record, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Records.Enqueue((CliTelemetryEvent)record);
                return ValueTask.FromResult(true);
            }

            public void Dispose() { }
        }

        private sealed class ManualClock : TimeProvider
        {
            private long _ticks;
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() => Interlocked.Read(ref _ticks);
            public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(Interlocked.Read(ref _ticks));
            public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
        }
    }
}
