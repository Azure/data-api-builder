// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Cli.Constants;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cli.Tests.Telemetry
{
    /// <summary>
    /// Real CLI parsing/export dispatch with offline generation failures or scripted discovery.
    /// Helper scheduling is canceled before execution; no host, database, SDK or profile is used.
    /// </summary>
    [TestClass]
    [TestCategory("CliTelemetry")]
    [DoNotParallelize]
    public class CliTelemetryExportOutcomeTests
    {
        private const string SENTINEL = "EXPORT_PRIVATE_ARGUMENT_PATH_ERROR_75da";
        private const string SCHEMA = "type Query { synthetic: String }";
        private ILoggerFactory _originalLoggerFactory = null!;

        [TestInitialize]
        public void Initialize()
        {
            _originalLoggerFactory = Utils.LoggerFactoryForCli;
            Utils.SetCliUtilsLogger(NullLogger<Utils>.Instance);
        }

        [TestCleanup]
        public void Cleanup() => Utils.SetCliUtilsLogger(_originalLoggerFactory.CreateLogger<Utils>());

        [DataTestMethod]
        [DataRow("unsupported-database", "Config file passed is not compatible with this feature")]
        [DataRow("invalid-sample-count", "Invalid Configuration Found")]
        [DataRow("empty-generated-schema", "Generated GraphQL schema is empty")]
        [DataRow("empty-service-schema", "Generated GraphQL schema is empty")]
        [DataRow("retry-then-empty", "Generated GraphQL schema is empty")]
        public async Task TerminalNoSchemaIsFailureWithoutChangingTheLegacySuccessExitCode(string scenario, string expectedDiagnostic)
        {
            ExportRun baseline = await RunAsync(scenario, enabled: false);
            ExportRun observed = await RunAsync(scenario, enabled: true);

            AssertEquivalentBehavior(baseline, observed);
            Assert.AreEqual(CliReturnCode.SUCCESS, observed.ExitCode, "Do not fix the pre-existing exit code as a telemetry side effect.");
            Assert.IsNull(observed.Schema);
            Assert.AreEqual(0, observed.WriteAttempts);
            Assert.IsTrue(observed.Logs.Any(message => message.Contains(expectedDiagnostic, StringComparison.Ordinal)),
                "The real generation/empty-result boundary must be reached, not a parser or earlier configuration rejection.");
            AssertCommand(observed, "execution_failure", "execution");
            if (scenario == "retry-then-empty")
            {
                CollectionAssert.AreEqual(new[] { false, true, false }, observed.DiscoveryAttempts);
                Assert.AreEqual(CliTelemetryFailureCategory.Storage, observed.FirstAttemptFailure,
                    "A terminal empty result must not inherit an earlier recoverable storage failure's category.");
            }
        }

        [DataTestMethod]
        [DataRow("valid-schema", 1, 1)]
        [DataRow("discovery-retry", 3, 1)]
        [DataRow("write-retry", 2, 2)]
        [DataRow("cancellation-retry", 3, 1)]
        public async Task SuccessfulSchemaAndRecoveredAttemptsRemainSuccessful(string scenario, int discoveryAttempts, int writeAttempts)
        {
            ExportRun baseline = await RunAsync(scenario, enabled: false);
            ExportRun observed = await RunAsync(scenario, enabled: true);

            AssertEquivalentBehavior(baseline, observed);
            Assert.AreEqual(CliReturnCode.SUCCESS, observed.ExitCode);
            Assert.AreEqual(SCHEMA, observed.Schema);
            Assert.AreEqual(discoveryAttempts, observed.DiscoveryAttempts.Length);
            Assert.AreEqual(writeAttempts, observed.WriteAttempts);
            Assert.AreEqual(scenario != "valid-schema", observed.HadAttemptFailure,
                "Retry controls must actually record a failed attempt before succeeding.");
            AssertCommand(observed, "success", "none");
        }

        [DataTestMethod]
        [DataRow("exhausted-discovery", "execution_failure", "storage", 10, 0)]
        [DataRow("exhausted-write", "execution_failure", "storage", 5, 5)]
        [DataRow("exhausted-cancellation", "canceled", "canceled", 10, 0)]
        public async Task ExhaustedAttemptsKeepTheirOriginalErrorExitAndClassification(
            string scenario, string outcome, string category, int discoveryAttempts, int writeAttempts)
        {
            ExportRun baseline = await RunAsync(scenario, enabled: false);
            ExportRun observed = await RunAsync(scenario, enabled: true);

            AssertEquivalentBehavior(baseline, observed);
            Assert.AreEqual(CliReturnCode.GENERAL_ERROR, observed.ExitCode);
            Assert.IsNull(observed.Schema);
            Assert.AreEqual(discoveryAttempts, observed.DiscoveryAttempts.Length);
            Assert.AreEqual(writeAttempts, observed.WriteAttempts);
            AssertCommand(observed, outcome, category);
        }

        private static async Task<ExportRun> RunAsync(string scenario, bool enabled)
        {
            MockFileSystem files = new();
            string root = files.Path.GetFullPath(files.Path.Combine("export-outcome", SENTINEL + ".json"));
            string directory = files.Path.GetDirectoryName(root)!;
            string outputDirectory = files.Path.Combine(directory, SENTINEL + "-output");
            string output = files.Path.Combine(outputDirectory, "schema.gql");
            bool generated = scenario is "unsupported-database" or "invalid-sample-count" or "empty-generated-schema";
            string databaseType = scenario is "invalid-sample-count" or "empty-generated-schema" ? "cosmosdb_nosql" : "mssql";
            files.AddFile(root, new MockFileData($$"""
                {
                  "data-source": {
                    "database-type": "{{databaseType}}",
                    "connection-string": "Server=unused.invalid;Database={{SENTINEL}};Integrated Security=true",
                    "options": { "database": "{{SENTINEL}}" }
                  },
                  "entities": {}
                }
                """));

            int writeAttempts = 0;
            Mock<IFile> file = new(MockBehavior.Strict);
            file.Setup(item => item.Exists(It.IsAny<string>())).Returns((string path) => files.File.Exists(path));
            file.Setup(item => item.ReadAllText(It.IsAny<string>())).Returns((string path) => files.File.ReadAllText(path));
            file.Setup(item => item.WriteAllText(It.IsAny<string>(), It.IsAny<string>())).Callback((string path, string content) =>
            {
                writeAttempts++;
                if (scenario == "exhausted-write" || (scenario == "write-retry" && writeAttempts == 1))
                {
                    throw new IOException(SENTINEL);
                }

                files.File.WriteAllText(path, content);
            });
            Mock<IFileSystem> fileSystem = new(MockBehavior.Strict);
            fileSystem.SetupGet(item => item.File).Returns(file.Object);
            fileSystem.SetupGet(item => item.Path).Returns(files.Path);
            fileSystem.SetupGet(item => item.Directory).Returns(files.Directory);

            ScriptedExporter exporter = new(scenario);
            RecordingLogger logger = new();
            RecordingSender sender = new();
            int identityWork = 0;
            int notices = 0;
            int engineLaunches = 0;
            using CliTelemetrySession telemetry = CliTelemetrySession.Create(() => sender, enableSyntheticCollection: enabled,
                readEnvironmentVariable: _ => null, showNotice: () => notices++,
                resolveInstallation: () => { identityWork++; return new(Guid.NewGuid(), "reused"); },
                lookupIdentity: _ => { identityWork++; return null; },
                createIdentity: _ => throw new AssertFailedException("Export must not mint an API identity without a real engine handoff."));
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            using FileSystemRuntimeConfigLoader loader = new(fileSystem.Object, isCliLoader: true,
                logger: NullLogger<FileSystemRuntimeConfigLoader>.Instance);
            List<string> args = ["export", "--graphql", "--config", root, "--output", outputDirectory];
            if (generated)
            {
                args.Add("--generate");
            }

            if (scenario == "invalid-sample-count")
            {
                args.AddRange(["--sampling-count", "0"]);
            }

            TextWriter originalOut = Console.Out;
            TextWriter originalError = Console.Error;
            using StringWriter stdout = new();
            using StringWriter stderr = new();
            int exitCode;
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                exitCode = Program.Execute(args.ToArray(), logger, fileSystem.Object, loader, enabled ? telemetry : null,
                    engineLauncher: (_, _, _) => { Interlocked.Increment(ref engineLaunches); return false; },
                    exporterFactory: () => exporter, exportCancellationTokenSource: cancellation);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            await telemetry.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(0, Volatile.Read(ref engineLaunches), "Canceled helper scheduling must keep this test completely offline.");
            Assert.AreEqual(enabled ? 1 : 0, notices);
            Assert.AreEqual(enabled ? 2 : 0, identityWork, "Only installation initialization and lookup-only root observation are allowed.");
            return new(exitCode, files.File.Exists(output) ? files.File.ReadAllText(output) : null,
                exporter.Attempts.ToArray(), writeAttempts, logger.Entries.ToArray(), stdout.ToString(), stderr.ToString(),
                sender.Events.ToArray(), telemetry.HasFailure, telemetry.FailureCategory);
        }

        private static void AssertEquivalentBehavior(ExportRun baseline, ExportRun observed)
        {
            Assert.AreEqual(baseline.ExitCode, observed.ExitCode);
            Assert.AreEqual(baseline.Schema, observed.Schema);
            Assert.AreEqual(baseline.WriteAttempts, observed.WriteAttempts);
            CollectionAssert.AreEqual(baseline.DiscoveryAttempts, observed.DiscoveryAttempts);
            CollectionAssert.AreEqual(baseline.Logs, observed.Logs, "Telemetry must not change command diagnostics.");
            Assert.AreEqual(baseline.Stdout, observed.Stdout);
            Assert.AreEqual(baseline.Stderr, observed.Stderr);
            Assert.AreEqual(0, baseline.Events.Length);
        }

        private static void AssertCommand(ExportRun result, string outcome, string category)
        {
            Assert.AreEqual(1, result.Events.Length, "Only one top-level completion; no speculative helper launch.");
            CliTelemetryEvent command = result.Events.Single();
            Assert.AreEqual("dab.cli.command", command.Name);
            Assert.AreEqual("export", command.Properties["command"]);
            Assert.AreEqual(outcome, command.Properties["outcome"]);
            Assert.AreEqual(category, command.Properties["failure_category"]);
            Assert.AreEqual("none", command.Properties["control"]);
            Assert.IsFalse(JsonSerializer.Serialize(result.Events).Contains(SENTINEL, StringComparison.Ordinal));
        }

        private sealed record ExportRun(int ExitCode, string? Schema, bool[] DiscoveryAttempts, int WriteAttempts,
            string[] Logs, string Stdout, string Stderr, CliTelemetryEvent[] Events,
            bool HadAttemptFailure, CliTelemetryFailureCategory FirstAttemptFailure);

        private sealed class ScriptedExporter(string scenario) : Exporter
        {
            public List<bool> Attempts { get; } = new();

            internal override string GetGraphQLSchema(RuntimeConfig runtimeConfig, bool useFallbackUrl = false)
            {
                Attempts.Add(useFallbackUrl);
                if (scenario is "unsupported-database" or "invalid-sample-count" or "empty-generated-schema")
                {
                    throw new AssertFailedException("Generation must exercise SchemaGeneratorFactory rather than discovery.");
                }

                if (scenario is "exhausted-discovery" or "exhausted-cancellation"
                    || (Attempts.Count <= 2 && scenario is "discovery-retry" or "retry-then-empty" or "cancellation-retry"))
                {
                    throw scenario is "cancellation-retry" or "exhausted-cancellation"
                        ? new OperationCanceledException(SENTINEL) : new IOException(SENTINEL);
                }

                return scenario is "empty-service-schema" or "retry-then-empty" ? string.Empty : SCHEMA;
            }
        }

        private sealed class RecordingSender : IProductTelemetryExporter<IProductTelemetryEvent>
        {
            public ConcurrentQueue<CliTelemetryEvent> Events { get; } = new();

            public ValueTask<bool> ExportAsync(IProductTelemetryEvent record, CancellationToken cancellationToken)
            {
                Events.Enqueue((CliTelemetryEvent)record);
                return ValueTask.FromResult(true);
            }

            public void Dispose() { }
        }

        private sealed class RecordingLogger : ILogger
        {
            public ConcurrentQueue<string> Entries { get; } = new();
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) => Entries.Enqueue(formatter(state, exception));
        }
    }
}
