// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Cli.Constants;
using Microsoft.Extensions.Logging.Abstractions;

namespace Cli.Tests.Telemetry
{
    /// <summary>
    /// Real CLI parsing and configuration boundaries with mock file I/O and injected, offline
    /// telemetry. Never calls Main, opens a database, uses a telemetry SDK, or reads a user identity.
    /// </summary>
    [TestClass]
    [TestCategory("CliTelemetry")]
    [DoNotParallelize]
    public class CliTelemetryProviderTests
    {
        private const string PRIVATE_VALUE = "PRIVATE_PROVIDER_mssql_postgresql_mysql_6f19";
        private const string ROOT = PRIVATE_VALUE + ".json";
        private ILoggerFactory _originalLoggerFactory = null!;
        private bool _originalConfigOverride;
        private LogLevel _originalConfigLogLevel;
        private TextWriter _originalOut = null!;
        private TextWriter _originalError = null!;
        private StringWriter _stdout = null!;
        private StringWriter _stderr = null!;

        [TestInitialize]
        public void Initialize()
        {
            _originalLoggerFactory = Utils.LoggerFactoryForCli;
            _originalConfigOverride = Utils.IsConfigOverriding;
            _originalConfigLogLevel = Utils.ConfigLogLevel;
            _originalOut = Console.Out;
            _originalError = Console.Error;
            _stdout = new StringWriter();
            _stderr = new StringWriter();
            Console.SetOut(_stdout);
            Console.SetError(_stderr);
            UseQuietLoggers();
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Dispose any start-helper logger while its captured writers are still alive.
            UseQuietLoggers();
            Console.SetOut(_originalOut);
            Console.SetError(_originalError);
            _stdout.Dispose();
            _stderr.Dispose();
            Utils.LoggerFactoryForCli = _originalLoggerFactory;
            Utils.SetCliUtilsLogger(_originalLoggerFactory.CreateLogger<Utils>());
            ConfigGenerator.SetLoggerForCliConfigGenerator(_originalLoggerFactory.CreateLogger<ConfigGenerator>());
            Utils.IsConfigOverriding = _originalConfigOverride;
            Utils.ConfigLogLevel = _originalConfigLogLevel;
        }

        [DataTestMethod]
        [DataRow("mssql")]
        [DataRow("dwsql")]
        [DataRow("postgresql")]
        [DataRow("mysql")]
        [DataRow("cosmosdb_nosql")]
        [DataRow("cosmosdb_postgresql")]
        public async Task SuccessfulInitRecordsTheSavedProviderButFirstRunRemainsUnknown(string provider)
        {
            List<string> args = ["init", "--database-type", provider, "--connection-string", PRIVATE_VALUE, "--config", ROOT];
            if (provider == "cosmosdb_nosql")
            {
                // The database is mandatory; omitting --graphql-schema uses the normal default
                // without reading a schema or creating a Cosmos client.
                args.AddRange(["--cosmosdb_nosql-database", PRIVATE_VALUE]);
            }

            RunResult result = await RunWithParityAsync(args.ToArray(), newlySavedInstallation: true);

            Assert.AreEqual(CliReturnCode.SUCCESS, result.ExitCode);
            Assert.AreEqual(1, result.Writes.Length);
            Assert.AreEqual(1, result.IdentityCreations);
            Assert.AreEqual(0, result.IdentityLookups);
            Assert.IsNotNull(result.Configuration);
            using JsonDocument saved = JsonDocument.Parse(result.Configuration);
            Assert.AreEqual(provider, saved.RootElement.GetProperty("data-source").GetProperty("database-type").GetString());
            if (provider == "cosmosdb_nosql")
            {
                Assert.AreEqual("schema.gql", saved.RootElement.GetProperty("data-source").GetProperty("options").GetProperty("schema").GetString());
                Assert.IsFalse(result.FileOperations.Any(operation => operation.EndsWith("schema.gql", StringComparison.Ordinal)),
                    "The omitted schema option must not cause a schema-file probe or read.");
            }

            CollectionAssert.AreEqual(new[] { "dab.cli.first_run", "dab.cli.command" }, result.Events.Select(item => item.Name).ToArray());
            Assert.AreEqual("unknown", result.Events[0].Properties["database_type"]);
            CliTelemetryEvent command = AssertCommand(result, "init", provider, "success");
            Dictionary<string, string> context = ConfigurationContext(command);
            Assert.AreEqual("saved", context["observation"]);
            Assert.AreEqual(provider, context["data_sources.types"]);
            Assert.AreEqual("0", context["scale.entity_count"]);
            Assert.AreEqual("true", result.Events[1].Properties["option_database_type"]);
            Assert.AreEqual(0, result.Reads.Count(path => path.EndsWith(ROOT, StringComparison.Ordinal)),
                "A successful init projects its saved model, not a second config read.");
        }

        [DataTestMethod]
        [DataRow("existing-file")]
        [DataRow("read-only")]
        [DataRow("missing-cosmos-database")]
        public async Task RejectedOrUnwrittenInitNeverInfersTheRequestedOrExistingProvider(string scenario)
        {
            bool existing = scenario == "existing-file";
            bool denied = scenario == "read-only";
            string? original = existing ? Configuration("mysql") : null;
            string provider = scenario == "missing-cosmos-database" ? "cosmosdb_nosql" : "postgresql";
            RunResult result = await RunWithParityAsync(
                ["init", "--database-type", provider, "--config", ROOT], original, failWrites: denied);

            Assert.AreEqual(CliReturnCode.GENERAL_ERROR, result.ExitCode);
            Assert.AreEqual(original, result.Configuration);
            Assert.AreEqual(denied ? 1 : 0, result.Writes.Length);
            Assert.AreEqual(0, result.IdentityCreations);
            Assert.AreEqual(existing ? 1 : 0, result.IdentityLookups);
            Assert.AreEqual(0, result.Reads.Count(path => path.EndsWith(ROOT, StringComparison.Ordinal)),
                "An existing init target is rejected, not normally loaded.");
            CliTelemetryEvent command = AssertCommand(result, "init", "unknown", denied ? "execution_failure" : "validation_failure");
            Assert.AreEqual(denied ? "storage" : "configuration", command.Properties["failure_category"]);
            Assert.AreEqual("unknown", ConfigurationContext(command)["observation"]);
        }

        [DataTestMethod]
        [DynamicData(nameof(LoadedCommandCases), DynamicDataSourceType.Method)]
        public async Task ConfigAwareCommandsReportTheNormallyLoadedProviderEvenWhenLaterValidationFails(
            string[] args, string provider, int expectedExitCode, string outcome, int writes)
        {
            RunResult result = await RunWithParityAsync([.. args, "--config", ROOT], Configuration(provider));

            Assert.AreEqual(expectedExitCode, result.ExitCode);
            Assert.IsNotNull(result.LoadedConfiguration, "The real load must succeed before testing the observation boundary.");
            Assert.AreEqual(1, result.Reads.Length);
            Assert.IsTrue(result.Reads[0].EndsWith(ROOT, StringComparison.Ordinal));
            Assert.AreEqual(writes, result.Writes.Length);
            Assert.AreEqual(1, result.IdentityLookups);
            Assert.AreEqual(0, result.IdentityCreations);
            Assert.AreEqual(0, result.EngineLaunches, "All validation/generation failures in this matrix precede a host or database.");
            Assert.AreEqual(1, result.Events.Length);
            CliTelemetryEvent command = AssertCommand(result, args[0], provider, outcome);
            Dictionary<string, string> context = ConfigurationContext(command);
            Assert.AreEqual(writes == 0 ? "loaded" : "saved", context["observation"]);
            Assert.AreEqual(provider, context["data_sources.types"]);
            Assert.AreEqual("disabled", context["runtime.rest.configured"]);
            Assert.AreEqual("enabled", context["runtime.graphql.configured"]);
            Assert.AreEqual("disabled", context["runtime.mcp.configured"]);
        }

        public static IEnumerable<object[]> LoadedCommandCases()
        {
            yield return new object[] { new[] { "add", PRIVATE_VALUE, "--source", PRIVATE_VALUE, "--permissions", "anonymous:read" }, "postgresql", 0, "success", 1 };
            yield return new object[] { new[] { "update", "Books", "--description", PRIVATE_VALUE }, "mysql", 0, "success", 1 };
            yield return new object[] { new[] { "configure", "--show-effective-permissions" }, "dwsql", 0, "success", 0 };
            yield return new object[] { new[] { "add-telemetry", "--app-insights-enabled", "true" }, "cosmosdb_postgresql", -1, "validation_failure", 0 };
            yield return new object[] { new[] { "auto-config", PRIVATE_VALUE, "--permissions", "anonymous:read" }, "mssql", 0, "success", 1 };
            yield return new object[] { new[] { "auto-config", PRIVATE_VALUE, "--template.mcp.dml-tools", PRIVATE_VALUE }, "dwsql", -1, "validation_failure", 0 };
            yield return new object[] { new[] { "auto-config-simulate" }, "postgresql", -1, "validation_failure", 0 };
            yield return new object[] { new[] { "appname" }, "cosmosdb_postgresql", 0, "success", 0 };
            yield return new object[] { new[] { "export", "--output", PRIVATE_VALUE }, "mysql", -1, "validation_failure", 0 };
            // Both real generation branches reject before constructing a Cosmos client.
            // Their legacy exit code is zero despite a terminal telemetry failure.
            yield return new object[] { new[] { "export", "--graphql", "--generate", "--sampling-count", "0", "--output", PRIVATE_VALUE }, "cosmosdb_nosql", 0, "execution_failure", 0 };
            yield return new object[] { new[] { "export", "--graphql", "--generate", "--output", PRIVATE_VALUE }, "mssql", 0, "execution_failure", 0 };
            yield return new object[] { new[] { "start", "--log-level", "None" }, "mysql", -1, "validation_failure", 0 };
        }

        [DataTestMethod]
        [DataRow("saved")]
        [DataRow("read-only")]
        [DataRow("rejected-after-provider-change")]
        [DataRow("invalid-provider-value")]
        public async Task ConfigureChangesProviderOnlyAfterTheChangedModelIsSuccessfullyWritten(string scenario)
        {
            bool saved = scenario == "saved";
            bool denied = scenario == "read-only";
            string original = Configuration("mssql");
            List<string> args = ["configure", "--config", ROOT, "--data-source.database-type",
                scenario == "invalid-provider-value" ? PRIVATE_VALUE : "postgresql"];
            if (scenario == "rejected-after-provider-change")
            {
                // DataSource has already been cloned to PostgreSQL when this later check rejects it.
                args.AddRange(["--runtime.graphql.depth-limit", "0"]);
            }

            RunResult result = await RunWithParityAsync(args.ToArray(), original, failWrites: denied);

            Assert.AreEqual(saved ? CliReturnCode.SUCCESS : CliReturnCode.GENERAL_ERROR, result.ExitCode);
            Assert.AreEqual(1, result.Reads.Length);
            Assert.AreEqual(saved || denied ? 1 : 0, result.Writes.Length);
            Assert.IsNotNull(result.LoadedConfiguration);
            Assert.AreEqual(DatabaseType.MSSQL, result.LoadedConfiguration.ListAllDataSources().Single().DatabaseType,
                "Configure's record clone retains the old internal map; the successfully saved DataSource field must win.");
            CliTelemetryEvent command = AssertCommand(result, "configure", saved ? "postgresql" : "mssql",
                saved ? "success" : denied ? "execution_failure" : "validation_failure");
            Assert.AreEqual("true", command.Properties["option_data_source_database_type"]);
            Dictionary<string, string> context = ConfigurationContext(command);
            Assert.AreEqual(saved ? "saved" : "loaded", context["observation"]);
            Assert.AreEqual(saved ? "postgresql" : "mssql", context["data_sources.types"]);
            Assert.AreEqual(0, result.IdentityCreations);
            if (saved)
            {
                using JsonDocument document = JsonDocument.Parse(result.Configuration!);
                Assert.AreEqual("postgresql", document.RootElement.GetProperty("data-source").GetProperty("database-type").GetString());
            }
            else
            {
                Assert.AreEqual(original, result.Configuration);
            }
        }

        [TestMethod]
        public async Task DuplicateEntityFailuresKeepDistinctLoadedConfigurationBreakdownsWithoutApiIdentity()
        {
            string[] args = ["add", "Books", "--source", PRIVATE_VALUE, "--permissions", "anonymous:read", "--config", ROOT];
            string first = Configuration("mssql");
            string second = first.Replace("\"rest\": { \"enabled\": false }", "\"rest\": { \"enabled\": true }", StringComparison.Ordinal)
                .Replace("\"graphql\": { \"enabled\": true }", "\"graphql\": { \"enabled\": false }", StringComparison.Ordinal)
                .Replace("\"mcp\": { \"enabled\": false }", "\"mcp\": { \"enabled\": true }", StringComparison.Ordinal)
                .Replace("\"production\"", "\"development\"", StringComparison.Ordinal)
                .Replace("\"Unauthenticated\"", "\"Simulator\"", StringComparison.Ordinal)
                .Replace("\"runtime\": {", "\"runtime\": {\"cache\":{\"enabled\":true},", StringComparison.Ordinal);
            using (JsonDocument input = JsonDocument.Parse(second))
            {
                string entity = input.RootElement.GetProperty("entities").GetProperty("Books").GetRawText();
                second = second.Replace("\"Books\": " + entity,
                    string.Join(",", Enumerable.Range(0, 10).Select(index => JsonSerializer.Serialize(index == 0 ? "Books" : PRIVATE_VALUE + index) + ":" + entity)),
                    StringComparison.Ordinal);
            }

            RunResult firstResult = await RunWithParityAsync(args, first);
            RunResult secondResult = await RunWithParityAsync(args, second);
            CliTelemetryEvent firstCommand = AssertCommand(firstResult, "add", "mssql", "validation_failure");
            CliTelemetryEvent secondCommand = AssertCommand(secondResult, "add", "mssql", "validation_failure");
            Assert.AreEqual("configuration", firstCommand.Properties["failure_category"]);
            Assert.AreEqual("configuration", secondCommand.Properties["failure_category"]);
            Assert.IsFalse(firstCommand.Properties.ContainsKey("dab_api_id"));
            Assert.IsFalse(secondCommand.Properties.ContainsKey("dab_api_id"));
            Assert.AreEqual(0, firstResult.Writes.Length + secondResult.Writes.Length);
            Assert.AreEqual(1, firstResult.Reads.Length);
            Assert.AreEqual(1, secondResult.Reads.Length);
            using JsonDocument firstContext = JsonDocument.Parse(firstCommand.Properties["configuration_context"]);
            using JsonDocument secondContext = JsonDocument.Parse(secondCommand.Properties["configuration_context"]);
            Assert.AreEqual("loaded", firstContext.RootElement.GetProperty("observation").GetString());
            foreach ((string key, string firstValue, string secondValue) in new[]
            {
                ("runtime.rest.configured", "disabled", "enabled"),
                ("runtime.graphql.configured", "enabled", "disabled"),
                ("runtime.mcp.configured", "disabled", "enabled"),
                ("runtime.cache.configured", "missing", "enabled"),
                ("authentication.provider.configured", "unauthenticated", "simulator"),
                ("host.mode.configured", "production", "development"),
                ("scale.entity_count", "1", "2-10")
            })
            {
                Assert.AreEqual(firstValue, firstContext.RootElement.GetProperty(key).GetString(), key);
                Assert.AreEqual(secondValue, secondContext.RootElement.GetProperty(key).GetString(), key);
            }
        }

        [DataTestMethod]
        [DynamicData(nameof(UnloadedCommandCases), DynamicDataSourceType.Method)]
        public async Task HelpDecodeAndParseFailuresDoNotInferProvidersOrExportPrivateValues(
            string[] args, string configuration, int expectedExitCode, string outcome, int reads)
        {
            RunResult result = await RunWithParityAsync(args, configuration);

            Assert.AreEqual(expectedExitCode, result.ExitCode, $"Unexpected result for {args[0]}.");
            Assert.AreEqual(reads, result.Reads.Length);
            Assert.AreEqual(0, result.Writes.Length);
            Assert.AreEqual(0, result.IdentityCreations);
            Assert.AreEqual(0, result.EngineLaunches);
            Assert.IsNull(result.LoadedConfiguration);
            Assert.AreEqual(configuration, result.Configuration);
            Assert.AreEqual(1, result.Events.Length);
            CliTelemetryEvent command = AssertCommand(result, args[0], "unknown", outcome);
            Assert.AreEqual("unknown", ConfigurationContext(command)["observation"]);
        }

        public static IEnumerable<object[]> UnloadedCommandCases()
        {
            string known = Configuration("mysql");
            yield return new object[] { new[] { "init", "--database-type", PRIVATE_VALUE, "--config", ROOT }, known, -1, "parse_failure", 0 };
            yield return new object[] { new[] { "configure", "--data-source.database-type", "postgresql", "--runtime.rest.enabled", PRIVATE_VALUE, "--config", ROOT }, known, -1, "parse_failure", 0 };
            // The pinned parser recognizes help only immediately after the verb.
            yield return new object[] { new[] { "init", "--help", "--database-type", "mssql", "--config", ROOT }, known, 0, "success", 0 };
            yield return new object[] { new[] { "init", "--database-type", "mssql", "--help", "--config", ROOT }, known, -1, "parse_failure", 0 };
            yield return new object[] { new[] { "appname", "--decode=--database-type=cosmosdb_nosql;" + PRIVATE_VALUE, "--config", ROOT }, known, 0, "success", 0 };
            // validate returns at the parse failure. A loaded invalid REST path is NOT safe:
            // validate can still proceed to schema retrieval and database metadata initialization.
            yield return new object[] { new[] { "validate", "--config", ROOT }, "{\"data-source\":{\"database-type\":\"postgresql\"},", -1, "validation_failure", 1 };
            yield return new object[] { new[] { "appname", "--config", ROOT }, Configuration(PRIVATE_VALUE), -1, "validation_failure", 1 };
        }

        [DataTestMethod]
        [DataRow(false, "postgresql")]
        [DataRow(true, "mysql")]
        public async Task StartPassesTheActualPreflightProviderToTheLaunchEvent(bool stdio, string provider)
        {
            List<string> args = ["start", "--config", ROOT, "--log-level", "None"];
            if (stdio)
            {
                args.Add("--mcp-stdio");
            }

            RunResult result = await RunWithParityAsync(args.ToArray(), Configuration(provider, "Database=" + PRIVATE_VALUE));

            Assert.AreEqual(CliReturnCode.SUCCESS, result.ExitCode);
            Assert.AreEqual(1, result.EngineLaunches, "Only the injected in-memory launcher may run.");
            Assert.AreEqual(1, result.Reads.Length);
            Assert.AreEqual(2, result.Events.Length);
            CliTelemetryEvent launch = result.Events.Single(item => item.Name == "dab.cli.engine_launch");
            Assert.AreEqual(provider, launch.Properties["database_type"]);
            Assert.AreEqual(stdio ? "start_stdio" : "start_web", launch.Properties["launch_source"]);
            AssertCommand(result, "start", provider, "success");
        }

        [DataTestMethod]
        [DataRow("saved")]
        [DataRow("write-failed")]
        [DataRow("rejected")]
        public async Task ConfigurationDetailsChangeOnlyAfterTheWriteCommits(string scenario)
        {
            List<string> args = ["configure", "--config", ROOT, "--runtime.rest.enabled", "true",
                "--runtime.cache.enabled", "true", "--runtime.host.mode", "development"];
            if (scenario == "rejected")
            {
                args.AddRange(["--runtime.graphql.depth-limit", "0"]);
            }

            RunResult result = await RunWithParityAsync(args.ToArray(), Configuration("mssql"), failWrites: scenario == "write-failed");
            bool saved = scenario == "saved";
            CliTelemetryEvent command = AssertCommand(result, "configure", "mssql",
                saved ? "success" : scenario == "rejected" ? "validation_failure" : "execution_failure");
            Dictionary<string, string> context = ConfigurationContext(command);
            Assert.AreEqual(saved ? "saved" : "loaded", context["observation"]);
            Assert.AreEqual(saved ? "enabled" : "disabled", context["runtime.rest.configured"]);
            Assert.AreEqual(saved ? "enabled" : "missing", context["runtime.cache.configured"]);
            Assert.AreEqual(saved ? "development" : "production", context["host.mode.configured"]);
            Assert.AreEqual(1, result.Reads.Length);
            Assert.AreEqual(scenario == "rejected" ? 0 : 1, result.Writes.Length);
        }

        [DataTestMethod]
        [DataRow("", "missing")]
        [DataRow("\"runtime\":null,", "unknown")]
        [DataRow("\"runtime\":{},", "missing")]
        [DataRow("\"runtime\":{\"rest\":null,\"graphql\":null,\"cache\":null,\"mcp\":null},", "unknown")]
        public async Task LoadedPresenceSeparatesOmissionFromNullWithoutInventingDefaults(string runtime, string configured)
        {
            string json = "{" + runtime + "\"data-source\":{\"database-type\":\"mssql\"},\"entities\":{\"Books\":{\"source\":\""
                + PRIVATE_VALUE + "\",\"permissions\":[]}}}";
            RunResult result = await RunWithParityAsync(["appname", "--config", ROOT], json);
            Dictionary<string, string> context = ConfigurationContext(AssertCommand(result, "appname", "mssql", "success"));
            foreach (string key in new[] { "runtime.rest", "runtime.graphql", "runtime.mcp", "runtime.cache" })
            {
                Assert.AreEqual(configured, context[key + ".configured"], key);
            }

            Assert.AreEqual("enabled", context["runtime.rest.effective"]);
            Assert.AreEqual("disabled", context["runtime.cache.effective"]);
            Assert.AreEqual("missing", context["entities.any.rest.configured"]);
            Assert.AreEqual("enabled", context["entities.any.rest.effective"]);
            Assert.AreEqual("loaded", context["observation"]);
            Assert.AreEqual(1, result.Reads.Length);
        }

        [TestMethod]
        public async Task OversizedInputLosesOnlyProvenanceAndNeverAddsAReadOrChangesTheCommand()
        {
            string json = Configuration("mssql").Replace("\"source\":", "\"description\":\"" + new string('x', 1024 * 1024)
                + "\",\"source\":", StringComparison.Ordinal);
            RunResult result = await RunWithParityAsync(["appname", "--config", ROOT], json);
            Assert.IsNotNull(result.LoadedConfiguration);
            Assert.IsNull(result.LoadedConfiguration.TelemetryPresence);
            Dictionary<string, string> context = ConfigurationContext(AssertCommand(result, "appname", "mssql", "success"));
            Assert.AreEqual("loaded", context["observation"]);
            Assert.AreEqual("unknown", context["runtime.rest.configured"]);
            Assert.AreEqual("disabled", context["runtime.rest.effective"]);
            Assert.AreEqual("enabled", context["entities.any.descriptions"]);
            Assert.AreEqual(1, result.Reads.Length);
        }

        [TestMethod]
        public async Task CommandContextIncludesMcpRelationshipsAndPoliciesAsCategoriesNotValues()
        {
            string json = "{\"data-source\":{\"database-type\":\"mssql\",\"options\":{\"set-session-context\":true}},"
                + "\"runtime\":{\"cache\":{\"enabled\":true,\"level-2\":{\"enabled\":true}},"
                + "\"mcp\":{\"enabled\":true,\"dml-tools\":{\"enabled\":false,\"execute-entity\":true}},"
                + "\"host\":{\"authentication\":{\"provider\":\"SYNTHETIC_VALUE\"}}},"
                + "\"entities\":{\"Books\":{\"source\":{\"object\":\"SYNTHETIC_VALUE\",\"type\":\"stored-procedure\"},"
                + "\"mcp\":{\"dml-tools\":true,\"custom-tool\":true},\"cache\":{\"enabled\":true},"
                + "\"description\":\"SYNTHETIC_VALUE\","
                + "\"relationships\":{\"SYNTHETIC_VALUE\":{\"cardinality\":\"one\",\"target.entity\":\"Books\"}},"
                + "\"permissions\":[{\"role\":\"SYNTHETIC_VALUE\",\"actions\":[{\"action\":\"execute\","
                + "\"policy\":{\"request\":\"SYNTHETIC_VALUE\",\"database\":\"SYNTHETIC_VALUE\"}}]}]}}}";
            json = json.Replace("SYNTHETIC_VALUE", PRIVATE_VALUE, StringComparison.Ordinal);
            using JsonDocument validFixture = JsonDocument.Parse(json);
            RunResult result = await RunWithParityAsync(["add", "Books", "--source", PRIVATE_VALUE, "--permissions", "anonymous:read", "--config", ROOT], json);
            Dictionary<string, string> context = ConfigurationContext(AssertCommand(result, "add", "mssql", "validation_failure"));
            foreach (string key in new[] { "entities.any.mcp_dml.configured", "entities.any.mcp_dml.effective", "entities.any.mcp_custom_tool.configured",
                "entities.any.mcp_custom_tool.effective", "entities.any.relationships", "entities.any.custom_roles", "entities.any.policies", "entities.any.descriptions" })
            {
                Assert.AreEqual("enabled", context[key], key);
            }

            Assert.AreEqual("jwt", context["authentication.provider.configured"]);
            Assert.AreEqual("enabled", context["runtime.cache.configured"]);
            Assert.AreEqual("disabled", context["runtime.cache.effective"], "Session context gates effective caching.");
            Assert.AreEqual(1, result.Reads.Length);
            Assert.AreEqual(0, result.Writes.Length);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ReservedHelperUsesItsOwnReloadedProviderAfterCommandCompletion(bool stopOwner)
        {
            FileFixture fixture = new(Configuration("mysql"));
            RecordingExporter exporter = new();
            using CliTelemetrySession session = CliTelemetrySession.Create(() => exporter, enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveInstallation: () => new(Guid.NewGuid(), "reused"),
                lookupIdentity: _ => null, createIdentity: _ => new(Guid.NewGuid(), "ephemeral"));
            using CliTelemetryLaunchReservation? reservation = session.ReserveEngineLaunch(CliTelemetryLaunchSource.ExportGraphQL);
            Assert.IsNotNull(reservation);
            using FileSystemRuntimeConfigLoader loader = new(fixture.FileSystem, isCliLoader: true,
                logger: NullLogger<FileSystemRuntimeConfigLoader>.Instance);

            // Exercise the helper synchronously, not Exporter's background scheduling or HTTP
            // introspection. The parent completes through the real, read-only appname handler.
            Assert.AreEqual(CliReturnCode.SUCCESS, Program.Execute(["appname", "--config", ROOT],
                NullLogger.Instance, fixture.FileSystem, loader, session));
            if (stopOwner)
            {
                await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
                session.Dispose();
            }

            fixture.Files.File.WriteAllText(ROOT, Configuration("postgresql", "Database=" + PRIVATE_VALUE));
            int launches = 0;
            StartOptions helper = new(verbose: false, logLevel: LogLevel.None, isHttpsRedirectionDisabled: false,
                mcpStdio: false, mcpRole: null, logLevelLegacy: null, config: ROOT)
            {
                ProductTelemetry = session,
                ProductTelemetryLaunchSource = CliTelemetryLaunchSource.ExportGraphQL,
                ProductTelemetryLaunchReservation = reservation,
                EngineLauncher = (_, context, _) =>
                {
                    launches++;
                    Assert.IsNotNull(context);
                    Assert.AreEqual(CliTelemetryLaunchSource.ExportGraphQL, context.Source);
                    return true;
                }
            };

            Assert.IsTrue(ConfigGenerator.TryStartEngineWithOptions(helper, loader, fixture.FileSystem));
            CliTelemetryEvent launch = await exporter.Launch.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            CliTelemetryEvent command = exporter.Events.Single(item => item.Name == "dab.cli.command");
            Assert.AreEqual("mysql", command.Properties["database_type"]);
            Assert.AreEqual("mysql", ConfigurationContext(command)["data_sources.types"]);
            Assert.AreEqual("loaded", ConfigurationContext(command)["observation"]);
            Assert.AreEqual("postgresql", launch.Properties["database_type"]);
            Assert.IsFalse(launch.Properties.ContainsKey("configuration_context"));
            Assert.AreEqual("export_graphql", launch.Properties["launch_source"]);
            Assert.IsTrue(launch.Sequence > command.Sequence);
            Assert.AreEqual(1, launches);
            Assert.AreEqual(2, fixture.Reads.Count, "One normal parent load and one helper preflight load, with no telemetry rereads.");
            Assert.IsFalse(reservation.IsAvailable);
            Assert.IsFalse(JsonSerializer.Serialize(exporter.Events.ToArray()).Contains(PRIVATE_VALUE, StringComparison.OrdinalIgnoreCase));
        }

        [DataTestMethod]
        [DataRow(false, false, "mssql")]
        [DataRow(true, false, "multiple")]
        [DataRow(true, true, "unknown")]
        public async Task AppNameProjectsOnlyTheAlreadyLoadedRootAndChildren(bool mixedProviders, bool missingChild, string expected)
        {
            DataSource first = new(DatabaseType.MSSQL, string.Empty);
            DataSource second = new(mixedProviders ? DatabaseType.MySQL : DatabaseType.MSSQL, string.Empty);
            RuntimeEntities entities = new(new Dictionary<string, Entity>());
            string firstFile = PRIVATE_VALUE + "-first.json";
            string secondFile = PRIVATE_VALUE + "-second.json";
            // Use the explicit-map constructor: the JSON constructor uses real child loaders
            // and watchers. These are already-loaded child records, not files to discover.
            RuntimeConfig root = new(Schema: string.Empty, DataSource: null!, Runtime: new(null, null, null, null),
                Entities: entities, DefaultDataSourceName: "root",
                DataSourceNameToDataSource: new() { ["first"] = first, ["second"] = second },
                EntityNameToDataSourceName: new(), DataSourceFiles: new([firstFile, secondFile]));
            root.ChildConfigs.Add((firstFile, new RuntimeConfig(Schema: null, DataSource: first, Entities: entities) { IsChildConfig = true }));
            if (!missingChild)
            {
                root.ChildConfigs.Add((secondFile, new RuntimeConfig(Schema: null, DataSource: second, Entities: entities) { IsChildConfig = true }));
            }

            RunResult result = await RunWithParityAsync(["appname", "--config", ROOT], Configuration("cosmosdb_nosql"), loadedConfiguration: root);

            Assert.AreEqual(CliReturnCode.SUCCESS, result.ExitCode);
            Assert.AreSame(root, result.LoadedConfiguration);
            Assert.AreEqual(missingChild ? 1 : 2, root.ChildConfigs.Count);
            Assert.AreEqual(2, root.ListAllDataSources().Count());
            Assert.AreEqual(0, result.Reads.Length, "The normally used cached model, not conflicting disk contents or extra child reads, is authoritative.");
            Assert.AreEqual(0, result.Writes.Length);
            Assert.AreEqual(1, result.IdentityLookups, "Only the selected root is observed, never individual child paths.");
            AssertCommand(result, "appname", expected, "success");
        }

        [TestMethod]
        public async Task AddRejectsMissingDataSourceWithoutGuessingAProviderFromOtherValues()
        {
            RunResult result = await RunWithParityAsync(
                ["add", PRIVATE_VALUE, "--source", "mssql", "--permissions", "anonymous:read", "--config", ROOT],
                "{\"runtime\":{},\"entities\":{}}");

            Assert.AreEqual(CliReturnCode.GENERAL_ERROR, result.ExitCode);
            Assert.IsNotNull(result.LoadedConfiguration);
            Assert.IsNull(result.LoadedConfiguration.DataSource);
            Assert.AreEqual(1, result.Reads.Length);
            Assert.AreEqual(0, result.Writes.Length);
            AssertCommand(result, "add", "unknown", "validation_failure");
        }

        private static string Configuration(string provider, string connectionString = "") => $$"""
            {
              "data-source": {
                "database-type": "{{provider}}",
                "connection-string": {{JsonSerializer.Serialize(connectionString)}}
              },
              "runtime": {
                "rest": { "enabled": false },
                "graphql": { "enabled": true },
                "mcp": { "enabled": false },
                "host": { "mode": "production", "authentication": { "provider": "Unauthenticated" } }
              },
              "entities": {
                "Books": { "source": "{{PRIVATE_VALUE}}", "permissions": [{ "role": "anonymous", "actions": ["read"] }] }
              }
            }
            """;

        private async Task<RunResult> RunWithParityAsync(string[] args, string? configuration = null,
            bool failWrites = false, bool newlySavedInstallation = false, RuntimeConfig? loadedConfiguration = null)
        {
            RunResult baseline = await RunAsync(args, configuration, failWrites, newlySavedInstallation, loadedConfiguration, enabled: false);
            RunResult observed = await RunAsync(args, configuration, failWrites, newlySavedInstallation, loadedConfiguration, enabled: true);
            Assert.AreEqual(baseline.ExitCode, observed.ExitCode);
            Assert.AreEqual(baseline.Configuration, observed.Configuration);
            Assert.AreEqual(baseline.Stdout, observed.Stdout);
            Assert.AreEqual(baseline.Stderr, observed.Stderr);
            Assert.AreEqual(baseline.EngineLaunches, observed.EngineLaunches);
            CollectionAssert.AreEqual(baseline.FileOperations, observed.FileOperations,
                "Enabled telemetry must not add config probes, reads, or writes. Identity callbacks do no file I/O.");
            Assert.AreEqual(0, baseline.Events.Length);
            Assert.AreEqual(0, baseline.IdentityCreations + baseline.IdentityLookups);
            if (loadedConfiguration is null && baseline.LoadedConfiguration is not null)
            {
                Assert.IsNull(baseline.LoadedConfiguration.TelemetryPresence, "A public/disabled invocation cannot inherit command capture.");
            }

            return observed;
        }

        private async Task<RunResult> RunAsync(string[] args, string? configuration, bool failWrites,
            bool newlySavedInstallation, RuntimeConfig? loadedConfiguration, bool enabled)
        {
            UseQuietLoggers();
            _stdout.GetStringBuilder().Clear();
            _stderr.GetStringBuilder().Clear();
            FileFixture fixture = new(configuration, failWrites);
            RecordingExporter exporter = new();
            int identityCreations = 0;
            int identityLookups = 0;
            int engineLaunches = 0;
            using CliTelemetrySession session = CliTelemetrySession.Create(() => exporter, enableSyntheticCollection: enabled,
                readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveInstallation: () => new(Guid.NewGuid(), newlySavedInstallation ? "newly_saved" : "reused"),
                createIdentity: _ => { identityCreations++; return new(Guid.NewGuid(), "ephemeral"); },
                lookupIdentity: _ => { identityLookups++; return null; });
            Assert.AreEqual(enabled, session.IsEnabled);
            using FileSystemRuntimeConfigLoader loader = new(fixture.FileSystem, isCliLoader: true,
                logger: NullLogger<FileSystemRuntimeConfigLoader>.Instance);
            loader.RuntimeConfig = loadedConfiguration;
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();

            int exitCode = Program.Execute(args, NullLogger.Instance, fixture.FileSystem, loader, enabled ? session : null,
                engineLauncher: (_, context, _) =>
                {
                    engineLaunches++;
                    Assert.AreEqual(enabled, context is not null);
                    return true;
                },
                exporterFactory: () => throw new AssertFailedException("These cases must not attempt HTTP schema discovery."),
                exportCancellationTokenSource: cancellation);
            Assert.IsFalse(TelemetryConfigurationPresence.IsCaptureEnabled(), "The invocation must restore its scoped capture policy.");
            await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            CliTelemetryEvent[] events = exporter.Events.ToArray();
            Assert.IsFalse(JsonSerializer.Serialize(events).Contains(PRIVATE_VALUE, StringComparison.OrdinalIgnoreCase));
            foreach (CliTelemetryEvent item in events)
            {
                Assert.IsTrue(item.IsSynthetic);
                CollectionAssert.Contains(new[] { "mssql", "dwsql", "postgresql", "mysql", "cosmosdb_nosql", "cosmosdb_postgresql", "multiple", "unknown" },
                    item.Properties["database_type"], "Only the closed provider vocabulary may be emitted.");
                Assert.IsTrue(item.Properties.Where(pair => pair.Key.StartsWith("option_", StringComparison.Ordinal))
                    .All(pair => pair.Value == "true"), "Option presence must not export values, even when a value resembles a provider option.");
                Assert.AreEqual(item.Name == "dab.cli.command", item.Properties.ContainsKey("configuration_context"));
                if (item.Name == "dab.cli.command")
                {
                    _ = ConfigurationContext(item);
                }
            }

            return new(exitCode, events, fixture.Operations.ToArray(), fixture.Reads.ToArray(), fixture.Writes.ToArray(),
                fixture.Files.File.Exists(ROOT) ? fixture.Files.File.ReadAllText(ROOT) : null,
                loader.RuntimeConfig, identityCreations, identityLookups, engineLaunches, _stdout.ToString(), _stderr.ToString());
        }

        private void UseQuietLoggers()
        {
            ILoggerFactory current = Utils.LoggerFactoryForCli;
            Utils.LoggerFactoryForCli = NullLoggerFactory.Instance;
            Utils.SetCliUtilsLogger(NullLogger<Utils>.Instance);
            ConfigGenerator.SetLoggerForCliConfigGenerator(NullLogger<ConfigGenerator>.Instance);
            if (!ReferenceEquals(current, _originalLoggerFactory) && !ReferenceEquals(current, NullLoggerFactory.Instance))
            {
                current.Dispose();
            }
        }

        private static CliTelemetryEvent AssertCommand(RunResult result, string command, string provider, string outcome)
        {
            CliTelemetryEvent record = result.Events.Single(item => item.Name == "dab.cli.command");
            Assert.AreEqual(command, record.Properties["command"]);
            Assert.AreEqual(outcome, record.Properties["outcome"]);
            Assert.AreEqual(provider, record.Properties["database_type"]);
            return record;
        }

        private static Dictionary<string, string> ConfigurationContext(CliTelemetryEvent command)
        {
            string json = command.Properties["configuration_context"];
            Assert.IsTrue(json.Length <= 8192);
            Assert.IsFalse(json.Contains(PRIVATE_VALUE, StringComparison.Ordinal));
            Dictionary<string, string> properties = JsonSerializer.Deserialize<Dictionary<string, string>>(json)!;
            Assert.AreEqual(83, properties.Count);
            Assert.AreEqual("cli-configuration-v1", properties["snapshot_schema"]);
            Assert.IsTrue(command.Properties.Count <= 128);
            return properties;
        }

        private sealed record RunResult(int ExitCode, CliTelemetryEvent[] Events, string[] FileOperations, string[] Reads,
            string[] Writes, string? Configuration, RuntimeConfig? LoadedConfiguration, int IdentityCreations,
            int IdentityLookups, int EngineLaunches, string Stdout, string Stderr);

        private sealed class FileFixture
        {
            public MockFileSystem Files { get; } = new();
            public IFileSystem FileSystem { get; }
            public List<string> Operations { get; } = new();
            public List<string> Reads { get; } = new();
            public List<string> Writes { get; } = new();

            public FileFixture(string? configuration, bool failWrites = false)
            {
                string directory = Files.Path.GetFullPath("cli-provider-offline");
                Files.AddDirectory(directory);
                Files.Directory.SetCurrentDirectory(directory);
                string schema = Files.Path.Combine(Files.Path.GetDirectoryName(typeof(FileSystemRuntimeConfigLoader).Assembly.Location)!, SCHEMA);
                Files.AddFile(schema, new MockFileData("{ \"$id\": \"https://example.invalid/dab-schema.json\" }"));
                if (configuration is not null)
                {
                    Files.AddFile(ROOT, new MockFileData(configuration));
                }

                Mock<IFile> file = new(MockBehavior.Strict);
                file.Setup(item => item.Exists(It.IsAny<string>())).Returns((string path) =>
                {
                    Operations.Add("exists:" + Files.Path.GetFullPath(path));
                    return Files.File.Exists(path);
                });
                file.Setup(item => item.ReadAllText(It.IsAny<string>())).Returns((string path) =>
                {
                    string fullPath = Files.Path.GetFullPath(path);
                    Operations.Add("read:" + fullPath);
                    Reads.Add(fullPath);
                    return Files.File.ReadAllText(path);
                });
                file.Setup(item => item.WriteAllText(It.IsAny<string>(), It.IsAny<string>())).Callback((string path, string content) =>
                {
                    string fullPath = Files.Path.GetFullPath(path);
                    Operations.Add("write:" + fullPath);
                    Writes.Add(fullPath);
                    if (failWrites)
                    {
                        throw new UnauthorizedAccessException(PRIVATE_VALUE);
                    }

                    Files.File.WriteAllText(path, content);
                });
                Mock<IFileSystem> fileSystem = new(MockBehavior.Strict);
                fileSystem.SetupGet(item => item.File).Returns(file.Object);
                fileSystem.SetupGet(item => item.Path).Returns(Files.Path);
                fileSystem.SetupGet(item => item.Directory).Returns(Files.Directory);
                FileSystem = fileSystem.Object;
            }
        }

        private sealed class RecordingExporter : IProductTelemetryExporter<IProductTelemetryEvent>
        {
            public ConcurrentQueue<CliTelemetryEvent> Events { get; } = new();
            public TaskCompletionSource<CliTelemetryEvent> Launch { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask<bool> ExportAsync(IProductTelemetryEvent record, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                CliTelemetryEvent item = (CliTelemetryEvent)record;
                Events.Enqueue(item);
                if (item.Name == "dab.cli.engine_launch")
                {
                    Launch.TrySetResult(item);
                }

                return ValueTask.FromResult(true);
            }

            public void Dispose() { }
        }
    }
}
