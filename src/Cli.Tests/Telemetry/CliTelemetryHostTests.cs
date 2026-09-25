// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Data;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text;
using System.Threading.Channels;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Telemetry;
using Azure.DataApiBuilder.Service;
using Azure.DataApiBuilder.Service.Telemetry;
using Cli.Constants;
using HotChocolate.Language;
using HotChocolate.Utilities.Introspection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using EngineProgram = Azure.DataApiBuilder.Service.Program;
using IOPath = System.IO.Path;

namespace Cli.Tests.Telemetry
{
    /// <summary>
    /// Real Execute/parser/handler -> service bootstrap -> metadata -> request -> shutdown
    /// coverage. Only telemetry delivery/installation, HTTP transport and stdio streams are
    /// substituted. Requires Windows and the fixed current-user MSSQLLocalDB instance; each
    /// integration case owns its database and config directory. Never calls Main, changes
    /// product-telemetry environment variables, contacts a cloud sink, or writes a real profile.
    /// Kept out of the database-free EngineTelemetry category deliberately.
    /// </summary>
    [TestClass]
    [DoNotParallelize]
    [TestCategory("CliTelemetryLocalDb")]
    public class CliTelemetryHostTests
    {
        private const string COMMAND = "dab.cli.command";
        private const string LAUNCH = "dab.cli.engine_launch";
        private const string PROCESS_STARTED = "dab.engine.process_started";
        private const string READY = "dab.engine.ready";
        private const string FIRST_SERVED = "dab.engine.first_request_served";
        private const string FIRST_SUCCESS = "dab.engine.first_successful_request";
        private const string STARTUP_FAILED = "dab.engine.startup_failed";
        private const string STOPPED = "dab.engine.stopped";
        private const string PRIVATE_TITLE = "CLI_HOST_PRIVATE_ROW_769f17";
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan _startupTimeout = TimeSpan.FromMinutes(2);
        private HostSettingsScope _settings = null!;

        [TestInitialize]
        public void Initialize() => _settings = new();

        [TestCleanup]
        public void Cleanup() => _settings.Dispose();

        [TestMethod]
        public async Task InitAddStartWebKeepsTheSidecarIdentityAndCompletesOnlyAfterHostShutdown()
        {
            await using WorkflowFixture fixture = await WorkflowFixture.CreateAsync();
            await fixture.InitializeConfigurationAsync();
            ManualClock clock = new();
            using CliTelemetrySession cli = fixture.CreateCli(clock);
            await using EngineInvocation engine = new(fixture, pauseHttpStartup: true);

            Task<int> command = engine.ExecuteAsync(cli,
                ["start", "--config", fixture.ConfigPath, "--log-level", "None", "--no-https-redirect"]);

            // The startup filter is outside Startup.Configure. It holds the real host before
            // runtime validation/metadata; neither this test nor the probe accepts a config.
            IServiceProvider services = await engine.Probe.Captured.Task.WaitAsync(_startupTimeout);
            await AssertBootstrapLinkAsync(fixture, cli, engine, CliTelemetryLaunchSource.StartWeb);
            Assert.IsFalse(engine.Session!.IsReady);
            Assert.IsFalse(engine.Probe.ReadyWhenCaptured);
            Assert.IsFalse(engine.Probe.Lifetime!.ApplicationStarted.IsCancellationRequested);
            Assert.IsFalse(command.IsCompleted);
            AssertNoCommand(fixture, cli);
            Assert.IsFalse(fixture.Events.Engine(engine.Context!.EngineSessionId).Any(record => record.Name == READY));

            clock.Advance(TimeSpan.FromMinutes(1));
            engine.Probe.ReleaseStartup();
            EngineTelemetryEvent ready = await fixture.Events.WaitAsync<EngineTelemetryEvent>(engine.Context.EngineSessionId, READY);
            await engine.Probe.Started.Task.WaitAsync(_startupTimeout);
            AssertReady(fixture, engine, services, ready);

            // Use the actual Startup pipeline, REST engine, SQL executor and owned seed row.
            await AssertBookResponseAsync(engine.Probe);
            EngineTelemetryEvent success = await fixture.Events.WaitAsync<EngineTelemetryEvent>(engine.Context.EngineSessionId, FIRST_SUCCESS);
            Assert.AreEqual("rest", success.Properties["api"]);
            Assert.IsFalse(command.IsCompleted, "A successful first request must not complete dab start.");
            AssertNoCommand(fixture, cli);

            clock.Advance(TimeSpan.FromMinutes(2));
            engine.Probe.Lifetime.StopApplication();
            Assert.AreEqual(CliReturnCode.SUCCESS, await command.WaitAsync(_timeout));
            await cli.StopAsync().WaitAsync(_timeout);

            CliTelemetryEvent completed = AssertCommand(fixture, cli, "start", "success", "none");
            Assert.AreEqual("180000", completed.Properties["duration_ms"], "CLI duration includes pre-metadata startup and the serving lifetime.");
            AssertSuccessfulLifetime(fixture, cli, engine, api: "rest", transport: "http");
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task StartStdioServesRealReadRecordsUntilShutdownOrEofWithoutStartingHttp(bool endWithEof)
        {
            await using WorkflowFixture fixture = await WorkflowFixture.CreateAsync();
            await fixture.InitializeConfigurationAsync();
            using CliTelemetrySession cli = fixture.CreateCli();
            using ChannelTextReader input = new();
            using ProtocolWriter output = new();
            await using EngineInvocation engine = new(fixture, input: input, output: output);

            Task<int> command = engine.ExecuteAsync(cli,
                ["start", "--config", fixture.ConfigPath, "--mcp-stdio", "--log-level", "None"]);
            await engine.Created.Task.WaitAsync(_startupTimeout);
            await AssertBootstrapLinkAsync(fixture, cli, engine, CliTelemetryLaunchSource.StartStdio);
            IServiceProvider services = await engine.Probe.Captured.Task.WaitAsync(_startupTimeout);
            EngineTelemetryEvent ready = await fixture.Events.WaitAsync<EngineTelemetryEvent>(engine.Context!.EngineSessionId, READY);
            await input.Reading.Task.WaitAsync(_timeout);
            AssertReady(fixture, engine, services, ready);

            Assert.IsFalse(engine.Probe.ReadyWhenCaptured, "The stdio server is resolved before the real helper marks readiness.");
            Assert.IsFalse(engine.Probe.HttpPipelineConfigured, "The real stdio helper must initialize metadata without Startup.Configure/HTTP Start.");
            CancellationToken applicationStarted = engine.Probe.Lifetime!.ApplicationStarted;
            Assert.IsFalse(applicationStarted.IsCancellationRequested);
            Assert.IsInstanceOfType(services.GetRequiredService<IMcpStdioServer>(), typeof(McpStdioServer));
            Assert.AreSame(engine.Stdout, services.GetServices<McpStdoutWriter>().Single());
            McpToolRegistry registry = services.GetRequiredService<McpToolRegistry>();
            Assert.IsTrue(registry.TryGetTool("read_records", out IMcpTool? tool));
            Assert.AreSame(services.GetServices<IMcpTool>().OfType<ReadRecordsTool>().Single(), tool);
            Assert.IsFalse(command.IsCompleted, "An open input channel must keep the actual CLI command alive.");

            input.Send(Rpc(1, "initialize", new
            {
                protocolVersion = "2025-03-26",
                capabilities = new { },
                clientInfo = new { name = "cli-lifetime-test", version = "1.0" }
            }));
            JsonElement initialized = await output.ResponseAsync(1);
            Assert.AreEqual("2025-03-26", initialized.GetProperty("result").GetProperty("protocolVersion").GetString());
            input.Send("""{"jsonrpc":"2.0","method":"notifications/initialized"}""");
            input.Send(Rpc(2, "tools/list"));
            JsonElement listed = await output.ResponseAsync(2);
            Assert.IsTrue(listed.GetProperty("result").GetProperty("tools").EnumerateArray()
                .Any(item => item.GetProperty("name").GetString() == "read_records"));
            AssertNoCommand(fixture, cli);

            input.Send(Rpc(3, "tools/call", new { name = "read_records", arguments = new { entity = "Books", first = 10 } }));
            JsonElement read = await output.ResponseAsync(3);
            Assert.IsFalse(read.TryGetProperty("error", out _));
            JsonElement result = read.GetProperty("result");
            Assert.IsFalse(result.TryGetProperty("isError", out JsonElement isError) && isError.GetBoolean());
            JsonElement content = result.GetProperty("content");
            Assert.AreEqual(1, content.GetArrayLength());
            Assert.AreEqual("text", content[0].GetProperty("type").GetString());
            using (JsonDocument payload = JsonDocument.Parse(content[0].GetProperty("text").GetString()!))
            {
                Assert.AreEqual("success", payload.RootElement.GetProperty("status").GetString());
                AssertRows(payload.RootElement.GetProperty("result").GetProperty("value"));
            }

            await fixture.Events.WaitAsync<EngineTelemetryEvent>(engine.Context.EngineSessionId, FIRST_SUCCESS);
            Assert.IsFalse(command.IsCompleted, "Returning a real tool response is not CLI completion.");
            AssertNoCommand(fixture, cli);
            if (endWithEof)
            {
                input.Complete();
            }
            else
            {
                input.Send(Rpc(4, "shutdown"));
                JsonElement shutdown = await output.ResponseAsync(4);
                Assert.IsTrue(shutdown.GetProperty("result").GetProperty("ok").GetBoolean());
            }

            Assert.AreEqual(CliReturnCode.SUCCESS, await command.WaitAsync(_timeout));
            await cli.StopAsync().WaitAsync(_timeout);
            Assert.IsFalse(applicationStarted.IsCancellationRequested, "No HTTP host may be started, even transiently.");
            Assert.IsFalse(engine.Probe.HttpPipelineConfigured);
            JsonElement[] responses = output.ReadAll(); // Parses EVERY captured stdout line, not just known responses.
            Assert.IsTrue(responses.All(response => response.GetProperty("jsonrpc").GetString() == "2.0"));
            CollectionAssert.AreEqual(Enumerable.Range(1, endWithEof ? 3 : 4).ToArray(), responses
                .Where(response => response.TryGetProperty("id", out _))
                .Select(response => response.GetProperty("id").GetInt32()).ToArray());
            AssertCommand(fixture, cli, "start", "success", "none");
            AssertSuccessfulLifetime(fixture, cli, engine, api: "mcp", transport: "stdio");
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task RealMetadataFailureReturnsLinkedInitializationFailureWithoutReady(bool stdio)
        {
            await using WorkflowFixture fixture = await WorkflowFixture.CreateAsync();
            // The config passes CLI preflight but names an absent table in THIS owned database.
            // No arbitrary server, authentication failure, fabricated exception or mock metadata.
            await fixture.InitializeConfigurationAsync(source: "dbo.MissingBooks");
            using CliTelemetrySession cli = fixture.CreateCli();
            using ChannelTextReader input = new();
            using ProtocolWriter output = new();
            await using EngineInvocation engine = stdio
                ? new(fixture, input: input, output: output)
                : new(fixture);
            string[] arguments = stdio
                ? ["start", "--config", fixture.ConfigPath, "--mcp-stdio", "--log-level", "Error"]
                : ["start", "--config", fixture.ConfigPath, "--log-level", "None", "--no-https-redirect"];

            Task<int> command = engine.ExecuteAsync(cli, arguments);
            await engine.Created.Task.WaitAsync(_startupTimeout);
            await AssertBootstrapLinkAsync(fixture, cli, engine,
                stdio ? CliTelemetryLaunchSource.StartStdio : CliTelemetryLaunchSource.StartWeb);
            Assert.AreEqual(stdio ? CliReturnCode.GENERAL_ERROR : CliReturnCode.SUCCESS, await command.WaitAsync(_startupTimeout),
                "Preserve the existing web host's normal-return exit code even though telemetry observes failed initialization.");
            await cli.StopAsync().WaitAsync(_timeout);

            Assert.AreEqual(1, engine.LaunchCalls, "A real launch occurred; this is not a CLI preflight rejection.");
            CliTelemetryEvent completed = AssertCommand(fixture, cli, "start", "execution_failure", "initialization");
            EngineTelemetryEvent[] records = fixture.Events.Engine(engine.Context!.EngineSessionId);
            CollectionAssert.AreEqual(new[] { PROCESS_STARTED, STARTUP_FAILED, STOPPED }, records.Select(record => record.Name).ToArray());
            EngineTelemetryEvent failure = records.Single(record => record.Name == STARTUP_FAILED);
            Assert.AreEqual("metadata", failure.Properties["failure_stage"]);
            Assert.AreEqual("initialization", failure.Properties["failure_category"]);
            Assert.IsTrue(records.All(record => record.ConfigurationEpoch == 0));
            Assert.IsFalse(records.Any(record => record.Properties.ContainsKey("runtime.rest.effective")),
                "Rejected metadata must never be reported as an accepted configuration snapshot.");
            Assert.AreEqual(0, engine.IdentityResolutions);
            AssertLinkage(fixture, cli, engine, completed);
            AssertPrivateValuesAbsent(fixture);
        }

        [TestMethod]
        public async Task ExportUsesItsRealHelperHostAndFinishesWhileTheHelperStillServes()
        {
            await using WorkflowFixture fixture = await WorkflowFixture.CreateAsync();
            await fixture.InitializeConfigurationAsync();
            using CliTelemetrySession cli = fixture.CreateCli();
            using CancellationTokenSource exportCancellation = new();
            await using EngineInvocation engine = new(fixture);
            TestServerExporter exporter = new(engine.Probe);
            string outputDirectory = IOPath.Combine(fixture.DirectoryPath, "export");

            Task<int> command = engine.ExecuteAsync(cli,
                ["export", "--graphql", "--config", fixture.ConfigPath, "--output", outputDirectory],
                () => exporter, exportCancellation);

            Assert.AreEqual(CliReturnCode.SUCCESS, await command.WaitAsync(_startupTimeout));
            await AssertBootstrapLinkAsync(fixture, cli, engine, CliTelemetryLaunchSource.ExportGraphQL);
            IServiceProvider services = await engine.Probe.Captured.Task.WaitAsync(_timeout);
            EngineTelemetryEvent ready = await fixture.Events.WaitAsync<EngineTelemetryEvent>(engine.Context!.EngineSessionId, READY);
            AssertReady(fixture, engine, services, ready);
            Assert.IsTrue(exportCancellation.IsCancellationRequested, "Keep the export command's existing cancellation action.");
            Assert.IsFalse(engine.Returned.Task.IsCompleted,
                "The legacy helper is not stopped by canceling the Task.Run scheduling token. Do not silently fix that behavior here.");
            Assert.IsFalse(engine.Probe.Lifetime!.ApplicationStopping.IsCancellationRequested);
            CollectionAssert.AreEqual(new[] { false }, exporter.FallbackAttempts.ToArray());

            string schema = await File.ReadAllTextAsync(IOPath.Combine(outputDirectory, "schema.gql"));
            DocumentNode document = Utf8GraphQLParser.Parse(schema);
            ObjectTypeDefinitionNode book = document.Definitions.OfType<ObjectTypeDefinitionNode>().Single(type => type.Name.Value == "Book");
            CollectionAssert.IsSubsetOf(new[] { "id", "title" }, book.Fields.Select(field => field.Name.Value).ToArray());
            Assert.IsTrue(document.Definitions.OfType<ObjectTypeDefinitionNode>().Single(type => type.Name.Value == "Query")
                .Fields.Any(field => field.Name.Value == "books"), "Exported SDL must come from the real metadata-backed schema.");

            // Reproduce Main's ownership boundary: drain/dispose CLI collection while its
            // already launched helper remains alive. Later real requests belong to the engine.
            await cli.StopAsync().WaitAsync(_timeout);
            cli.Dispose();
            CliTelemetryEvent completed = AssertCommand(fixture, cli, "export", "success", "none");
            Assert.AreEqual(2, fixture.Events.Cli(cli.SessionId).Length, "Only engine_launch and export command; no nested start command.");
            await AssertBookResponseAsync(engine.Probe);
            await fixture.Events.WaitAsync<EngineTelemetryEvent>(engine.Context.EngineSessionId, FIRST_SUCCESS);
            Assert.IsFalse(engine.Returned.Task.IsCompleted);

            engine.Probe.Lifetime.StopApplication();
            Assert.IsTrue(await engine.Returned.Task.WaitAsync(_timeout));
            Assert.AreSame(completed, fixture.Events.Cli(cli.SessionId).Single(record => record.Name == COMMAND));
            AssertSuccessfulLifetime(fixture, cli, engine, api: "rest", transport: "http");
        }

        [TestMethod]
        public async Task ExportCompletingBeforeHelperPreflightPreservesItsReservedLaunchAfterCliDisposal()
        {
            // No database/server is used in this scheduling regression. The real export handler
            // and real helper preflight execute; only schema retrieval and the final launcher
            // are controlled. Main's ordinary shutdown must not revoke the scheduled helper.
            await using WorkflowFixture fixture = await WorkflowFixture.CreateAsync(createDatabase: false);
            await fixture.InitializeConfigurationAsync();
            using CliTelemetrySession cli = fixture.CreateCli();
            using CancellationTokenSource exportCancellation = new();
            TaskCompletionSource preflightEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource releasePreflight = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<ProductTelemetryLaunchContext?> launched = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int configurationReads = 0;
            int launches = 0;
            Mock<IFile> file = new(MockBehavior.Strict);
            file.Setup(item => item.Exists(It.IsAny<string>())).Returns((string path) => fixture.FileSystem.File.Exists(path));
            file.Setup(item => item.ReadAllText(It.IsAny<string>())).Returns((string path) =>
            {
                if (string.Equals(IOPath.GetFullPath(path), fixture.ConfigPath, StringComparison.OrdinalIgnoreCase)
                    && Interlocked.Increment(ref configurationReads) == 2)
                {
                    // First read: Export's load. Second read: background helper's real preflight.
                    preflightEntered.TrySetResult();
                    releasePreflight.Task.WaitAsync(_startupTimeout).GetAwaiter().GetResult();
                }

                return fixture.FileSystem.File.ReadAllText(path);
            });
            file.Setup(item => item.WriteAllText(It.IsAny<string>(), It.IsAny<string>()))
                .Callback((string path, string text) => fixture.FileSystem.File.WriteAllText(path, text));
            Mock<IFileSystem> fileSystem = new(MockBehavior.Strict);
            fileSystem.SetupGet(item => item.File).Returns(file.Object);
            fileSystem.SetupGet(item => item.Path).Returns(fixture.FileSystem.Path);
            fileSystem.SetupGet(item => item.Directory).Returns(fixture.FileSystem.Directory);
            using FileSystemRuntimeConfigLoader loader = CreateLoader(fileSystem.Object);
            CallbackExporter exporter = new(() =>
            {
                preflightEntered.Task.WaitAsync(_startupTimeout, exportCancellation.Token).GetAwaiter().GetResult();
                return "type Query { synthetic: String }";
            });

            Task<int> command = Task.Run(() => Program.Execute(
                ["export", "--graphql", "--config", fixture.ConfigPath, "--output", IOPath.Combine(fixture.DirectoryPath, "export")],
                NullLogger.Instance, fileSystem.Object, loader, cli,
                engineLauncher: (_, context, _) =>
                {
                    Interlocked.Increment(ref launches);
                    launched.TrySetResult(context);
                    return true;
                }, exporterFactory: () => exporter, exportCancellationTokenSource: exportCancellation));

            try
            {
                await preflightEntered.Task.WaitAsync(_startupTimeout);
                Assert.AreEqual(CliReturnCode.SUCCESS, await command.WaitAsync(_timeout));
                CliTelemetryEvent completed = await fixture.Events.WaitAsync<CliTelemetryEvent>(cli.SessionId, COMMAND);
                Assert.AreEqual("export", completed.Properties["command"]);
                Assert.AreEqual(0, Volatile.Read(ref launches));
                Assert.IsFalse(launched.Task.IsCompleted);
                Assert.AreSame(completed, fixture.Events.Cli(cli.SessionId).Single(),
                    "Scheduling/preflight cannot emit a speculative engine_launch.");

                // Reproduce Main's finally BEFORE preflight is released. Neither command
                // return nor Stop/Dispose may wait for the helper or lose its reserved bridge.
                await cli.StopAsync().WaitAsync(_timeout);
                cli.Dispose();
                Assert.IsFalse(cli.IsEnabled);
                Assert.IsNull(cli.BeginEngineLaunch(fixture.ConfigPath, CliTelemetryLaunchSource.ExportGraphQL),
                    "Only the previously reserved helper may launch after completion.");
                Assert.AreEqual(0, Volatile.Read(ref launches));
                Assert.AreSame(completed, fixture.Events.Cli(cli.SessionId).Single());
                releasePreflight.TrySetResult();
                ProductTelemetryLaunchContext? context = await launched.Task.WaitAsync(_timeout);
                Assert.IsNotNull(context, "The actual approved handoff must consume its reservation even after CLI disposal.");
                CliTelemetryEvent launch = await fixture.Events.WaitAsync<CliTelemetryEvent>(cli.SessionId, LAUNCH);
                Assert.AreEqual(CliTelemetryLaunchSource.ExportGraphQL, context.Source);
                Assert.AreEqual(cli.SessionId, context.ParentCliSessionId);
                Assert.AreNotEqual(Guid.Empty, context.EngineSessionId);
                Assert.AreNotEqual(cli.SessionId, context.EngineSessionId);
                Assert.AreEqual(fixture.ApiId, context.ApiIdentity!.ApiId);
                Assert.AreEqual("reused", context.ApiIdentity.Stability);
                Assert.AreEqual(fixture.InstallationId, context.Installation.InstallationId);
                Assert.AreEqual(context.EngineSessionId.ToString("D"), launch.Properties["dab_launched_engine_session_id"]);
                Assert.AreEqual(cli.SessionId.ToString("D"), launch.Properties["dab_parent_cli_session_id"]);
                Assert.AreEqual(fixture.ApiId.ToString("D"), launch.Properties["dab_api_id"]);
                Assert.AreEqual(fixture.InstallationId.ToString("D"), launch.Properties["dab_installation_id"]);
                Assert.AreEqual("export_graphql", launch.Properties["launch_source"]);
                Assert.AreEqual(completed.Sequence + 1, launch.Sequence);
                Assert.AreEqual(1, launches);
                CliTelemetryEvent[] records = fixture.Events.Cli(cli.SessionId);
                CollectionAssert.AreEqual(new[] { COMMAND, LAUNCH }, records.Select(record => record.Name).ToArray(),
                    "Exactly one actual late launch, not a nested start command or another completion.");
                Assert.AreSame(completed, records[0]);
                Assert.AreSame(launch, records[1]);
                CollectionAssert.AreEqual(fixture.SidecarBytes, File.ReadAllBytes(fixture.ConfigPath + ".dab-telemetry.json"));
                AssertPrivateValuesAbsent(fixture);
            }
            finally
            {
                exportCancellation.Cancel();
                releasePreflight.TrySetResult();
                await command.WaitAsync(_startupTimeout);
                if (preflightEntered.Task.IsCompletedSuccessfully)
                {
                    await launched.Task.WaitAsync(_timeout);
                }

                await cli.StopAsync().WaitAsync(_timeout);
            }
        }

        private static async Task AssertBootstrapLinkAsync(WorkflowFixture fixture, CliTelemetrySession cli,
            EngineInvocation engine, CliTelemetryLaunchSource source)
        {
            await engine.Created.Task.WaitAsync(_startupTimeout);
            ProductTelemetryLaunchContext? context = engine.Context;
            Assert.IsNotNull(context, "The actual preflight-approved ConfigGenerator handoff must supply the bridge.");
            Assert.AreSame(context, engine.FactoryContext, "Service bootstrap must consume the exact CLI-owned immutable context.");
            Assert.AreEqual(source, context.Source);
            Assert.AreEqual(cli.SessionId, context.ParentCliSessionId);
            Assert.AreNotEqual(cli.SessionId, context.EngineSessionId);
            Assert.AreEqual(fixture.ApiId, context.ApiIdentity!.ApiId);
            Assert.AreEqual("reused", context.ApiIdentity.Stability);
            Assert.AreEqual(fixture.InstallationId, context.Installation.InstallationId);
            Assert.AreEqual(fixture.ConfigPath, engine.FactoryConfigPath);
            Assert.AreEqual(source == CliTelemetryLaunchSource.StartStdio, engine.FactoryStdio);
            Assert.AreEqual(1, engine.FactoryCalls);
            EngineTelemetryEvent started = await fixture.Events.WaitAsync<EngineTelemetryEvent>(context.EngineSessionId, PROCESS_STARTED);
            Assert.AreEqual(1L, started.Sequence);
            Assert.AreEqual(0L, started.ConfigurationEpoch);
            Assert.AreEqual(fixture.ApiId.ToString("D"), started.Properties["dab_api_id"],
                "Identity must be adopted in process_started, not repaired after metadata acceptance.");
            Assert.IsFalse(started.Properties.ContainsKey("scale.entity_count"));
            Assert.IsFalse(started.Properties.ContainsKey("runtime.rest.effective"));
            CliTelemetryEvent launch = await fixture.Events.WaitAsync<CliTelemetryEvent>(cli.SessionId, LAUNCH);
            Assert.AreEqual(context.EngineSessionId.ToString("D"), launch.Properties["dab_launched_engine_session_id"]);
            Assert.AreEqual(0, engine.IdentityResolutions, "The engine must not independently regenerate the API identity.");
        }

        private static void AssertReady(WorkflowFixture fixture, EngineInvocation engine, IServiceProvider services, EngineTelemetryEvent ready)
        {
            Assert.IsTrue(engine.Session!.IsReady);
            Assert.AreSame(engine.Session, services.GetRequiredService<EngineTelemetrySession>());
            Assert.AreEqual(1L, ready.ConfigurationEpoch);
            Assert.AreEqual("startup", ready.Properties["configuration_delivery"]);
            Assert.AreEqual("mssql", ready.Properties["data_sources.types"]);
            Assert.AreEqual("1", ready.Properties["scale.entity_count"]);
            Assert.AreEqual("enabled", ready.Properties["runtime.rest.effective"]);
            Assert.AreEqual("enabled", ready.Properties["runtime.graphql.effective"]);
            Assert.AreEqual("enabled", ready.Properties["runtime.mcp.effective"]);
            RuntimeConfig configuration = services.GetRequiredService<RuntimeConfigProvider>().GetConfig();
            Assert.AreEqual(1, configuration.Entities.Count());
            Assert.IsTrue(configuration.Entities.ContainsKey("Books"));
            SqlConnectionStringBuilder connection = new(configuration.DataSource!.ConnectionString);
            Assert.AreEqual(@"(localdb)\MSSQLLocalDB", connection.DataSource);
            Assert.AreEqual(fixture.DatabaseName, connection.InitialCatalog);
            Assert.IsTrue(connection.IntegratedSecurity);
        }

        private static async Task AssertBookResponseAsync(HostProbe probe)
        {
            (HttpStatusCode status, string content) = await probe.GetBooksAsync();
            Assert.AreEqual(HttpStatusCode.OK, status);
            using JsonDocument document = JsonDocument.Parse(content);
            AssertRows(document.RootElement.GetProperty("value"));
        }

        private static void AssertRows(JsonElement rows)
        {
            Assert.AreEqual(1, rows.GetArrayLength());
            Assert.AreEqual(1, rows[0].GetProperty("id").GetInt32());
            Assert.AreEqual(PRIVATE_TITLE, rows[0].GetProperty("title").GetString(), "A real read must return this owned database's row.");
        }

        private static void AssertNoCommand(WorkflowFixture fixture, CliTelemetrySession cli)
            => Assert.IsFalse(fixture.Events.Cli(cli.SessionId).Any(record => record.Name == COMMAND));

        private static CliTelemetryEvent AssertCommand(WorkflowFixture fixture, CliTelemetrySession cli,
            string command, string outcome, string failureCategory)
        {
            CliTelemetryEvent completed = fixture.Events.Cli(cli.SessionId).Single(record => record.Name == COMMAND);
            Assert.AreEqual(command, completed.Properties["command"]);
            Assert.AreEqual(outcome, completed.Properties["outcome"]);
            Assert.AreEqual(failureCategory, completed.Properties["failure_category"]);
            Assert.AreEqual("none", completed.Properties["control"]);
            Assert.IsFalse(completed.Properties.ContainsKey("dab_config_epoch"));
            Assert.IsTrue(long.Parse(completed.Properties["duration_ms"], CultureInfo.InvariantCulture) >= 0);
            return completed;
        }

        private static void AssertSuccessfulLifetime(WorkflowFixture fixture, CliTelemetrySession cli,
            EngineInvocation engine, string api, string transport)
        {
            EngineTelemetryEvent[] records = fixture.Events.Engine(engine.Context!.EngineSessionId);
            EngineTelemetryEvent[] milestones = new[] { PROCESS_STARTED, READY, FIRST_SERVED, FIRST_SUCCESS, STOPPED }
                .Select(name => records.Single(record => record.Name == name)).ToArray();
            Assert.IsTrue(milestones.Zip(milestones.Skip(1)).All(pair => pair.First.Sequence < pair.Second.Sequence));
            Assert.AreEqual(0L, milestones[0].ConfigurationEpoch);
            Assert.IsTrue(records.Skip(1).All(record => record.ConfigurationEpoch == 1));
            Assert.IsFalse(records.Any(record => record.Name == STARTUP_FAILED));
            Assert.IsTrue(milestones.Skip(2).Take(2).All(record => record.Properties["api"] == api
                && record.Properties["transport"] == transport && record.Properties["outcome"] == "success"));
            EngineTelemetryEvent[] requests = Summaries(records, "request");
            Assert.AreEqual(1L, Count(requests, "count"), "Initialize/list/introspection and bootstrap metadata are not data requests.");
            Assert.AreEqual(1L, Count(requests, "success"));
            Assert.AreEqual(0L, Count(requests, "failure"));
            Assert.AreEqual(1L, Count(requests, "timed_count"));
            Assert.IsTrue(requests.All(record => record.Properties["api"] == api && record.Properties["transport"] == transport));
            Assert.AreEqual(1L, Count(Summaries(records, "operation"), "success"));
            Assert.IsTrue(Count(Summaries(records, "database_attempt"), "success") > 0,
                "The actual request must execute SQL; no test synthesizes telemetry observations.");
            Assert.AreEqual(transport == "http" ? 1L : 0L, Count(Summaries(records, "http_outcome"), "count"));
            Assert.AreEqual(0, engine.IdentityResolutions);
            Assert.AreEqual(1, engine.LaunchCalls);
            AssertLinkage(fixture, cli, engine, fixture.Events.Cli(cli.SessionId).Single(record => record.Name == COMMAND));
            AssertPrivateValuesAbsent(fixture);
        }

        private static void AssertLinkage(WorkflowFixture fixture, CliTelemetrySession cli, EngineInvocation engine, CliTelemetryEvent completed)
        {
            ProductTelemetryLaunchContext context = engine.Context!;
            CliTelemetryEvent[] cliRecords = fixture.Events.Cli(cli.SessionId);
            CollectionAssert.AreEqual(new[] { LAUNCH, COMMAND }, cliRecords.Select(record => record.Name).ToArray());
            Assert.IsTrue(cliRecords[0].Sequence < completed.Sequence);
            string source = context.Source switch
            {
                CliTelemetryLaunchSource.StartWeb => "start_web",
                CliTelemetryLaunchSource.StartStdio => "start_stdio",
                _ => "export_graphql"
            };
            Assert.AreEqual(source, cliRecords[0].Properties["launch_source"]);
            foreach (EngineTelemetryEvent record in fixture.Events.Engine(context.EngineSessionId))
            {
                Assert.AreEqual("cli", record.Properties["launcher"]);
                Assert.AreEqual(source, record.Properties["launch_source"]);
                Assert.AreEqual(context.ParentCliSessionId.ToString("D"), record.Properties["dab_parent_cli_session_id"]);
                Assert.AreEqual(context.Source == CliTelemetryLaunchSource.StartStdio ? "mcp_stdio" : "web", record.Properties["execution_mode"]);
            }

            IProductTelemetryEvent[] events = fixture.Events.Records.ToArray();
            Assert.AreEqual(3, events.OfType<CliTelemetryEvent>().Count(record => record.Name == COMMAND), "One init, one add, one top-level start/export.");
            Assert.IsTrue(events.All(record => record.IsSynthetic && record.Properties["dab_api_id"] == fixture.ApiId.ToString("D")
                && record.Properties["dab_installation_id"] == fixture.InstallationId.ToString("D")));
            Assert.AreEqual(events.Length, events.Select(record => record.EventId).Distinct().Count());
            CollectionAssert.AreEqual(fixture.SidecarBytes, File.ReadAllBytes(fixture.ConfigPath + ".dab-telemetry.json"),
                "Add, engine bootstrap and first success must reuse, not replace, the real sidecar.");
        }

        private static void AssertPrivateValuesAbsent(WorkflowFixture fixture)
        {
            string exported = string.Join("\n", fixture.Events.Records.SelectMany(record =>
                record.Properties.Keys.Concat(record.Properties.Values).Prepend(record.Name)));
            foreach (string privateValue in new[] { fixture.ConfigPath, fixture.DirectoryPath, fixture.DatabaseName,
                fixture.ConnectionString, @"(localdb)\MSSQLLocalDB", "Books", "dbo.MissingBooks", PRIVATE_TITLE })
            {
                Assert.IsFalse(exported.Contains(privateValue, StringComparison.OrdinalIgnoreCase), "Telemetry contains a synthetic customer value.");
            }
        }

        private static EngineTelemetryEvent[] Summaries(IEnumerable<EngineTelemetryEvent> records, string family)
            => records.Where(record => record.Name == "dab.engine.usage_summary" && record.Properties["family"] == family).ToArray();

        private static long Count(IEnumerable<EngineTelemetryEvent> records, string key)
            => records.Sum(record => long.Parse(record.Properties[key], CultureInfo.InvariantCulture));

        private static string Rpc(int id, string method, object? parameters = null)
            => JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });

        private static FileSystemRuntimeConfigLoader CreateLoader(IFileSystem fileSystem)
            => new(fileSystem, isCliLoader: true, logger: NullLogger<FileSystemRuntimeConfigLoader>.Instance);

        private sealed class WorkflowFixture : IAsyncDisposable
        {
            private const string DATABASE_PREFIX = "dab_cli_telemetry_";
            private bool _databaseCreated;
            private int _activeHosts;
            public string DatabaseName { get; } = DATABASE_PREFIX + Guid.NewGuid().ToString("N");
            public string DirectoryPath { get; }
            public string ConfigPath { get; }
            public string ConnectionString => ConnectionFor(DatabaseName);
            public FileSystem FileSystem { get; } = new();
            public RecordingExporter Events { get; } = new();
            public Guid InstallationId { get; } = Guid.NewGuid();
            public Guid ApiId { get; private set; }
            public byte[] SidecarBytes { get; private set; } = [];

            private WorkflowFixture()
            {
                DirectoryPath = IOPath.Combine(IOPath.GetTempPath(), DatabaseName);
                ConfigPath = IOPath.Combine(DirectoryPath, "dab-config.json");
            }

            public static async Task<WorkflowFixture> CreateAsync(bool createDatabase = true)
            {
                // Even the no-database scheduling case uses this fixture's real Windows
                // sidecar persistence. It does not require an installed/running SQL instance.
                if (!OperatingSystem.IsWindows())
                {
                    Assert.Inconclusive("CliTelemetryLocalDb: platform_unavailable");
                }

                WorkflowFixture fixture = new();
                try
                {
                    Directory.CreateDirectory(fixture.DirectoryPath);
                    if (createDatabase)
                    {
                        await using (SqlConnection master = new(ConnectionFor("master")))
                        {
                            // Only inability to open this fixed LocalDB instance is a prerequisite
                            // skip. DDL, metadata, protocol, requests and assertions must FAIL.
                            try
                            {
                                await master.OpenAsync();
                            }
                            catch (SqlException)
                            {
                                Assert.Inconclusive("CliTelemetryLocalDb: localdb_unavailable");
                            }
                            catch (PlatformNotSupportedException)
                            {
                                Assert.Inconclusive("CliTelemetryLocalDb: localdb_unavailable");
                            }

                            using SqlCommand create = master.CreateCommand();
                            create.CommandText = $"CREATE DATABASE [{fixture.DatabaseName}];";
                            await create.ExecuteNonQueryAsync();
                            fixture._databaseCreated = true;
                        }

                        await using SqlConnection database = new(fixture.ConnectionString);
                        await database.OpenAsync();
                        using SqlCommand seed = database.CreateCommand();
                        seed.CommandText = """
                            CREATE TABLE dbo.Books (id int NOT NULL PRIMARY KEY, title nvarchar(200) NOT NULL);
                            INSERT INTO dbo.Books (id, title) VALUES (1, @title);
                            """;
                        seed.Parameters.Add("@title", SqlDbType.NVarChar, 200).Value = PRIVATE_TITLE;
                        await seed.ExecuteNonQueryAsync();
                    }

                    return fixture;
                }
                catch
                {
                    await fixture.DisposeAsync();
                    throw;
                }
            }

            public CliTelemetrySession CreateCli(TimeProvider? clock = null)
            {
                // Use the DEFAULT real API-sidecar lookup/create paths. Only installation
                // identity is injected, so no test can write to the current user's profile.
                CliTelemetrySession session = CliTelemetrySession.Create(() => Events, enableSyntheticCollection: true,
                    clock: clock, readEnvironmentVariable: _ => null, showNotice: () => { },
                    resolveInstallation: () => new(InstallationId, "reused"));
                Assert.IsTrue(session.IsEnabled);
                return session;
            }

            public int Execute(string[] args, CliTelemetrySession? session = null,
                Func<string[], ProductTelemetryLaunchContext?, Action?, bool>? launcher = null,
                Func<Exporter>? exporterFactory = null, CancellationTokenSource? exportCancellation = null)
            {
                using FileSystemRuntimeConfigLoader loader = CreateLoader(FileSystem);
                return Program.Execute(args, NullLogger.Instance, FileSystem, loader, session,
                    launcher, exporterFactory, exportCancellation);
            }

            public async Task InitializeConfigurationAsync(string source = "dbo.Books")
            {
                using (CliTelemetrySession init = CreateCli())
                {
                    Assert.AreEqual(CliReturnCode.SUCCESS, Execute(
                        ["init", "--database-type", "mssql", "--connection-string", ConnectionString, "--config", ConfigPath,
                            "--host-mode", "development", "--auth.provider", "Simulator", "--rest.enabled", "true",
                            "--graphql.enabled", "true", "--mcp.enabled", "true"], init));
                    await init.StopAsync().WaitAsync(_timeout);
                    SidecarBytes = File.ReadAllBytes(ConfigPath + ".dab-telemetry.json");
                    using JsonDocument sidecar = JsonDocument.Parse(SidecarBytes);
                    Assert.AreEqual(1, sidecar.RootElement.GetProperty("version").GetInt32());
                    ApiId = sidecar.RootElement.GetProperty("apiId").GetGuid();
                    Assert.AreNotEqual(Guid.Empty, ApiId);
                    CliTelemetryEvent created = AssertCommand(this, init, "init", "success", "none");
                    Assert.AreEqual(ApiId.ToString("D"), created.Properties["dab_api_id"]);
                    Assert.AreEqual("newly_saved", created.Properties["dab_api_id_stability"]);
                }

                // Neutralize init's OTEL @env defaults through real CLI handlers before any
                // preflight performs replacement. These fixture-only calls have no session.
                Assert.AreEqual(CliReturnCode.SUCCESS, Execute(
                    ["add-telemetry", "--config", ConfigPath, "--otel-enabled", "false", "--app-insights-enabled", "false"]));
                Assert.AreEqual(CliReturnCode.SUCCESS, Execute(
                    ["configure", "--config", ConfigPath, "--runtime.health.enabled", "false", "--runtime.cache.enabled", "false",
                        "--runtime.telemetry.azure-log-analytics.enabled", "false", "--runtime.telemetry.file.enabled", "false"]));

                using CliTelemetrySession add = CreateCli();
                Assert.AreEqual(CliReturnCode.SUCCESS, Execute(
                    ["add", "Books", "--source", source, "--source.type", "table", "--permissions", "anonymous:read",
                        "--graphql", "Book:Books", "--rest", "Books", "--mcp.dml-tools", "true", "--cache.enabled", "false",
                        "--health.enabled", "false", "--config", ConfigPath], add));
                await add.StopAsync().WaitAsync(_timeout);
                CliTelemetryEvent added = AssertCommand(this, add, "add", "success", "none");
                Assert.AreEqual(ApiId.ToString("D"), added.Properties["dab_api_id"]);
                Assert.AreEqual("reused", added.Properties["dab_api_id_stability"]);
                CollectionAssert.AreEqual(SidecarBytes, File.ReadAllBytes(ConfigPath + ".dab-telemetry.json"));
            }

            public void HostEntered() => Interlocked.Increment(ref _activeHosts);
            public void HostExited() => Interlocked.Decrement(ref _activeHosts);

            public async ValueTask DisposeAsync()
            {
                Assert.AreEqual(0, Volatile.Read(ref _activeHosts),
                    "Retaining the owned fixture: never dispose files or force-disconnect a database underneath a live helper.");
                if (_databaseCreated)
                {
                    if (!DatabaseName.StartsWith(DATABASE_PREFIX, StringComparison.Ordinal)
                        || !Guid.TryParseExact(DatabaseName[DATABASE_PREFIX.Length..], "N", out _))
                    {
                        throw new InvalidOperationException("Refusing to drop an unowned database.");
                    }

                    await using SqlConnection master = new(ConnectionFor("master"));
                    await master.OpenAsync();
                    using SqlCommand drop = master.CreateCommand();
                    drop.CommandText = $"""
                        IF DB_ID(@name) IS NOT NULL
                        BEGIN
                            ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
                            DROP DATABASE [{DatabaseName}];
                        END;
                        """;
                    drop.Parameters.Add("@name", SqlDbType.NVarChar, 128).Value = DatabaseName;
                    await drop.ExecuteNonQueryAsync();
                    _databaseCreated = false;
                }

                if (Directory.Exists(DirectoryPath))
                {
                    Directory.Delete(DirectoryPath, recursive: true);
                }
            }

            private static string ConnectionFor(string database) => new SqlConnectionStringBuilder
            {
                DataSource = @"(localdb)\MSSQLLocalDB",
                InitialCatalog = database,
                IntegratedSecurity = true,
                Encrypt = SqlConnectionEncryptOption.Optional,
                TrustServerCertificate = true,
                Pooling = false,
                ConnectTimeout = 15,
                ConnectRetryCount = 0
            }.ConnectionString;
        }

        private sealed class EngineInvocation : IAsyncDisposable
        {
            private readonly WorkflowFixture _fixture;
            private readonly ChannelTextReader? _input;
            private Task<int>? _command;
            private CancellationTokenSource? _exportCancellation;
            private int _launchCalls;
            private int _factoryCalls;
            private int _identityResolutions;
            public HostProbe Probe { get; }
            public McpStdoutWriter? Stdout { get; }
            public ProductTelemetryLaunchContext? Context { get; private set; }
            public ProductTelemetryLaunchContext? FactoryContext { get; private set; }
            public string? FactoryConfigPath { get; private set; }
            public bool FactoryStdio { get; private set; }
            public EngineTelemetrySession? Session { get; private set; }
            public TaskCompletionSource Created { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource<bool> Returned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int LaunchCalls => Volatile.Read(ref _launchCalls);
            public int FactoryCalls => Volatile.Read(ref _factoryCalls);
            public int IdentityResolutions => Volatile.Read(ref _identityResolutions);

            public EngineInvocation(WorkflowFixture fixture, bool pauseHttpStartup = false,
                ChannelTextReader? input = null, ProtocolWriter? output = null)
            {
                _fixture = fixture;
                _input = input;
                Probe = new(pauseHttpStartup);
                Stdout = output is null ? null : new McpStdoutWriter(output);
            }

            public Task<int> ExecuteAsync(CliTelemetrySession cli, string[] args,
                Func<Exporter>? exporterFactory = null, CancellationTokenSource? exportCancellation = null)
            {
                _exportCancellation = exportCancellation;
                return _command = Task.Run(() => _fixture.Execute(args, cli, Launch, exporterFactory, exportCancellation));
            }

            private bool Launch(string[] args, ProductTelemetryLaunchContext? context, Action? startupFailureObserved)
            {
                Interlocked.Increment(ref _launchCalls);
                _fixture.HostEntered();
                Context = context;
                bool result = false;
                try
                {
                    // This is the real service overload: it creates the session at the normal
                    // boundary and calls StartEngineCore. Do not build/start a replacement host.
                    result = EngineProgram.StartEngine(args, context, CreateSession, ConfigureHost, startupFailureObserved);
                    return result;
                }
                finally
                {
                    _fixture.HostExited();
                    Returned.TrySetResult(result);
                }
            }

            private EngineTelemetrySession CreateSession(bool stdio, ProductTelemetryLaunchContext? context, string? configPath)
            {
                Interlocked.Increment(ref _factoryCalls);
                FactoryContext = context;
                FactoryConfigPath = configPath;
                FactoryStdio = stdio;
                Session = EngineTelemetrySession.Create(() => _fixture.Events, enableSyntheticCollection: true,
                    configPath: configPath, executionMode: stdio ? "mcp_stdio" : "web", readEnvironmentVariable: _ => null,
                    showNotice: () => { }, startTimer: false, launchContext: context,
                    resolveIdentity: _ =>
                    {
                        // An unintended second resolution must be visible even if persistence
                        // would happen to return the same saved UUID. Never touch other paths.
                        Interlocked.Increment(ref _identityResolutions);
                        return new(Guid.NewGuid(), "ephemeral");
                    });
                Created.TrySetResult();
                return Session;
            }

            private IHostBuilder ConfigureHost(IHostBuilder builder)
                => builder.UseEnvironment(Environments.Development)
                    .UseContentRoot(_fixture.DirectoryPath)
                    .ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                        new Dictionary<string, string?> { ["CONNSTRING"] = _fixture.ConnectionString }))
                    .ConfigureServices(services => services.Configure<Microsoft.Extensions.Hosting.HostOptions>(options => options.ShutdownTimeout = _timeout))
                    .ConfigureLogging(logging => logging.ClearProviders())
                    .ConfigureWebHost(web => web
                        .UseSetting(WebHostDefaults.ApplicationKey, typeof(EngineProgram).Assembly.GetName().Name)
                        .UseTestServer()
                        .ConfigureTestServices(services =>
                        {
                            services.AddSingleton<IStartupFilter>(Probe);
                            if (_input is not null)
                            {
                                services.RemoveAll<McpStdoutWriter>();
                                services.AddSingleton(Stdout!);
                                services.RemoveAll<IMcpLogNotificationWriter>();
                                services.AddSingleton<IMcpLogNotificationWriter>(new McpLogNotificationWriter(Stdout!));
                                services.RemoveAll<IMcpStdioServer>();
                                services.AddSingleton<IMcpStdioServer>(provider =>
                                {
                                    // The real helper resolves this AFTER metadata and BEFORE
                                    // MarkHostReady. HTTP startup filters never run in stdio.
                                    Probe.Capture(provider);
                                    return new McpStdioServer(provider.GetRequiredService<McpToolRegistry>(), provider, _input);
                                });
                            }
                        }));

            public async ValueTask DisposeAsync()
            {
                // Bound failed-test retries without changing or resetting Exporter's static
                // source. This source, when present, belongs only to this test invocation.
                _exportCancellation?.Cancel();
                Probe.ReleaseStartup();
                _input?.Complete();
                Probe.RequestStop(stopApplication: !Returned.Task.IsCompleted);

                try
                {
                    if (_command is not null)
                    {
                        await _command.WaitAsync(_startupTimeout);
                    }

                    if (LaunchCalls != 0)
                    {
                        await Returned.Task.WaitAsync(_timeout);
                    }
                }
                finally
                {
                    if (LaunchCalls == 0 || Returned.Task.IsCompleted)
                    {
                        Probe.Dispose();
                        Stdout?.Dispose();
                    }
                }
            }
        }

        /// <summary>Observation/lifetime control only; never calls a telemetry collection method.</summary>
        private sealed class HostProbe : IStartupFilter, IDisposable
        {
            private const string COMPLETION_HEADER = "X-Cli-Lifetime-Probe";
            private readonly ConcurrentDictionary<string, TaskCompletionSource> _completedRequests = new();
            private readonly TaskCompletionSource _releaseStartup = new(TaskCreationOptions.RunContinuationsAsynchronously);
            private readonly CancellationTokenSource _waitCancellation = new();
            private CancellationTokenRegistration _startedRegistration;
            private int _stopRequested;
            public TaskCompletionSource<IServiceProvider> Captured { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public IHostApplicationLifetime? Lifetime { get; private set; }
            public bool HttpPipelineConfigured { get; private set; }
            public bool ReadyWhenCaptured { get; private set; }
            public CancellationToken Stopping => _waitCancellation.Token;

            public HostProbe(bool pauseStartup)
            {
                if (!pauseStartup)
                {
                    ReleaseStartup();
                }
            }

            public void Capture(IServiceProvider services)
            {
                ReadyWhenCaptured = services.GetRequiredService<EngineTelemetrySession>().IsReady;
                Lifetime = services.GetRequiredService<IHostApplicationLifetime>();
                _startedRegistration = Lifetime.ApplicationStarted.Register(() => Started.TrySetResult());
                if (Volatile.Read(ref _stopRequested) != 0)
                {
                    Lifetime.StopApplication();
                }

                Captured.TrySetResult(services);
            }

            public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
            {
                HttpPipelineConfigured = true;
                Capture(app.ApplicationServices);
                _releaseStartup.Task.WaitAsync(_startupTimeout).GetAwaiter().GetResult();
                app.Use(async (HttpContext context, RequestDelegate nextRequest) =>
                {
                    if (_completedRequests.TryGetValue(context.Request.Headers[COMPLETION_HEADER].ToString(), out TaskCompletionSource? completed))
                    {
                        // Registered outside DAB: OnCompleted runs LIFO, so this is the
                        // barrier AFTER the product request's actual completion callback.
                        context.Response.OnCompleted(() =>
                        {
                            completed.TrySetResult();
                            return Task.CompletedTask;
                        });
                    }

                    await nextRequest(context);
                });
                next(app);
            };

            public void ReleaseStartup() => _releaseStartup.TrySetResult();

            public void RequestStop(bool stopApplication)
            {
                _waitCancellation.Cancel();
                if (stopApplication)
                {
                    Interlocked.Exchange(ref _stopRequested, 1);
                    Lifetime?.StopApplication();
                }
            }

            public async Task<(HttpStatusCode Status, string Content)> GetBooksAsync()
            {
                IServiceProvider services = await Captured.Task.WaitAsync(_startupTimeout);
                using HttpClient client = ((TestServer)services.GetRequiredService<IServer>()).CreateClient();
                client.BaseAddress = new Uri("https://localhost");
                string id = Guid.NewGuid().ToString("N");
                TaskCompletionSource completed = new(TaskCreationOptions.RunContinuationsAsynchronously);
                Assert.IsTrue(_completedRequests.TryAdd(id, completed));
                try
                {
                    using HttpRequestMessage request = new(HttpMethod.Get, "/api/Books");
                    request.Headers.Add(COMPLETION_HEADER, id);
                    request.Headers.Add("X-MS-API-ROLE", "anonymous");
                    using CancellationTokenSource deadline = new(_timeout);
                    using HttpResponseMessage response = await client.SendAsync(request, deadline.Token);
                    string content = await response.Content.ReadAsStringAsync(deadline.Token);
                    await completed.Task.WaitAsync(deadline.Token);
                    return (response.StatusCode, content);
                }
                finally
                {
                    _completedRequests.TryRemove(id, out _);
                }
            }

            public void Dispose()
            {
                _startedRegistration.Dispose();
                _waitCancellation.Dispose();
            }
        }

        private sealed class TestServerExporter(HostProbe probe) : Exporter
        {
            public ConcurrentQueue<bool> FallbackAttempts { get; } = new();

            internal override string GetGraphQLSchema(RuntimeConfig runtimeConfig, bool useFallbackUrl = false)
            {
                FallbackAttempts.Enqueue(useFallbackUrl);
                IServiceProvider services = probe.Captured.Task.WaitAsync(_startupTimeout, probe.Stopping).GetAwaiter().GetResult();
                probe.Started.Task.WaitAsync(_startupTimeout, probe.Stopping).GetAwaiter().GetResult();
                using HttpClient client = ((TestServer)services.GetRequiredService<IServer>()).CreateClient();
                client.BaseAddress = new Uri("https://localhost" + runtimeConfig.GraphQLPath);
                client.Timeout = _timeout;
                client.DefaultRequestHeaders.Add("X-MS-API-ROLE", "anonymous");
                // Only replace the loopback transport. Keep real introspection, the metadata-
                // backed schema, Exporter fallback/retries, helper launch and schema file write.
                return IntrospectionClient.IntrospectServerAsync(client).WaitAsync(_timeout, probe.Stopping).GetAwaiter().GetResult().ToString();
            }
        }

        private sealed class CallbackExporter(Func<string> getSchema) : Exporter
        {
            internal override string GetGraphQLSchema(RuntimeConfig runtimeConfig, bool useFallbackUrl = false) => getSchema();
        }

        private sealed class ChannelTextReader : TextReader
        {
            private readonly Channel<string> _lines = Channel.CreateUnbounded<string>(new() { SingleReader = true, SingleWriter = true });
            public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public void Send(string line) => Assert.IsTrue(_lines.Writer.TryWrite(line));
            public void Complete() => _lines.Writer.TryComplete();

            public override async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
            {
                Reading.TrySetResult();
                try
                {
                    return await _lines.Reader.ReadAsync(cancellationToken);
                }
                catch (ChannelClosedException)
                {
                    return null;
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Complete();
                }

                base.Dispose(disposing);
            }
        }

        private sealed class ProtocolWriter : TextWriter
        {
            private readonly ConcurrentQueue<string> _lines = new();
            private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonElement>> _responses = new();
            public override Encoding Encoding => Encoding.UTF8;

            public override void WriteLine(string? value)
            {
                _lines.Enqueue(value ?? string.Empty);
                using JsonDocument document = JsonDocument.Parse(value ?? string.Empty);
                if (document.RootElement.TryGetProperty("id", out JsonElement id) && id.TryGetInt32(out int number))
                {
                    Signal(number).TrySetResult(document.RootElement.Clone());
                }
            }

            public Task<JsonElement> ResponseAsync(int id) => Signal(id).Task.WaitAsync(_timeout);

            public JsonElement[] ReadAll() => _lines.Select(line =>
            {
                using JsonDocument document = JsonDocument.Parse(line);
                return document.RootElement.Clone();
            }).ToArray();

            private TaskCompletionSource<JsonElement> Signal(int id)
                => _responses.GetOrAdd(id, _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        private sealed class RecordingExporter : IEngineTelemetryExporter, IProductTelemetryExporter<IProductTelemetryEvent>
        {
            private readonly ConcurrentDictionary<(Guid Session, string Name), TaskCompletionSource<IProductTelemetryEvent>> _signals = new();
            public ConcurrentQueue<IProductTelemetryEvent> Records { get; } = new();

            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
                => ExportAsync((IProductTelemetryEvent)record, cancellationToken);

            public ValueTask<bool> ExportAsync(IProductTelemetryEvent record, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Records.Enqueue(record);
                Signal(record.SessionId, record.Name).TrySetResult(record);
                return ValueTask.FromResult(true);
            }

            public async Task<T> WaitAsync<T>(Guid session, string name) where T : IProductTelemetryEvent
                => (T)await Signal(session, name).Task.WaitAsync(_startupTimeout);

            public CliTelemetryEvent[] Cli(Guid session) => Records.OfType<CliTelemetryEvent>().Where(record => record.SessionId == session).ToArray();
            public EngineTelemetryEvent[] Engine(Guid session) => Records.OfType<EngineTelemetryEvent>().Where(record => record.SessionId == session).ToArray();
            public void Dispose() { }

            private TaskCompletionSource<IProductTelemetryEvent> Signal(Guid session, string name)
                => _signals.GetOrAdd((session, name), _ => new(TaskCreationOptions.RunContinuationsAsynchronously));
        }

        private sealed class ManualClock : TimeProvider
        {
            private long _ticks;
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() => Interlocked.Read(ref _ticks);
            public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch.AddTicks(GetTimestamp());
            public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
        }

        /// <summary>Existing process-wide logging/console state is restored after every test.</summary>
        private sealed class HostSettingsScope : IDisposable
        {
            private readonly ILoggerFactory _loggerFactory = Utils.LoggerFactoryForCli;
            private readonly FieldInfo _utilsLogger = typeof(Utils).GetField("_logger", BindingFlags.Static | BindingFlags.NonPublic)!;
            private readonly FieldInfo _configLogger = typeof(ConfigGenerator).GetField("_logger", BindingFlags.Static | BindingFlags.NonPublic)!;
            private readonly object? _originalUtilsLogger;
            private readonly object? _originalConfigLogger;
            private readonly bool _mcpStdio = Utils.IsMcpStdioMode;
            private readonly bool _cliOverriding = Utils.IsCliOverriding;
            private readonly bool _configOverriding = Utils.IsConfigOverriding;
            private readonly LogLevel _cliLogLevel = Utils.CliLogLevel;
            private readonly LogLevel _configLogLevel = Utils.ConfigLogLevel;
            private readonly ApplicationInsightsOptions _appInsights = Startup.AppInsightsOptions;
            private readonly OpenTelemetryOptions _openTelemetry = Startup.OpenTelemetryOptions;
            private readonly AzureLogAnalyticsOptions _logAnalytics = Startup.AzureLogAnalyticsOptions;
            private readonly FileSinkOptions _fileSink = Startup.FileSinkOptions;
            private readonly DynamicLogLevelProvider _logLevelProvider = EngineProgram.LogLevelProvider;
            private readonly LogLevel _minimumLogLevel = Startup.MinimumLogLevel;
            private readonly bool _startupCliOverriding = Startup.IsCliOverriding;
            private readonly bool _httpsDisabled = EngineProgram.IsHttpsRedirectionDisabled;
            private readonly TextWriter _out = Console.Out;
            private readonly TextWriter _error = Console.Error;
            private readonly StringWriter _captureOut = new(CultureInfo.InvariantCulture);
            private readonly StringWriter _captureError = new(CultureInfo.InvariantCulture);
            private readonly string? _connectionString = Environment.GetEnvironmentVariable(RUNTIME_ENV_CONNECTION_STRING);
            private readonly string? _environment = Environment.GetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME);

            public HostSettingsScope()
            {
                _originalUtilsLogger = _utilsLogger.GetValue(null);
                _originalConfigLogger = _configLogger.GetValue(null);
                Utils.LoggerFactoryForCli = NullLoggerFactory.Instance;
                Utils.SetCliUtilsLogger(NullLogger<Utils>.Instance);
                ConfigGenerator.SetLoggerForCliConfigGenerator(NullLogger<ConfigGenerator>.Instance);
                Utils.IsMcpStdioMode = false;
                Utils.IsCliOverriding = false;
                Utils.IsConfigOverriding = false;
                Startup.AppInsightsOptions = new();
                Startup.OpenTelemetryOptions = new();
                Startup.AzureLogAnalyticsOptions = new();
                Startup.FileSinkOptions = new();
                EngineProgram.LogLevelProvider = new();
                Console.SetOut(TextWriter.Synchronized(_captureOut));
                Console.SetError(TextWriter.Synchronized(_captureError));
                // These are fixture-routing variables, never the product telemetry gates.
                Environment.SetEnvironmentVariable(RUNTIME_ENV_CONNECTION_STRING, null);
                Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, null);
            }

            public void Dispose()
            {
                ILoggerFactory current = Utils.LoggerFactoryForCli;
                Utils.LoggerFactoryForCli = _loggerFactory;
                _utilsLogger.SetValue(null, _originalUtilsLogger);
                _configLogger.SetValue(null, _originalConfigLogger);
                Utils.IsMcpStdioMode = _mcpStdio;
                Utils.IsCliOverriding = _cliOverriding;
                Utils.IsConfigOverriding = _configOverriding;
                Utils.CliLogLevel = _cliLogLevel;
                Utils.ConfigLogLevel = _configLogLevel;
                Startup.AppInsightsOptions = _appInsights;
                Startup.OpenTelemetryOptions = _openTelemetry;
                Startup.AzureLogAnalyticsOptions = _logAnalytics;
                Startup.FileSinkOptions = _fileSink;
                EngineProgram.LogLevelProvider = _logLevelProvider;
                Startup.MinimumLogLevel = _minimumLogLevel;
                Startup.IsCliOverriding = _startupCliOverriding;
                typeof(EngineProgram).GetProperty(nameof(EngineProgram.IsHttpsRedirectionDisabled))!.SetValue(null, _httpsDisabled);
                Environment.SetEnvironmentVariable(RUNTIME_ENV_CONNECTION_STRING, _connectionString);
                Environment.SetEnvironmentVariable(RUNTIME_ENVIRONMENT_VAR_NAME, _environment);
                Console.SetOut(_out);
                Console.SetError(_error);
                if (!ReferenceEquals(current, _loggerFactory) && !ReferenceEquals(current, NullLoggerFactory.Instance))
                {
                    current.Dispose();
                }

                _captureOut.Dispose();
                _captureError.Dispose();
            }
        }
    }
}
