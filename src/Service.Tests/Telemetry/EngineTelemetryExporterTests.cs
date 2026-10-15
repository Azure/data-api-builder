// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core.Pipeline;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.Telemetry;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Service.Telemetry;
using Azure.Monitor.OpenTelemetry.Exporter;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OpenTelemetry;
using OpenTelemetry.Logs;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>The actual SDK uses intercepted HTTP. Statistics are disabled before testhost startup.</summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    [DoNotParallelize]
    public class EngineTelemetryExporterTests
    {
        private const string KEY = "01234567-89ab-cdef-0123-456789abcdef";
        private const string CONNECTION_STRING = "InstrumentationKey=" + KEY + ";IngestionEndpoint=https://synthetic.invalid/";
        private static readonly TimeSpan _testTimeout = TimeSpan.FromSeconds(10);

        [TestInitialize]
        public void RequireOfflineSdkTestEnvironment()
        {
            Assert.IsTrue(EngineTelemetryApplicationInsightsExporter.AreSdkStatisticsDisabled(),
                "Use telemetry.runsettings so both SDK statistics opt-outs are set before testhost startup.");
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public void UrlPreflightUsesTheSameGatedBootstrapLifetime(bool enabled)
        {
            string? previousUrls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
            TextWriter previousError = Console.Error;
            using StringWriter errors = new();
            BootstrapExporter exporter = new();
            int factories = 0;
            using EngineTelemetrySession session = EngineTelemetrySession.Create(() =>
                {
                    Interlocked.Increment(ref factories);
                    return exporter;
                }, enableSyntheticCollection: enabled, readEnvironmentVariable: _ => null,
                showNotice: () => { }, startTimer: false);
            try
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_URLS", "not-a-url");
                Console.SetError(errors);
                Assert.IsFalse(Program.StartEngineCore([], runMcpStdio: false, mcpRole: null, session, validateUrls: true));
                Assert.IsTrue(errors.ToString().Contains("Invalid ASPNETCORE_URLS format", StringComparison.Ordinal));
                Assert.AreEqual(enabled ? 1 : 0, factories);
                if (enabled)
                {
                    CollectionAssert.AreEqual(new[] { "dab.engine.process_started", "dab.engine.startup_failed", "dab.engine.stopped" },
                        exporter.Records.Select(record => record.Name).ToArray());
                    Assert.AreEqual("configuration", exporter.Records.Single(record => record.Name == "dab.engine.startup_failed").Properties["failure_stage"]);
                    Assert.IsTrue(exporter.Records.All(record => record.IsSynthetic && record.ConfigurationEpoch == 0));
                }
                else
                {
                    Assert.AreEqual(0, exporter.Records.Count);
                }
            }
            finally
            {
                Console.SetError(previousError);
                Environment.SetEnvironmentVariable("ASPNETCORE_URLS", previousUrls);
            }
        }

        private sealed class BootstrapExporter : IEngineTelemetryExporter
        {
            internal ConcurrentQueue<EngineTelemetryEvent> Records { get; } = new();

            public ValueTask<bool> ExportAsync(EngineTelemetryEvent telemetryEvent, CancellationToken cancellationToken)
            {
                Records.Enqueue(telemetryEvent);
                return ValueTask.FromResult(true);
            }

            public void Dispose() { }
        }

        [TestMethod]
        public void SdkOptionsDisableAutomaticCollectionStorageAndPipelineRetries()
        {
            using HttpClient client = new(new RecordingHandler());
            using HttpClientTransport transport = new(client);
            AzureMonitorExporterOptions options = EngineTelemetryApplicationInsightsExporter.CreateExporterOptions(CONNECTION_STRING, transport);
            Assert.AreEqual(CONNECTION_STRING, options.ConnectionString);
            Assert.AreSame(transport, options.Transport);
            Assert.IsTrue(options.DisableOfflineStorage);
            Assert.IsFalse(options.EnableLiveMetrics);
            Assert.IsFalse(options.EnableStandardMetrics);
            Assert.IsFalse(options.EnablePerformanceCounters);
            Assert.IsFalse(options.EnableTraceBasedLogsSampler);
            Assert.IsFalse(options.Diagnostics.IsLoggingEnabled);
            Assert.IsFalse(options.Diagnostics.IsLoggingContentEnabled);
            Assert.IsFalse(options.Diagnostics.IsDistributedTracingEnabled);
            Assert.IsFalse(options.Diagnostics.IsTelemetryEnabled);
            Assert.AreEqual(0, options.Retry.MaxRetries);
            Assert.AreEqual(TimeSpan.FromSeconds(1), options.Retry.NetworkTimeout);
        }

        [DataTestMethod]
        [DataRow(null)]
        [DataRow("")]
        [DataRow("false")]
        [DataRow("1")]
        [DataRow(" true ")]
        public void MissingStatisticsPrerequisiteRejectsBeforeSdkInitialization(string? value)
        {
            foreach (string variable in new[]
            {
                EngineTelemetryApplicationInsightsExporter.STATSBEAT_DISABLED_VARIABLE,
                EngineTelemetryApplicationInsightsExporter.SDK_STATS_DISABLED_VARIABLE
            })
            {
                string? original = Environment.GetEnvironmentVariable(variable);
                using RecordingHandler handler = new();
                try
                {
                    Environment.SetEnvironmentVariable(variable, value);
                    Assert.ThrowsException<InvalidOperationException>(() => CreateExporter(handler));
                    Assert.AreEqual(0, handler.Requests.Count);
                    Assert.AreEqual(value, Environment.GetEnvironmentVariable(variable));
                }
                finally
                {
                    Environment.SetEnvironmentVariable(variable, original);
                }
            }
        }

        [TestMethod]
        public void DefaultHttpHandlerDisablesRedirectsCookiesAndTracePropagation()
        {
            using SocketsHttpHandler handler = EngineTelemetryApplicationInsightsExporter.CreateHttpHandler();
            Assert.IsFalse(handler.AllowAutoRedirect);
            Assert.IsFalse(handler.UseCookies);
            Assert.IsNull(handler.ActivityHeadersPropagator);
            Assert.AreEqual(TimeSpan.FromSeconds(1), handler.ConnectTimeout);
            Assert.AreEqual(TimeSpan.FromMinutes(5), handler.PooledConnectionLifetime);
        }

        [TestMethod]
        public async Task ExplicitDestinationAndOnlyProductDataAreSent()
        {
            RecordingHandler handler = new();
            using EngineTelemetryApplicationInsightsExporter exporter = CreateExporter(handler, CONNECTION_STRING);
            Assert.AreEqual(0, handler.Requests.Count, "Construction must not start another telemetry stream.");

            EngineTelemetryEvent record = CreateEvent();
            Assert.IsTrue(await exporter.ExportAsync(record, CancellationToken.None));
            CapturedRequest request = handler.Requests.Single();
            Assert.AreEqual(new Uri("https://synthetic.invalid/v2.1/track"), request.Uri);
            Assert.AreEqual("application/json", request.Headers["Content-Type"]);
            Assert.IsTrue(request.InstrumentationSuppressed);
            Assert.IsFalse(request.HadActivity);
            Assert.IsTrue(request.TokenCanBeCanceled);
            foreach (string header in new[] { "Authorization", "Cookie", "traceparent", "tracestate", "Request-Id", "baggage", "User-Agent" })
            {
                Assert.IsFalse(request.Headers.ContainsKey(header), header);
            }

            using JsonDocument body = JsonDocument.Parse(request.Body);
            JsonElement envelope = body.RootElement;
            Assert.AreEqual(KEY, envelope.GetProperty("iKey").GetString());
            Assert.AreEqual("EventData", envelope.GetProperty("data").GetProperty("baseType").GetString());
            Assert.AreEqual("0.0.0.0", envelope.GetProperty("tags").GetProperty("ai.location.ip").GetString());
            Assert.AreEqual(record.OccurredAt, envelope.GetProperty("time").GetDateTimeOffset());
            Assert.IsTrue(EngineTelemetrySdkTransportHandler.HasOnlyApprovedEnvelopeTags(request.Body));
            JsonElement properties = EventProperties(body);
            Assert.AreEqual(9, properties.EnumerateObject().Count());
            Assert.AreEqual(record.EventId.ToString("D"), properties.GetProperty("dab_event_id").GetString());
            Assert.AreEqual(record.OccurredAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), properties.GetProperty("dab_occurred_at").GetString());
            Assert.AreEqual("2", properties.GetProperty("count").GetString());
        }

        [TestMethod]
        public async Task ActualSdkDoesNotExportAmbientActivityOrResourceAttributes()
        {
            const string sentinel = "PRIVATE_AMBIENT_VALUE_184e";
            string? resourceAttributes = Environment.GetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES");
            try
            {
                Environment.SetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES", "host.name=" + sentinel);
                using Activity activity = new Activity(sentinel).SetIdFormat(ActivityIdFormat.W3C).Start();
                activity.TraceStateString = "private=" + sentinel;
                activity.AddBaggage("private", sentinel);
                RecordingHandler handler = new();
                using EngineTelemetryApplicationInsightsExporter exporter = CreateExporter(handler);
                Assert.IsTrue(await exporter.ExportAsync(CreateEvent(), CancellationToken.None));
                CapturedRequest request = handler.Requests.Single();
                Assert.IsFalse(request.Body.Contains(sentinel, StringComparison.Ordinal));
                Assert.IsFalse(request.Body.Contains(activity.TraceId.ToHexString(), StringComparison.Ordinal));
                Assert.IsTrue(EngineTelemetrySdkTransportHandler.HasOnlyApprovedEnvelopeTags(request.Body));
                Assert.AreSame(activity, Activity.Current);
                Assert.AreEqual("host.name=" + sentinel, Environment.GetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES"));
            }
            finally
            {
                Environment.SetEnvironmentVariable("OTEL_RESOURCE_ATTRIBUTES", resourceAttributes);
            }
        }

        [TestMethod]
        public async Task FullConfigurationSnapshotSurvivesActualSdkSerialization()
        {
            RuntimeConfig config = new(null, new(DatabaseType.MSSQL, string.Empty), new(new Dictionary<string, Entity>()));
            ImmutableDictionary<string, string> snapshot = EngineTelemetrySnapshotFactory.Create(config);
            Assert.AreEqual(82, snapshot.Count);
            RecordingHandler handler = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(
                () => CreateExporter(handler), enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);
            session.AcceptConfiguration(config);
            session.MarkHostReady();
            await session.StopAsync().WaitAsync(_testTimeout);

            JsonElement ready = handler.Requests.Select(request =>
            {
                using JsonDocument body = JsonDocument.Parse(request.Body);
                return body.RootElement.Clone();
            }).Single(envelope => envelope.GetProperty("data").GetProperty("baseData").GetProperty("name").GetString() == "dab.engine.ready");
            JsonElement properties = ready.GetProperty("data").GetProperty("baseData").GetProperty("properties");
            foreach ((string key, string value) in snapshot)
            {
                Assert.IsTrue(properties.TryGetProperty(key, out JsonElement actual), "SDK discarded snapshot property " + key);
                Assert.AreEqual(value, actual.GetString(), key);
            }

            Assert.AreEqual("true", properties.GetProperty("dab_is_synthetic").GetString());
            Assert.AreEqual("1", properties.GetProperty("dab_config_epoch").GetString());
            Assert.IsTrue(properties.TryGetProperty("dab_event_id", out _));
            Assert.IsTrue(properties.TryGetProperty("startup_ms", out _));
            Assert.IsTrue(EngineTelemetrySdkTransportHandler.HasOnlyApprovedEnvelopeTags(ready.GetRawText()));
        }

        [DataTestMethod]
        [DataRow(null, CONNECTION_STRING, null, "true", "true")]
        [DataRow("false", CONNECTION_STRING, null, "true", "true")]
        [DataRow("1", null, null, "true", "true")]
        [DataRow("1", "invalid-routing", null, "true", "true")]
        [DataRow("1", CONNECTION_STRING, "1", "true", "true")]
        [DataRow("1", CONNECTION_STRING, " true ", "true", "true")]
        [DataRow("1", CONNECTION_STRING, null, null, "true")]
        [DataRow("1", CONNECTION_STRING, null, "false", "true")]
        [DataRow("1", CONNECTION_STRING, null, "true", null)]
        [DataRow("1", CONNECTION_STRING, null, "true", "false")]
        public void MissingOrVetoedPrerequisitesLeaveStandaloneCollectionDisabled(
            string? testMode, string? connectionString, string? optOut, string? statsbeat, string? sdkStats)
        {
            string[] variables =
            [
                EngineTelemetryHosting.TEST_MODE_VARIABLE, EngineTelemetryHosting.CONNECTION_STRING_VARIABLE,
                ProductTelemetryPolicy.OPT_OUT_ENV_VAR,
                EngineTelemetryApplicationInsightsExporter.STATSBEAT_DISABLED_VARIABLE,
                EngineTelemetryApplicationInsightsExporter.SDK_STATS_DISABLED_VARIABLE
            ];
            string?[] values = [testMode, connectionString, optOut, statsbeat, sdkStats];
            Dictionary<string, string?> original = variables.ToDictionary(name => name, Environment.GetEnvironmentVariable);
            try
            {
                for (int index = 0; index < variables.Length; index++)
                {
                    Environment.SetEnvironmentVariable(variables[index], values[index]);
                }

                using EngineTelemetrySession session = EngineTelemetryHosting.CreateStandalone(stdio: true);
                Assert.IsFalse(session.IsEnabled);
                for (int index = 0; index < variables.Length; index++)
                {
                    Assert.AreEqual(values[index], Environment.GetEnvironmentVariable(variables[index]));
                }
            }
            finally
            {
                foreach ((string name, string? value) in original)
                {
                    Environment.SetEnvironmentVariable(name, value);
                }
            }
        }

        [TestMethod]
        public async Task PrivateLoggerDoesNotImportOrChangeCustomerEnvironmentAndActivity()
        {
            const string sentinel = "PRIVATE_CUSTOMER_CONTEXT_41a29";
            string[] variables =
            [
                "APPLICATIONINSIGHTS_CONNECTION_STRING",
                "APPLICATIONINSIGHTS_CLOUD_ROLE_NAME", "APPLICATIONINSIGHTS_CLOUD_ROLE_INSTANCE",
                "APPLICATIONINSIGHTS_COMPONENT_VERSION", "OTEL_RESOURCE_ATTRIBUTES", "OTEL_SERVICE_NAME"
            ];
            Dictionary<string, string?> saved = variables.ToDictionary(name => name, Environment.GetEnvironmentVariable);
            try
            {
                foreach (string variable in variables)
                {
                    Environment.SetEnvironmentVariable(variable, sentinel);
                }

                using Activity activity = new Activity(sentinel).SetIdFormat(ActivityIdFormat.W3C).Start();
                activity.TraceStateString = "private=" + sentinel;
                activity.AddBaggage("secret", sentinel);
                bool originalSuppression = Sdk.SuppressInstrumentation;
                CapturingLogExporter logExporter = new();
                using EngineTelemetryApplicationInsightsExporter exporter = new(logExporter);

                Assert.IsTrue(await exporter.ExportAsync(CreateEvent(), CancellationToken.None));
                Assert.IsFalse(logExporter.Attributes.Any(pair => Equals(pair.Value, sentinel)));
                Assert.IsFalse(logExporter.HadActivity);
                Assert.IsFalse(logExporter.HadTraceContext);
                Assert.IsFalse(logExporter.HadDiagnosticFields);
                Assert.IsTrue(logExporter.WasSuppressed);
                Assert.AreSame(activity, Activity.Current);
                Assert.AreEqual(originalSuppression, Sdk.SuppressInstrumentation);
                Assert.IsTrue(EngineTelemetryApplicationInsightsExporter.AreSdkStatisticsDisabled());
                foreach (string variable in variables)
                {
                    Assert.AreEqual(sentinel, Environment.GetEnvironmentVariable(variable));
                }
            }
            finally
            {
                foreach ((string name, string? value) in saved)
                {
                    Environment.SetEnvironmentVariable(name, value);
                }
            }
        }

        [TestMethod]
        public async Task SequentialInstancesWithIdenticalRoutingReleaseTheirTransport()
        {
            RecordingHandler firstHandler = new();
            RecordingHandler secondHandler = new();
            string routing = NewConnectionString();
            using EngineTelemetryApplicationInsightsExporter first = CreateExporter(firstHandler, routing);
            Assert.IsTrue(await first.ExportAsync(CreateEvent(), CancellationToken.None));
            first.Dispose();
            first.Dispose();

            using EngineTelemetryApplicationInsightsExporter second = CreateExporter(secondHandler, routing);
            Assert.IsFalse(await first.ExportAsync(CreateEvent(), CancellationToken.None));
            Assert.IsTrue(await second.ExportAsync(CreateEvent(), CancellationToken.None));
            Assert.AreEqual(1, firstHandler.Requests.Count);
            Assert.AreEqual(1, secondHandler.Requests.Count);
            Assert.AreEqual(1, firstHandler.DisposeCalls);
            Assert.AreEqual(0, secondHandler.DisposeCalls);
        }

        [TestMethod]
        public async Task ConcurrentSameDestinationSdkInstancesAreNotAnIsolationGuarantee()
        {
            RecordingHandler firstHandler = new();
            RecordingHandler secondHandler = new();
            string routing = NewConnectionString();
            using EngineTelemetryApplicationInsightsExporter first = CreateExporter(firstHandler, routing);
            using EngineTelemetryApplicationInsightsExporter second = CreateExporter(secondHandler, routing);
            Assert.IsTrue(await first.ExportAsync(CreateEvent(), CancellationToken.None));
            // 1.9.0 reuses the first transmitter. Its attempt guard rejects the second owner's
            // call. This characterization blocks claiming general embedded-instance isolation.
            Assert.IsFalse(await second.ExportAsync(CreateEvent(), CancellationToken.None));
            Assert.AreEqual(1, firstHandler.Requests.Count);
            Assert.AreEqual(0, secondHandler.Requests.Count);
        }

        [TestMethod]
        public async Task DisabledSdkStatisticsDoNotPublishMeasurementsDuringExport()
        {
            long measurements = 0;
            using MeterListener listener = new();
            listener.InstrumentPublished = (instrument, source) =>
            {
                if (instrument.Meter.Name.Contains("Statsbeat", StringComparison.OrdinalIgnoreCase) ||
                    instrument.Meter.Name.Contains("SdkStats", StringComparison.OrdinalIgnoreCase))
                {
                    source.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<long>((_, _, _, _) => Interlocked.Increment(ref measurements));
            listener.SetMeasurementEventCallback<int>((_, _, _, _) => Interlocked.Increment(ref measurements));
            listener.SetMeasurementEventCallback<double>((_, _, _, _) => Interlocked.Increment(ref measurements));
            listener.Start();
            RecordingHandler handler = new();
            using EngineTelemetryApplicationInsightsExporter exporter = CreateExporter(handler);
            Assert.IsTrue(await exporter.ExportAsync(CreateEvent(), CancellationToken.None));
            listener.RecordObservableInstruments();
            Assert.AreEqual(0L, measurements);
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task ChangingDestinationChangesKeyAndEndpointWithoutChangingTheEvent()
        {
            EngineTelemetryEvent record = CreateEvent();
            foreach (string endpoint in new[] { "https://temporary.synthetic.invalid/", "https://official.synthetic.invalid/" })
            {
                Guid key = Guid.NewGuid();
                RecordingHandler handler = new();
                using EngineTelemetryApplicationInsightsExporter exporter = CreateExporter(handler,
                    $"InstrumentationKey={key:D};IngestionEndpoint={endpoint}");
                Assert.IsTrue(await exporter.ExportAsync(record, CancellationToken.None));
                CapturedRequest request = handler.Requests.Single();
                Assert.AreEqual(new Uri(endpoint + "v2.1/track"), request.Uri);
                using JsonDocument envelope = JsonDocument.Parse(request.Body);
                Assert.AreEqual(key.ToString("D"), envelope.RootElement.GetProperty("iKey").GetString());
                Assert.AreEqual(record.EventId.ToString("D"), EventProperties(envelope).GetProperty("dab_event_id").GetString());
            }
        }

        [DataTestMethod]
        [DataRow(204)]
        [DataRow(206)]
        [DataRow(301)]
        [DataRow(302)]
        [DataRow(307)]
        [DataRow(308)]
        [DataRow(400)]
        [DataRow(401)]
        [DataRow(403)]
        [DataRow(429)]
        [DataRow(500)]
        [DataRow(503)]
        public async Task UnacceptedHttpStatusDoesNotRedirectOrRetryInsideExporter(int status)
        {
            RecordingHandler handler = new()
            {
                Response = _ =>
                {
                    HttpResponseMessage response = AcceptedResponse();
                    response.StatusCode = (HttpStatusCode)status;
                    response.Headers.Location = new Uri("https://not-selected.invalid/");
                    return Task.FromResult(response);
                }
            };
            using EngineTelemetryApplicationInsightsExporter exporter = CreateExporter(handler);
            Assert.IsFalse(await exporter.ExportAsync(CreateEvent(), CancellationToken.None));
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task SdkOwnsAcknowledgementOfHttp200WithoutASecondProtocolParser()
        {
            RecordingHandler handler = new()
            {
                Response = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK))
            };
            using EngineTelemetryApplicationInsightsExporter exporter = CreateExporter(handler);
            Assert.IsTrue(await exporter.ExportAsync(CreateEvent(), CancellationToken.None));
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task RetryUsesIdenticalBytesAndDoesNotInheritPreviousFailure()
        {
            RecordingHandler handler = new();
            handler.Response = _ => handler.Requests.Count == 1
                ? throw new HttpRequestException("Synthetic transport failure")
                : Task.FromResult(AcceptedResponse());
            using EngineTelemetryApplicationInsightsExporter exporter = CreateExporter(handler);
            EngineTelemetryEvent record = CreateEvent();
            Assert.IsFalse(await exporter.ExportAsync(record, CancellationToken.None));
            Assert.IsTrue(await exporter.ExportAsync(record, CancellationToken.None));
            Assert.AreEqual(2, handler.Requests.Count);
            Assert.AreEqual(handler.Requests.First().Body, handler.Requests.Last().Body);
        }

        [TestMethod]
        public async Task WorkerRetriesAsyncTransportFailureWithoutDuplicatingTheEvent()
        {
            RecordingHandler handler = new();
            handler.Response = _ => Task.FromResult(handler.Requests.Count == 1
                ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : AcceptedResponse());
            using EngineTelemetryDelivery delivery = new(() => CreateExporter(handler));
            Assert.IsTrue(delivery.TryEnqueue(CreateEvent()));
            await delivery.StopAsync().WaitAsync(_testTimeout);
            Assert.AreEqual(2, handler.Requests.Count);
            Assert.AreEqual(handler.Requests.First().Body, handler.Requests.Last().Body);
            Assert.AreEqual(0L, delivery.DroppedEvents);
            Assert.AreEqual(1, handler.DisposeCalls);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task CancellationReachesHttpAndResponseBodyWithoutBlockingTheCaller(bool duringBody)
        {
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            RecordingHandler handler = new()
            {
                Response = async token =>
                {
                    if (duringBody)
                    {
                        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new BlockingContent(entered, canceled) };
                    }

                    entered.TrySetResult();
                    try
                    {
                        await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    }
                    finally
                    {
                        canceled.TrySetResult();
                    }

                    return AcceptedResponse();
                }
            };
            using EngineTelemetryApplicationInsightsExporter exporter = CreateExporter(handler);
            using CancellationTokenSource cancellation = new();
            // Public SDK Export is synchronous. Test it on the same kind of worker boundary
            // used by production, so this thread can request cancellation while HTTP waits.
            Task<bool> send = Task.Run(async () => await exporter.ExportAsync(CreateEvent(), cancellation.Token));
            await entered.Task.WaitAsync(_testTimeout);
            Assert.IsFalse(send.IsCompleted);
            cancellation.Cancel();
            Assert.IsFalse(await send.WaitAsync(_testTimeout));
            await canceled.Task.WaitAsync(_testTimeout);
            Assert.IsFalse(await exporter.ExportAsync(CreateEvent(), cancellation.Token));
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task OneSecondBudgetAppliesWithoutCallerCancellation()
        {
            RecordingHandler handler = new()
            {
                Response = async token =>
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return AcceptedResponse();
                }
            };
            using EngineTelemetryApplicationInsightsExporter exporter = CreateExporter(handler);
            Stopwatch elapsed = Stopwatch.StartNew();
            Assert.IsFalse(await exporter.ExportAsync(CreateEvent(), CancellationToken.None).AsTask().WaitAsync(_testTimeout));
            Assert.IsTrue(elapsed.Elapsed < TimeSpan.FromSeconds(5));
        }

        [TestMethod]
        public async Task OversizedPayloadAndResponseAreRejectedWithoutUnboundedBuffering()
        {
            RecordingHandler handler = new();
            using EngineTelemetryApplicationInsightsExporter exporter = CreateExporter(handler);
            EngineTelemetryEvent tooLarge = CreateEvent() with
            {
                Properties = Enumerable.Range(0, 12).ToImmutableDictionary(i => "property" + i, _ => new string('x', 8192))
            };
            Assert.IsFalse(await exporter.ExportAsync(tooLarge, CancellationToken.None));
            Assert.AreEqual(0, handler.Requests.Count);
            handler.Response = _ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(new string('x', 20 * 1024))
            });
            Assert.IsFalse(await exporter.ExportAsync(CreateEvent(), CancellationToken.None));
            Assert.AreEqual(1, handler.Requests.Count);
        }

        [TestMethod]
        public async Task StartupFailureLifecycleReachesTransportAsThreeDistinctEvents()
        {
            RecordingHandler handler = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(
                () => CreateExporter(handler), enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null, showNotice: () => { }, startTimer: false);
            session.StartupFailed();
            await session.StopAsync();
            string[] names = handler.Requests.Select(request =>
            {
                using JsonDocument body = JsonDocument.Parse(request.Body);
                return body.RootElement.GetProperty("data").GetProperty("baseData").GetProperty("name").GetString()!;
            }).ToArray();
            CollectionAssert.AreEqual(new[] { "dab.engine.process_started", "dab.engine.startup_failed", "dab.engine.stopped" }, names);
        }

        private static EngineTelemetryApplicationInsightsExporter CreateExporter(RecordingHandler handler, string? connectionString = null)
        {
            Assert.IsTrue(ApplicationInsightsTelemetryDestination.TryParse(connectionString ?? NewConnectionString(), out ApplicationInsightsTelemetryDestination? destination));
            return new(destination, handler);
        }

        private static string NewConnectionString() => $"InstrumentationKey={Guid.NewGuid():D};IngestionEndpoint=https://synthetic.invalid/";

        private static EngineTelemetryEvent CreateEvent() => new(
            Guid.NewGuid(), Guid.NewGuid(), 17,
            new DateTimeOffset(2026, 9, 21, 10, 11, 12, TimeSpan.FromMinutes(330)).AddTicks(3456789),
            4, "dab.engine.usage_summary", ImmutableDictionary<string, string>.Empty.Add("api", "rest").Add("count", "2"));

        private static JsonElement EventProperties(JsonDocument body)
            => body.RootElement.GetProperty("data").GetProperty("baseData").GetProperty("properties");

        private static HttpResponseMessage AcceptedResponse() => new(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"itemsReceived\":1,\"itemsAccepted\":1,\"errors\":[]}", Encoding.UTF8, "application/json")
        };

        private sealed record CapturedRequest(Uri Uri, string Body, Dictionary<string, string> Headers,
            bool InstrumentationSuppressed, bool HadActivity, bool TokenCanBeCanceled);

        private sealed class CapturingLogExporter : BaseExporter<LogRecord>
        {
            internal KeyValuePair<string, object?>[] Attributes { get; private set; } = [];
            internal bool HadActivity { get; private set; }
            internal bool HadTraceContext { get; private set; }
            internal bool HadDiagnosticFields { get; private set; }
            internal bool WasSuppressed { get; private set; }

            public override ExportResult Export(in Batch<LogRecord> batch)
            {
                foreach (LogRecord record in batch)
                {
                    Attributes = record.Attributes!.ToArray();
                    HadActivity = Activity.Current is not null;
                    HadTraceContext = record.TraceId != default || record.SpanId != default || record.TraceState is not null;
                    // OTel can represent a cleared diagnostic string as null or string.Empty.
                    HadDiagnosticFields = !string.IsNullOrEmpty(record.Body) || !string.IsNullOrEmpty(record.FormattedMessage) ||
                        !string.IsNullOrEmpty(record.CategoryName) || record.Exception is not null || record.EventId.Id != 0;
                    WasSuppressed = Sdk.SuppressInstrumentation;
                }

                return ExportResult.Success;
            }
        }

        private sealed class RecordingHandler : HttpMessageHandler
        {
            internal ConcurrentQueue<CapturedRequest> Requests { get; } = new();
            internal Func<CancellationToken, Task<HttpResponseMessage>> Response { get; set; } = _ => Task.FromResult(AcceptedResponse());
            internal int DisposeCalls { get; private set; }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                string body = await request.Content!.ReadAsStringAsync(cancellationToken);
                Dictionary<string, string> headers = request.Headers.Concat(request.Content.Headers)
                    .ToDictionary(pair => pair.Key, pair => string.Join(",", pair.Value), StringComparer.OrdinalIgnoreCase);
                Requests.Enqueue(new(request.RequestUri!, body, headers,
                    Sdk.SuppressInstrumentation, Activity.Current is not null, cancellationToken.CanBeCanceled));
                return await Response(cancellationToken);
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

        private sealed class BlockingContent(TaskCompletionSource entered, TaskCompletionSource canceled) : HttpContent
        {
            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return false;
            }

            protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
                => SerializeToStreamAsync(stream, context, CancellationToken.None);

            protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
            {
                entered.TrySetResult();
                try
                {
                    await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                }
                finally
                {
                    canceled.TrySetResult();
                }
            }
        }
    }
}
