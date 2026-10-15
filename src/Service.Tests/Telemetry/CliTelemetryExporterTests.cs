// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Actual Azure Monitor SDK serialization over fake HTTP, with isolated pools and unique
    /// synthetic routing. No test acquires from the process-wide product sender pool.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    [TestCategory("CliTelemetry")]
    [DoNotParallelize]
    public class CliTelemetryExporterTests
    {
        private static readonly TimeSpan _testTimeout = TimeSpan.FromSeconds(10);

        [TestInitialize]
        public void RequireOfflineSdkTestEnvironment()
        {
            Assert.IsTrue(EngineTelemetryApplicationInsightsExporter.AreSdkStatisticsDisabled(),
                "Use telemetry.runsettings so both SDK statistics opt-outs are set before testhost startup.");
        }

        [TestMethod]
        public async Task ThreeCliEventsUseActualSdkWithoutEpochOrAmbientContext()
        {
            const string sentinel = "PRIVATE_CLI_CONTEXT_2f77";
            string[] variables =
            [
                "APPLICATIONINSIGHTS_CONNECTION_STRING",
                "OTEL_RESOURCE_ATTRIBUTES", "OTEL_SERVICE_NAME"
            ];
            Dictionary<string, string?> original = variables.ToDictionary(name => name, Environment.GetEnvironmentVariable);
            try
            {
                foreach (string variable in variables)
                {
                    Environment.SetEnvironmentVariable(variable, variable == "OTEL_RESOURCE_ATTRIBUTES"
                        ? "host.name=" + sentinel + ",enduser.id=" + sentinel : sentinel);
                }

                using Activity activity = new Activity(sentinel).SetIdFormat(ActivityIdFormat.W3C).Start();
                activity.TraceStateString = "private=" + sentinel;
                activity.AddBaggage("secret", sentinel);
                bool previousSuppression = Sdk.SuppressInstrumentation;
                ApplicationInsightsTelemetryDestination destination = NewDestination();
                RecordingHandler handler = new();
                using ProductTelemetrySenderPool pool = new(selected => new EngineTelemetryApplicationInsightsExporter(selected, handler));
                using IProductTelemetryExporter<IProductTelemetryEvent> lease = pool.AcquireLease(destination);
                IProductTelemetryExporter<CliTelemetryEvent> cli = lease;
                ImmutableDictionary<string, string> properties = ImmutableDictionary<string, string>.Empty.Add("command", "init");
                foreach (string key in new[]
                {
                    "microsoft.client.ip", "microsoft.custom_event.name", "ai.location.ip",
                    "ai.cloud.role", "ai.cloud.roleInstance", "ai.user.id", "ai.operation.id",
                    "enduser.id", "user_agent.original", "{OriginalFormat}", "CategoryName", "EventId", "EventName",
                    "dab_event_id", "dab_process_session_id", "dab_sequence", "dab_occurred_at",
                    "dab_config_epoch", "dab_schema_version", "dab_is_synthetic"
                })
                {
                    properties = properties.Add(key, sentinel);
                }

                // Event names are opaque producer data to the shared adapter, not a new CLI schema.
                CliTelemetryEvent[] records =
                [
                    CreateCliEvent(1, "dab.cli.first_run") with { Properties = properties },
                    CreateCliEvent(2, "dab.cli.command") with { Properties = properties },
                    CreateCliEvent(3, "dab.cli.engine_launch") with { Properties = properties }
                ];
                Assert.AreEqual(0, handler.Requests.Count);
                foreach (CliTelemetryEvent record in records)
                {
                    Assert.IsTrue(await cli.ExportAsync(record, CancellationToken.None));
                }

                CapturedRequest[] requests = handler.Requests.ToArray();
                Assert.AreEqual(3, requests.Length);
                for (int index = 0; index < records.Length; index++)
                {
                    CliTelemetryEvent record = records[index];
                    CapturedRequest request = requests[index];
                    Assert.AreEqual(destination.TrackEndpoint, request.Uri);
                    Assert.AreEqual("application/json", request.Headers["Content-Type"]);
                    Assert.IsTrue(request.InstrumentationSuppressed);
                    Assert.IsFalse(request.HadActivity);
                    Assert.IsTrue(request.Token.CanBeCanceled);
                    Assert.IsFalse(request.Body.Contains(sentinel, StringComparison.Ordinal));
                    Assert.IsFalse(request.Body.Contains(activity.TraceId.ToHexString(), StringComparison.Ordinal));
                    Assert.IsTrue(EngineTelemetrySdkTransportHandler.HasOnlyApprovedEnvelopeTags(request.Body));
                    foreach (string header in new[] { "Authorization", "Cookie", "traceparent", "tracestate", "Request-Id", "baggage", "User-Agent" })
                    {
                        Assert.IsFalse(request.Headers.ContainsKey(header), header);
                    }

                    using JsonDocument body = JsonDocument.Parse(request.Body);
                    JsonElement envelope = body.RootElement;
                    Assert.AreEqual(destination.InstrumentationKey.ToString("D"), envelope.GetProperty("iKey").GetString());
                    Assert.AreEqual("EventData", envelope.GetProperty("data").GetProperty("baseType").GetString());
                    Assert.AreEqual(record.Name, envelope.GetProperty("data").GetProperty("baseData").GetProperty("name").GetString());
                    Assert.AreEqual(record.OccurredAt, envelope.GetProperty("time").GetDateTimeOffset());
                    JsonElement tags = envelope.GetProperty("tags");
                    Assert.AreEqual("0.0.0.0", tags.GetProperty("ai.location.ip").GetString());
                    foreach (string tag in new[] { "ai.cloud.role", "ai.cloud.roleInstance", "ai.application.ver", "ai.user.id", "ai.user.authUserId", "ai.operation.id", "ai.operation.parentId" })
                    {
                        Assert.IsTrue(!tags.TryGetProperty(tag, out JsonElement value) || value.ValueKind == JsonValueKind.Null, tag);
                    }

                    JsonElement actual = EventProperties(body);
                    Assert.AreEqual(7, actual.EnumerateObject().Count());
                    Assert.AreEqual(record.EventId.ToString("D"), actual.GetProperty("dab_event_id").GetString());
                    Assert.AreEqual(record.SessionId.ToString("D"), actual.GetProperty("dab_process_session_id").GetString());
                    Assert.AreEqual(record.Sequence.ToString(CultureInfo.InvariantCulture), actual.GetProperty("dab_sequence").GetString());
                    Assert.AreEqual(record.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), actual.GetProperty("dab_occurred_at").GetString());
                    Assert.AreEqual("1", actual.GetProperty("dab_schema_version").GetString());
                    Assert.AreEqual("true", actual.GetProperty("dab_is_synthetic").GetString());
                    Assert.AreEqual("init", actual.GetProperty("command").GetString());
                    Assert.IsFalse(actual.TryGetProperty("dab_config_epoch", out _));
                    Assert.AreEqual(sentinel, record.Properties["dab_config_epoch"], "Mapping must not mutate producer properties.");
                }

                Assert.AreSame(activity, Activity.Current);
                Assert.AreEqual(previousSuppression, Sdk.SuppressInstrumentation);
                foreach (string variable in variables)
                {
                    Assert.AreEqual(variable == "OTEL_RESOURCE_ATTRIBUTES"
                        ? "host.name=" + sentinel + ",enduser.id=" + sentinel : sentinel, Environment.GetEnvironmentVariable(variable));
                }
            }
            finally
            {
                foreach ((string variable, string? value) in original)
                {
                    Environment.SetEnvironmentVariable(variable, value);
                }
            }
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
        [DataRow(int.MaxValue, "unknown")]
        public async Task SessionProviderCategoriesSurviveActualSdkSerialization(int provider, string expected)
        {
            const string ROOT = "PRIVATE_PROVIDER_CONFIG_9b63.json";
            RecordingHandler handler = new();
            ApplicationInsightsTelemetryDestination destination = NewDestination();
            using ProductTelemetrySenderPool pool = new(selected => new EngineTelemetryApplicationInsightsExporter(selected, handler));
            using CliTelemetrySession session = CliTelemetrySession.Create(() => pool.AcquireLease(destination),
                enableSyntheticCollection: true, readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveInstallation: () => new(Guid.NewGuid(), "newly_saved"),
                lookupIdentity: _ => throw new AssertFailedException("Provider observation must not look up identity."),
                createIdentity: _ => new(Guid.NewGuid(), "ephemeral"));

            session.ObserveDatabaseType(ROOT, (CliTelemetryDatabaseType)provider);
            Assert.IsNotNull(session.BeginEngineLaunch(ROOT, CliTelemetryLaunchSource.StartWeb, (CliTelemetryDatabaseType)provider));
            session.Complete("init", "none", ImmutableDictionary<string, string>.Empty.Add("option_database_type", "true"),
                CliTelemetryOutcome.Success);
            await session.StopAsync().WaitAsync(_testTimeout);

            CapturedRequest[] requests = handler.Requests.ToArray();
            Assert.AreEqual(3, requests.Length, "Exercise the real session and worker, not manually constructed properties.");
            string[] names = ["dab.cli.first_run", "dab.cli.engine_launch", "dab.cli.command"];
            for (int index = 0; index < requests.Length; index++)
            {
                CapturedRequest request = requests[index];
                Assert.AreEqual(destination.TrackEndpoint, request.Uri);
                Assert.IsTrue(EngineTelemetrySdkTransportHandler.HasOnlyApprovedEnvelopeTags(request.Body));
                Assert.IsFalse(request.Body.Contains(ROOT, StringComparison.Ordinal));
                using JsonDocument body = JsonDocument.Parse(request.Body);
                Assert.AreEqual(names[index], body.RootElement.GetProperty("data").GetProperty("baseData").GetProperty("name").GetString());
                JsonElement properties = EventProperties(body);
                Assert.AreEqual(index == 0 ? "unknown" : expected, properties.GetProperty("database_type").GetString());
                Assert.AreEqual(session.SessionId.ToString("D"), properties.GetProperty("dab_process_session_id").GetString());
                Assert.AreEqual("true", properties.GetProperty("dab_is_synthetic").GetString());
                Assert.IsFalse(properties.TryGetProperty("dab_config_epoch", out _));
                if (index == 2)
                {
                    Assert.AreEqual("true", properties.GetProperty("option_database_type").GetString());
                }
            }
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ConfigurationContextAndAll96OptionsReachTheSdkTogetherWithoutValues(bool observed)
        {
            const string ROOT = "PRIVATE_FULL_CONTEXT_58e12";
            const string JSON = """
                { "data-source":{"database-type":"mssql","connection-string":"PRIVATE_FULL_CONTEXT_58e12"},
                  "runtime":{"rest":false,"graphql":true,"mcp":true,"cache":{"enabled":true},
                    "host":{"authentication":{"provider":"PRIVATE_FULL_CONTEXT_58e12"}}},
                  "entities":{"PRIVATE_FULL_CONTEXT_58e12":{"source":"PRIVATE_FULL_CONTEXT_58e12","permissions":[]}} }
                """;
            // Maximum envelope pressure: all approved slots plus context and both identities.
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
            Assert.AreEqual(96, keys.Length);
            RuntimeConfig config = JsonSerializer.Deserialize<RuntimeConfig>(JSON, RuntimeConfigLoader.GetSerializationOptions())!;
            config = config with { TelemetryPresence = TelemetryConfigurationPresence.TryCaptureBounded(JSON, config, enabled: true) };
            Assert.IsNotNull(config.TelemetryPresence);
            RecordingHandler handler = new();
            ApplicationInsightsTelemetryDestination destination = NewDestination();
            using ProductTelemetrySenderPool pool = new(selected => new EngineTelemetryApplicationInsightsExporter(selected, handler));
            using CliTelemetrySession session = CliTelemetrySession.Create(() => pool.AcquireLease(destination),
                enableSyntheticCollection: true, readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveInstallation: () => new(Guid.NewGuid(), "reused"),
                lookupIdentity: _ => new(Guid.NewGuid(), "reused"),
                createIdentity: _ => throw new AssertFailedException("Configuration projection must not create API identity."));
            session.ObserveConfiguration(ROOT);
            if (observed)
            {
                session.ObserveConfigurationDetails(ROOT, config);
            }

            session.Complete("configure", "none", keys.ToImmutableDictionary(key => "option_" + key, _ => "true"),
                CliTelemetryOutcome.ValidationFailure, CliTelemetryFailureCategory.Configuration);
            await session.StopAsync().WaitAsync(_testTimeout);

            CapturedRequest request = handler.Requests.Single();
            Assert.IsFalse(request.Body.Contains(ROOT, StringComparison.Ordinal));
            Assert.IsTrue(Encoding.UTF8.GetByteCount(request.Body) <= 65536);
            Assert.IsTrue(EngineTelemetrySdkTransportHandler.HasOnlyApprovedEnvelopeTags(request.Body));
            using JsonDocument body = JsonDocument.Parse(request.Body);
            JsonElement properties = EventProperties(body);
            Assert.AreEqual(96, properties.EnumerateObject().Count(property => property.Name.StartsWith("option_", StringComparison.Ordinal)));
            Assert.IsTrue(properties.TryGetProperty("dab_api_id", out _));
            Assert.IsTrue(properties.TryGetProperty("dab_installation_id", out _));
            Assert.IsFalse(properties.TryGetProperty("dab_config_epoch", out _));
            string contextJson = properties.GetProperty("configuration_context").GetString()!;
            Assert.IsTrue(contextJson.Length <= 8192);
            using JsonDocument context = JsonDocument.Parse(contextJson);
            Assert.AreEqual(83, context.RootElement.EnumerateObject().Count());
            Assert.AreEqual("cli-configuration-v1", context.RootElement.GetProperty("snapshot_schema").GetString());
            Assert.AreEqual(observed ? "loaded" : "unknown", context.RootElement.GetProperty("observation").GetString());
            Assert.AreEqual(observed ? "disabled" : "unknown", context.RootElement.GetProperty("runtime.rest.configured").GetString());
            Assert.AreEqual(observed ? "jwt" : "unknown", context.RootElement.GetProperty("authentication.provider.configured").GetString());
            Assert.AreEqual(observed ? "1" : "unknown", context.RootElement.GetProperty("scale.entity_count").GetString());
        }

        [TestMethod]
        public async Task CachedSdkHostOverridesAreRejectedInAnIsolatedProcess()
        {
            const string WORKER = "DAB_CLI_SDK_OVERRIDE_TEST_WORKER";
            if (Environment.GetEnvironmentVariable(WORKER) == "1")
            {
                // SDK 1.9 caches these overrides at first envelope construction, independently
                // of the empty OTel resource. The transport must reject, not rewrite or transmit.
                RecordingHandler handler = new();
                using ProductTelemetrySenderPool pool = new(selected => new EngineTelemetryApplicationInsightsExporter(selected, handler));
                using IProductTelemetryExporter<IProductTelemetryEvent> lease = pool.AcquireLease(NewDestination());
                Assert.IsFalse(await lease.ExportAsync(CreateCliEvent(), CancellationToken.None));
                Assert.AreEqual(0, handler.Requests.Count);
                return;
            }

            ProcessStartInfo start = new("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            start.ArgumentList.Add("vstest");
            start.ArgumentList.Add(typeof(CliTelemetryExporterTests).Assembly.Location);
            start.ArgumentList.Add("--TestCaseFilter:FullyQualifiedName=" + typeof(CliTelemetryExporterTests).FullName + "." + nameof(CachedSdkHostOverridesAreRejectedInAnIsolatedProcess));
            start.ArgumentList.Add("--logger:console;verbosity=minimal");
            // Testhost can run from a read-only mounted assembly directory. The child adapter
            // needs a writable results location even though it writes only a console receipt.
            start.ArgumentList.Add("--ResultsDirectory:" + System.IO.Path.GetTempPath());
            start.Environment[WORKER] = "1";
            start.Environment[EngineTelemetryApplicationInsightsExporter.STATSBEAT_DISABLED_VARIABLE] = "true";
            start.Environment[EngineTelemetryApplicationInsightsExporter.SDK_STATS_DISABLED_VARIABLE] = "true";
            foreach (string name in new[] { "APPLICATIONINSIGHTS_CLOUD_ROLE_NAME", "APPLICATIONINSIGHTS_CLOUD_ROLE_INSTANCE", "APPLICATIONINSIGHTS_COMPONENT_VERSION" })
            {
                start.Environment[name] = "PRIVATE_SYNTHETIC_SDK_OVERRIDE";
            }

            using Process process = Process.Start(start)!;
            Task<string> output = process.StandardOutput.ReadToEndAsync();
            Task<string> error = process.StandardError.ReadToEndAsync();
            try
            {
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(45));
                string result = await output;
                string diagnostic = await error;
                Assert.AreEqual(0, process.ExitCode, "Isolated SDK privacy control must pass.\n" + result + diagnostic);
                Assert.IsTrue(result.Contains("Passed:", StringComparison.Ordinal) && result.Contains("Total:", StringComparison.Ordinal),
                    "A successful process with no selected tests is not privacy evidence.");
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                }
            }
        }

        [TestMethod]
        public async Task EngineCompatibilityAndExistingLogExporterSeamRemainUsable()
        {
            CapturingLogExporter logExporter = new();
            using EngineTelemetryApplicationInsightsExporter adapter = new(logExporter);
            IEngineTelemetryExporter engine = adapter;
            IProductTelemetryExporter<IProductTelemetryEvent> shared = adapter;
            EngineTelemetryEvent engineRecord = CreateEngineEvent();
            Assert.IsTrue(await engine.ExportAsync(engineRecord, CancellationToken.None));
            Assert.IsTrue(await shared.ExportAsync(CreateCliEvent(), CancellationToken.None));
            Assert.IsFalse(await shared.ExportAsync(new NonSyntheticEvent(CreateCliEvent()), CancellationToken.None));

            Assert.AreEqual(2, logExporter.Records.Count);
            Assert.AreEqual(engineRecord.ConfigurationEpoch.ToString(CultureInfo.InvariantCulture), logExporter.Records[0]["dab_config_epoch"]);
            Assert.IsFalse(logExporter.Records[1].ContainsKey("dab_config_epoch"));
            adapter.Dispose();
            adapter.Dispose();
            Assert.AreEqual(1, logExporter.DisposeCalls);
        }

        [TestMethod]
        public async Task NormalizedDestinationSharesOneActualSdkSenderAcrossCliAndEngineLeases()
        {
            ApplicationInsightsTelemetryDestination destination = NewDestination();
            Assert.IsTrue(ApplicationInsightsTelemetryDestination.TryParse(
                $" IngestionEndpoint=HTTPS://CLI.SYNTHETIC.INVALID:443; InstrumentationKey={destination.InstrumentationKey.ToString("D").ToUpperInvariant()};Authorization=ikey; ",
                out ApplicationInsightsTelemetryDestination? equivalent));
            Assert.AreEqual(destination.ConnectionString, equivalent.ConnectionString);
            RecordingHandler handler = new();
            int factories = 0;
            using ProductTelemetrySenderPool pool = new(selected =>
            {
                Interlocked.Increment(ref factories);
                return new EngineTelemetryApplicationInsightsExporter(selected, handler);
            });
            using IProductTelemetryExporter<IProductTelemetryEvent> first = pool.AcquireLease(destination);
            using IProductTelemetryExporter<IProductTelemetryEvent> second = pool.AcquireLease(equivalent);
            IProductTelemetryExporter<CliTelemetryEvent> cli = first;
            IProductTelemetryExporter<EngineTelemetryEvent> engine = second;
            Assert.AreEqual(0, factories, "Acquiring leases must not initialize the SDK.");
            Assert.IsTrue(await cli.ExportAsync(CreateCliEvent(), CancellationToken.None));
            first.Dispose();
            first.Dispose();
            Assert.IsFalse(await first.ExportAsync(CreateCliEvent(), CancellationToken.None));
            EngineTelemetryEvent engineRecord = CreateEngineEvent();
            Assert.IsTrue(await engine.ExportAsync(engineRecord, CancellationToken.None));
            Assert.AreEqual(1, factories);
            Assert.AreEqual(2, handler.Requests.Count);
            Assert.AreEqual(0, handler.DisposeCalls);
            using JsonDocument engineBody = JsonDocument.Parse(handler.Requests.Last().Body);
            Assert.AreEqual(engineRecord.ConfigurationEpoch.ToString(CultureInfo.InvariantCulture), EventProperties(engineBody).GetProperty("dab_config_epoch").GetString());

            second.Dispose();
            Assert.AreEqual(0, handler.DisposeCalls, "An idle sender is process-owned, not owned by the last invocation.");
            pool.Dispose();
            pool.Dispose();
            Assert.AreEqual(1, handler.DisposeCalls);
        }

        [TestMethod]
        public async Task RepeatedDeliveryInvocationsReuseTheSenderAndCreateItOnlyOnTheWorker()
        {
            ApplicationInsightsTelemetryDestination destination = NewDestination();
            RecordingHandler handler = new();
            AsyncLocal<string?> callerContext = new() { Value = "enqueueing-command" };
            bool factorySawCallerContext = false;
            int factories = 0;
            using ProductTelemetrySenderPool pool = new(selected =>
            {
                factorySawCallerContext = callerContext.Value is not null;
                Interlocked.Increment(ref factories);
                return new EngineTelemetryApplicationInsightsExporter(selected, handler);
            });
            using (IProductTelemetryExporter<IProductTelemetryEvent> unused = pool.AcquireLease(destination))
            {
                Assert.AreEqual(0, factories);
                Assert.AreEqual(0, handler.Requests.Count);
            }

            for (int invocation = 1; invocation <= 3; invocation++)
            {
                using ProductTelemetryDelivery<CliTelemetryEvent> delivery = new(() => pool.AcquireLease(destination));
                Assert.IsTrue(delivery.TryEnqueue(CreateCliEvent(invocation)));
                await delivery.StopAsync().WaitAsync(_testTimeout);
                Assert.AreEqual(0L, delivery.DroppedEvents);
                Assert.AreEqual(1, factories);
                Assert.AreEqual(invocation, handler.Requests.Count);
                Assert.AreEqual(0, handler.DisposeCalls);
            }

            using (ProductTelemetryDelivery<EngineTelemetryEvent> delivery = new(() => pool.AcquireLease(destination)))
            {
                Assert.IsTrue(delivery.TryEnqueue(CreateEngineEvent()));
                await delivery.StopAsync().WaitAsync(_testTimeout);
                Assert.AreEqual(0L, delivery.DroppedEvents);
            }

            Assert.IsFalse(factorySawCallerContext, "SDK initialization must not run in the enqueueing command's execution context.");
            Assert.AreEqual(1, factories);
            Assert.AreEqual(4, handler.Requests.Count);
            pool.Dispose();
            Assert.AreEqual(1, handler.DisposeCalls);
        }

        [TestMethod]
        public async Task WaitingOwnerCancellationDoesNotCancelTheActiveSdkCall()
        {
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            RecordingHandler handler = new()
            {
                Response = async token =>
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(token);
                    return AcceptedResponse();
                }
            };
            using ProductTelemetrySenderPool pool = new(selected => new EngineTelemetryApplicationInsightsExporter(selected, handler));
            ApplicationInsightsTelemetryDestination destination = NewDestination();
            using IProductTelemetryExporter<IProductTelemetryEvent> first = pool.AcquireLease(destination);
            using IProductTelemetryExporter<IProductTelemetryEvent> second = pool.AcquireLease(destination);
            Task<bool> active = Task.Run(async () => await first.ExportAsync(CreateEngineEvent(), CancellationToken.None));
            try
            {
                await entered.Task.WaitAsync(_testTimeout);
                using CancellationTokenSource cancellation = new();
                Task<bool> waiting = second.ExportAsync(CreateCliEvent(), cancellation.Token).AsTask();
                Assert.IsFalse(waiting.IsCompleted);
                Assert.AreEqual(1, handler.Requests.Count);
                cancellation.Cancel();
                Assert.IsFalse(await waiting.WaitAsync(_testTimeout));
                Assert.IsFalse(active.IsCompleted, "Canceling a waiter must not cancel the current owner.");
                Assert.IsFalse(handler.Requests.Single().Token.IsCancellationRequested);
                second.Dispose();
                release.TrySetResult();
                Assert.IsTrue(await active.WaitAsync(_testTimeout));
                Assert.IsTrue(await first.ExportAsync(CreateEngineEvent(), CancellationToken.None));
                Assert.AreEqual(2, handler.Requests.Count);
                Assert.AreEqual(1, handler.MaximumConcurrentRequests);
                Assert.AreEqual(0, handler.DisposeCalls);
            }
            finally
            {
                release.TrySetResult();
                await active.WaitAsync(_testTimeout);
            }
        }

        [TestMethod]
        public async Task ActiveOwnerCancellationDoesNotPoisonTheNextSdkCall()
        {
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int responses = 0;
            RecordingHandler handler = new()
            {
                Response = async token =>
                {
                    if (Interlocked.Increment(ref responses) == 1)
                    {
                        entered.TrySetResult();
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    }

                    return AcceptedResponse();
                }
            };
            using ProductTelemetrySenderPool pool = new(selected => new EngineTelemetryApplicationInsightsExporter(selected, handler));
            ApplicationInsightsTelemetryDestination destination = NewDestination();
            using IProductTelemetryExporter<IProductTelemetryEvent> first = pool.AcquireLease(destination);
            using IProductTelemetryExporter<IProductTelemetryEvent> second = pool.AcquireLease(destination);
            using CancellationTokenSource cancellation = new();
            Task<bool> active = Task.Run(async () => await first.ExportAsync(CreateCliEvent(), cancellation.Token));
            try
            {
                await entered.Task.WaitAsync(_testTimeout);
                EngineTelemetryEvent engineRecord = CreateEngineEvent();
                Task<bool> waiting = second.ExportAsync(engineRecord, CancellationToken.None).AsTask();
                Assert.IsFalse(waiting.IsCompleted);
                cancellation.Cancel();
                Assert.IsFalse(await active.WaitAsync(_testTimeout));
                first.Dispose();
                Assert.IsTrue(await waiting.WaitAsync(_testTimeout));
                Assert.AreEqual(2, handler.Requests.Count);
                Assert.AreEqual(1, handler.MaximumConcurrentRequests);
                Assert.AreEqual(0, handler.DisposeCalls);
                using JsonDocument secondBody = JsonDocument.Parse(handler.Requests.Last().Body);
                Assert.AreEqual(engineRecord.EventId.ToString("D"), EventProperties(secondBody).GetProperty("dab_event_id").GetString());
            }
            finally
            {
                cancellation.Cancel();
                await active.WaitAsync(_testTimeout);
            }
        }

        [TestMethod]
        public async Task PreCanceledOrNonSyntheticCallsDoNotInitializeASender()
        {
            int factories = 0;
            RecordingHandler handler = new();
            using ProductTelemetrySenderPool pool = new(selected =>
            {
                Interlocked.Increment(ref factories);
                return new EngineTelemetryApplicationInsightsExporter(selected, handler);
            });
            using IProductTelemetryExporter<IProductTelemetryEvent> lease = pool.AcquireLease(NewDestination());
            using CancellationTokenSource cancellation = new();
            cancellation.Cancel();
            Assert.IsFalse(await lease.ExportAsync(CreateCliEvent(), cancellation.Token));
            Assert.IsFalse(await lease.ExportAsync(new NonSyntheticEvent(CreateCliEvent()), CancellationToken.None));
            Assert.AreEqual(0, factories);
            Assert.AreEqual(0, handler.Requests.Count);
            Assert.IsTrue(await lease.ExportAsync(CreateCliEvent(), CancellationToken.None));
            Assert.AreEqual(1, factories);
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task ReacquireAfterLastOwnerReleaseKeepsTheInFlightSdkSender()
        {
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            RecordingHandler handler = new()
            {
                Response = async token =>
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(token);
                    return AcceptedResponse();
                }
            };
            int factories = 0;
            using ProductTelemetrySenderPool pool = new(selected =>
            {
                Interlocked.Increment(ref factories);
                return new EngineTelemetryApplicationInsightsExporter(selected, handler);
            });
            ApplicationInsightsTelemetryDestination destination = NewDestination();
            using IProductTelemetryExporter<IProductTelemetryEvent> first = pool.AcquireLease(destination);
            Task<bool> active = Task.Run(async () => await first.ExportAsync(CreateCliEvent(), CancellationToken.None));
            try
            {
                await entered.Task.WaitAsync(_testTimeout);
                first.Dispose();
                using IProductTelemetryExporter<IProductTelemetryEvent> next = pool.AcquireLease(destination);
                Task<bool> waiting = next.ExportAsync(CreateCliEvent(2), CancellationToken.None).AsTask();
                Assert.IsFalse(waiting.IsCompleted);
                Assert.AreEqual(0, handler.DisposeCalls);
                release.TrySetResult();
                Assert.IsTrue(await active.WaitAsync(_testTimeout));
                Assert.IsTrue(await waiting.WaitAsync(_testTimeout));
                Assert.AreEqual(1, factories);
                Assert.AreEqual(2, handler.Requests.Count);
                Assert.AreEqual(1, handler.MaximumConcurrentRequests);
                Assert.IsFalse(handler.DisposedDuringRequest);
            }
            finally
            {
                release.TrySetResult();
                await active.WaitAsync(_testTimeout);
            }

            pool.Dispose();
            Assert.AreEqual(1, handler.DisposeCalls);
        }

        [TestMethod]
        public async Task ClosingPoolDefersCleanupUntilTheLastActiveOperationFinishes()
        {
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            RecordingHandler handler = new()
            {
                Response = async token =>
                {
                    entered.TrySetResult();
                    await release.Task.WaitAsync(token);
                    return AcceptedResponse();
                }
            };
            using ProductTelemetrySenderPool pool = new(selected => new EngineTelemetryApplicationInsightsExporter(selected, handler));
            ApplicationInsightsTelemetryDestination destination = NewDestination();
            using IProductTelemetryExporter<IProductTelemetryEvent> lease = pool.AcquireLease(destination);
            Task<bool> active = Task.Run(async () => await lease.ExportAsync(CreateCliEvent(), CancellationToken.None));
            try
            {
                await entered.Task.WaitAsync(_testTimeout);
                lease.Dispose();
                pool.Dispose();
                pool.Dispose();
                Assert.AreEqual(0, handler.DisposeCalls);
                Assert.IsFalse(active.IsCompleted);
                Assert.ThrowsException<ObjectDisposedException>(() => pool.AcquireLease(destination));
                Assert.IsFalse(await lease.ExportAsync(CreateCliEvent(), CancellationToken.None));
                release.TrySetResult();
                Assert.IsTrue(await active.WaitAsync(_testTimeout));
                Assert.AreEqual(1, handler.DisposeCalls);
                Assert.IsFalse(handler.DisposedDuringRequest);
            }
            finally
            {
                release.TrySetResult();
                await active.WaitAsync(_testTimeout);
            }

            lease.Dispose();
            Assert.AreEqual(1, handler.DisposeCalls);
        }

        [TestMethod]
        public async Task ClosingPoolDefersCleanupUntilAllOwnersReleaseTheirLeases()
        {
            RecordingHandler handler = new();
            using ProductTelemetrySenderPool pool = new(selected => new EngineTelemetryApplicationInsightsExporter(selected, handler));
            ApplicationInsightsTelemetryDestination destination = NewDestination();
            using IProductTelemetryExporter<IProductTelemetryEvent> first = pool.AcquireLease(destination);
            using IProductTelemetryExporter<IProductTelemetryEvent> second = pool.AcquireLease(destination);
            Assert.IsTrue(await first.ExportAsync(CreateCliEvent(), CancellationToken.None));
            pool.Dispose();
            Assert.AreEqual(0, handler.DisposeCalls);
            first.Dispose();
            first.Dispose();
            Assert.AreEqual(0, handler.DisposeCalls);
            Assert.IsFalse(await second.ExportAsync(CreateCliEvent(), CancellationToken.None));
            second.Dispose();
            Assert.AreEqual(1, handler.DisposeCalls);
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task CapacityIncludesIdleDestinationsAndRejectsBeforeSdkConstruction()
        {
            List<RecordingHandler> handlers = [];
            using ProductTelemetrySenderPool pool = new(selected =>
            {
                RecordingHandler handler = new();
                handlers.Add(handler);
                return new EngineTelemetryApplicationInsightsExporter(selected, handler);
            }, capacity: 2);
            ApplicationInsightsTelemetryDestination firstDestination = NewDestination();
            using (IProductTelemetryExporter<IProductTelemetryEvent> first = pool.AcquireLease(firstDestination))
            {
                Assert.IsTrue(await first.ExportAsync(CreateCliEvent(), CancellationToken.None));
            }

            using (IProductTelemetryExporter<IProductTelemetryEvent> second = pool.AcquireLease(NewDestination()))
            {
                Assert.IsTrue(await second.ExportAsync(CreateCliEvent(), CancellationToken.None));
            }

            for (int rejected = 0; rejected < 10; rejected++)
            {
                Assert.ThrowsException<InvalidOperationException>(() => pool.AcquireLease(NewDestination()));
            }

            using (IProductTelemetryExporter<IProductTelemetryEvent> again = pool.AcquireLease(firstDestination))
            {
                Assert.IsTrue(await again.ExportAsync(CreateCliEvent(), CancellationToken.None));
            }

            Assert.AreEqual(2, handlers.Count);
            Assert.AreEqual(2, handlers[0].Requests.Count);
            Assert.AreEqual(1, handlers[1].Requests.Count);
            Assert.IsTrue(handlers.All(handler => handler.DisposeCalls == 0));
            pool.Dispose();
            Assert.IsTrue(handlers.All(handler => handler.DisposeCalls == 1));
        }

        [TestMethod]
        public void DefaultCapacityReservesAtMostEightDestinationsWithoutInitializingSdk()
        {
            int factories = 0;
            using ProductTelemetrySenderPool pool = new(_ =>
            {
                Interlocked.Increment(ref factories);
                throw new InvalidOperationException("No export was requested.");
            });
            for (int destination = 0; destination < 8; destination++)
            {
                using IProductTelemetryExporter<IProductTelemetryEvent> lease = pool.AcquireLease(NewDestination());
            }

            Assert.ThrowsException<InvalidOperationException>(() => pool.AcquireLease(NewDestination()));
            Assert.AreEqual(0, factories);
        }

        [TestMethod]
        public async Task FailedFactoryDoesNotPoisonOtherOwnersOrKeepTheSdkGate()
        {
            int factories = 0;
            RecordingHandler handler = new();
            using ProductTelemetrySenderPool pool = new(selected =>
            {
                if (Interlocked.Increment(ref factories) == 1)
                {
                    throw new InvalidOperationException("Synthetic initialization failure.");
                }

                return new EngineTelemetryApplicationInsightsExporter(selected, handler);
            });
            ApplicationInsightsTelemetryDestination destination = NewDestination();
            using IProductTelemetryExporter<IProductTelemetryEvent> first = pool.AcquireLease(destination);
            using IProductTelemetryExporter<IProductTelemetryEvent> second = pool.AcquireLease(destination);
            Assert.IsFalse(await first.ExportAsync(CreateCliEvent(), CancellationToken.None));
            Assert.IsTrue(await second.ExportAsync(CreateEngineEvent(), CancellationToken.None));
            Assert.AreEqual(2, factories);
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task ActualSdkCanBeRecreatedAfterAnIsolatedPoolHasCompletelyDisposed()
        {
            // Characterizes the pinned 1.9.0 disposed-transmitter replacement. Normal repeated
            // invocations do NOT rely on this behavior: their process-owned pool retains a sender.
            ApplicationInsightsTelemetryDestination destination = NewDestination();
            RecordingHandler firstHandler = new();
            using (ProductTelemetrySenderPool firstPool = new(selected => new EngineTelemetryApplicationInsightsExporter(selected, firstHandler)))
            {
                using IProductTelemetryExporter<IProductTelemetryEvent> first = firstPool.AcquireLease(destination);
                Assert.IsTrue(await first.ExportAsync(CreateCliEvent(), CancellationToken.None));
            }

            Assert.AreEqual(1, firstHandler.DisposeCalls);
            RecordingHandler secondHandler = new();
            using (ProductTelemetrySenderPool secondPool = new(selected => new EngineTelemetryApplicationInsightsExporter(selected, secondHandler)))
            {
                using IProductTelemetryExporter<IProductTelemetryEvent> second = secondPool.AcquireLease(destination);
                Assert.IsTrue(await second.ExportAsync(CreateCliEvent(), CancellationToken.None));
                Assert.AreEqual(1, firstHandler.Requests.Count);
                Assert.AreEqual(1, secondHandler.Requests.Count);
                Assert.AreEqual(0, secondHandler.DisposeCalls);
            }

            Assert.AreEqual(1, secondHandler.DisposeCalls);
        }

        [DataTestMethod]
        [DataRow(ProductTelemetryBootstrap.OPT_OUT_VARIABLE, "1")]
        [DataRow(ProductTelemetryBootstrap.OPT_OUT_VARIABLE, " true ")]
        [DataRow(ProductTelemetryBootstrap.TEST_MODE_VARIABLE, null)]
        [DataRow(ProductTelemetryBootstrap.TEST_MODE_VARIABLE, "false")]
        [DataRow(EngineTelemetryApplicationInsightsExporter.STATSBEAT_DISABLED_VARIABLE, null)]
        [DataRow(EngineTelemetryApplicationInsightsExporter.STATSBEAT_DISABLED_VARIABLE, "false")]
        [DataRow(EngineTelemetryApplicationInsightsExporter.STATSBEAT_DISABLED_VARIABLE, "1")]
        [DataRow(EngineTelemetryApplicationInsightsExporter.STATSBEAT_DISABLED_VARIABLE, " true ")]
        [DataRow(EngineTelemetryApplicationInsightsExporter.SDK_STATS_DISABLED_VARIABLE, null)]
        [DataRow(EngineTelemetryApplicationInsightsExporter.SDK_STATS_DISABLED_VARIABLE, "false")]
        [DataRow(EngineTelemetryApplicationInsightsExporter.SDK_STATS_DISABLED_VARIABLE, "1")]
        [DataRow(EngineTelemetryApplicationInsightsExporter.SDK_STATS_DISABLED_VARIABLE, " true ")]
        public void BootstrapVetoesBeforeReadingDestination(string variable, string? value)
        {
            Dictionary<string, string?> environment = BootstrapEnvironment();
            environment[variable] = value;
            List<string> reads = [];
            Assert.IsFalse(ProductTelemetryBootstrap.TryGetDestination(name =>
            {
                reads.Add(name);
                return environment.GetValueOrDefault(name);
            }, out ApplicationInsightsTelemetryDestination? destination));
            Assert.IsNull(destination);
            Assert.IsFalse(reads.Contains(ProductTelemetryBootstrap.CONNECTION_STRING_VARIABLE));
            if (variable == ProductTelemetryBootstrap.OPT_OUT_VARIABLE)
            {
                CollectionAssert.AreEqual(new[] { ProductTelemetryBootstrap.OPT_OUT_VARIABLE }, reads);
            }

            Assert.AreEqual(value, environment[variable]);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("invalid-routing")]
        [DataRow("InstrumentationKey=01234567-89ab-cdef-0123-456789abcdef;IngestionEndpoint=http://cli.synthetic.invalid/")]
        public void BootstrapRequiresValidatedProductRoutingWithoutCustomerFallback(string? connectionString)
        {
            Dictionary<string, string?> environment = BootstrapEnvironment();
            environment["APPLICATIONINSIGHTS_CONNECTION_STRING"] = environment[ProductTelemetryBootstrap.CONNECTION_STRING_VARIABLE];
            environment[ProductTelemetryBootstrap.CONNECTION_STRING_VARIABLE] = connectionString;
            List<string> reads = [];
            Assert.IsFalse(ProductTelemetryBootstrap.TryGetDestination(name =>
            {
                reads.Add(name);
                return environment.GetValueOrDefault(name);
            }, out ApplicationInsightsTelemetryDestination? destination));
            Assert.IsNull(destination);
            Assert.IsFalse(reads.Contains("APPLICATIONINSIGHTS_CONNECTION_STRING"));
        }

        [TestMethod]
        public void BootstrapAcceptsOnlyExplicitSyntheticRoutingAndBothStrictSdkOptOuts()
        {
            Dictionary<string, string?> environment = BootstrapEnvironment();
            environment[ProductTelemetryBootstrap.TEST_MODE_VARIABLE] = " true ";
            environment[EngineTelemetryApplicationInsightsExporter.STATSBEAT_DISABLED_VARIABLE] = "TRUE";
            environment[EngineTelemetryApplicationInsightsExporter.SDK_STATS_DISABLED_VARIABLE] = "TrUe";
            KeyValuePair<string, string?>[] original = environment.ToArray();
            List<string> reads = [];
            Assert.IsTrue(ProductTelemetryBootstrap.TryGetDestination(name =>
            {
                reads.Add(name);
                return environment.GetValueOrDefault(name);
            }, out ApplicationInsightsTelemetryDestination? destination));
            Assert.AreEqual(environment[ProductTelemetryBootstrap.CONNECTION_STRING_VARIABLE], destination.ConnectionString);
            CollectionAssert.AreEqual(new[]
            {
                ProductTelemetryBootstrap.OPT_OUT_VARIABLE,
                ProductTelemetryBootstrap.TEST_MODE_VARIABLE,
                EngineTelemetryApplicationInsightsExporter.STATSBEAT_DISABLED_VARIABLE,
                EngineTelemetryApplicationInsightsExporter.SDK_STATS_DISABLED_VARIABLE,
                ProductTelemetryBootstrap.CONNECTION_STRING_VARIABLE
            }, reads);
            CollectionAssert.AreEqual(original, environment.ToArray());
        }

        [DataTestMethod]
        [DataRow(ProductTelemetryBootstrap.OPT_OUT_VARIABLE)]
        [DataRow(ProductTelemetryBootstrap.TEST_MODE_VARIABLE)]
        [DataRow(EngineTelemetryApplicationInsightsExporter.STATSBEAT_DISABLED_VARIABLE)]
        [DataRow(EngineTelemetryApplicationInsightsExporter.SDK_STATS_DISABLED_VARIABLE)]
        [DataRow(ProductTelemetryBootstrap.CONNECTION_STRING_VARIABLE)]
        public void BootstrapEnvironmentExceptionsFailClosed(string throwingVariable)
        {
            Dictionary<string, string?> environment = BootstrapEnvironment();
            List<string> reads = [];
            Assert.IsFalse(ProductTelemetryBootstrap.TryGetDestination(name =>
            {
                reads.Add(name);
                return name == throwingVariable
                    ? throw new InvalidOperationException("Synthetic environment failure.")
                    : environment.GetValueOrDefault(name);
            }, out ApplicationInsightsTelemetryDestination? destination));
            Assert.IsNull(destination);
            Assert.AreEqual(throwingVariable, reads.Last());
            if (throwingVariable != ProductTelemetryBootstrap.CONNECTION_STRING_VARIABLE)
            {
                Assert.IsFalse(reads.Contains(ProductTelemetryBootstrap.CONNECTION_STRING_VARIABLE));
            }
        }

        private static ApplicationInsightsTelemetryDestination NewDestination()
        {
            Assert.IsTrue(ApplicationInsightsTelemetryDestination.TryParse(
                $"InstrumentationKey={Guid.NewGuid():D};IngestionEndpoint=https://cli.synthetic.invalid/",
                out ApplicationInsightsTelemetryDestination? destination));
            return destination;
        }

        private static Dictionary<string, string?> BootstrapEnvironment() => new()
        {
            [ProductTelemetryBootstrap.OPT_OUT_VARIABLE] = null,
            [ProductTelemetryBootstrap.TEST_MODE_VARIABLE] = "1",
            [EngineTelemetryApplicationInsightsExporter.STATSBEAT_DISABLED_VARIABLE] = "true",
            [EngineTelemetryApplicationInsightsExporter.SDK_STATS_DISABLED_VARIABLE] = "true",
            [ProductTelemetryBootstrap.CONNECTION_STRING_VARIABLE] = NewDestination().ConnectionString
        };

        private static CliTelemetryEvent CreateCliEvent(long sequence = 1, string name = "dab.cli.command") => new(
            Guid.NewGuid(), Guid.NewGuid(), sequence,
            new DateTimeOffset(2026, 9, 25, 10, 11, 12, TimeSpan.FromMinutes(330)).AddTicks(3456789),
            name, ImmutableDictionary<string, string>.Empty.Add("command", "init"));

        private static EngineTelemetryEvent CreateEngineEvent() => new(
            Guid.NewGuid(), Guid.NewGuid(), 17,
            new DateTimeOffset(2026, 9, 25, 10, 11, 12, TimeSpan.Zero),
            long.MaxValue, "dab.engine.usage_summary", ImmutableDictionary<string, string>.Empty.Add("api", "rest"));

        private static JsonElement EventProperties(JsonDocument body)
            => body.RootElement.GetProperty("data").GetProperty("baseData").GetProperty("properties");

        private static HttpResponseMessage AcceptedResponse() => new(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"itemsReceived\":1,\"itemsAccepted\":1,\"errors\":[]}", Encoding.UTF8, "application/json")
        };

        private sealed record CapturedRequest(Uri Uri, string Body, Dictionary<string, string> Headers,
            bool InstrumentationSuppressed, bool HadActivity, CancellationToken Token);

        private sealed class RecordingHandler : HttpMessageHandler
        {
            private int _activeRequests;
            private int _maximumConcurrentRequests;
            private int _disposeCalls;
            private int _disposedDuringRequest;

            internal ConcurrentQueue<CapturedRequest> Requests { get; } = new();
            internal Func<CancellationToken, Task<HttpResponseMessage>> Response { get; set; } = _ => Task.FromResult(AcceptedResponse());
            internal int MaximumConcurrentRequests => Volatile.Read(ref _maximumConcurrentRequests);
            internal int DisposeCalls => Volatile.Read(ref _disposeCalls);
            internal bool DisposedDuringRequest => Volatile.Read(ref _disposedDuringRequest) != 0;

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                int active = Interlocked.Increment(ref _activeRequests);
                int observed = Volatile.Read(ref _maximumConcurrentRequests);
                while (active > observed)
                {
                    int previous = Interlocked.CompareExchange(ref _maximumConcurrentRequests, active, observed);
                    if (previous == observed)
                    {
                        break;
                    }

                    observed = previous;
                }

                try
                {
                    string body = await request.Content!.ReadAsStringAsync(cancellationToken);
                    Dictionary<string, string> headers = request.Headers.Concat(request.Content.Headers)
                        .ToDictionary(pair => pair.Key, pair => string.Join(",", pair.Value), StringComparer.OrdinalIgnoreCase);
                    Requests.Enqueue(new(request.RequestUri!, body, headers,
                        Sdk.SuppressInstrumentation, Activity.Current is not null, cancellationToken));
                    return await Response(cancellationToken);
                }
                finally
                {
                    Interlocked.Decrement(ref _activeRequests);
                }
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    Interlocked.Increment(ref _disposeCalls);
                    if (Volatile.Read(ref _activeRequests) != 0)
                    {
                        Interlocked.Exchange(ref _disposedDuringRequest, 1);
                    }
                }

                base.Dispose(disposing);
            }
        }

        private sealed class CapturingLogExporter : BaseExporter<LogRecord>
        {
            internal List<Dictionary<string, object?>> Records { get; } = [];
            internal int DisposeCalls { get; private set; }

            public override ExportResult Export(in Batch<LogRecord> batch)
            {
                foreach (LogRecord record in batch)
                {
                    Records.Add(record.Attributes!.ToDictionary(pair => pair.Key, pair => pair.Value));
                }

                return ExportResult.Success;
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    DisposeCalls++;
                }

                base.Dispose(disposing);
            }
        }

        private sealed class NonSyntheticEvent(IProductTelemetryEvent record) : IProductTelemetryEvent
        {
            public Guid EventId => record.EventId;
            public Guid SessionId => record.SessionId;
            public long Sequence => record.Sequence;
            public DateTimeOffset OccurredAt => record.OccurredAt;
            public string Name => record.Name;
            public ImmutableDictionary<string, string> Properties => record.Properties;
            public bool IsSynthetic => false;
        }
    }
}
