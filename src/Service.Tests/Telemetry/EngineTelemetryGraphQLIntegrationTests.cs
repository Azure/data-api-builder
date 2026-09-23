// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Service.Telemetry;
using HotChocolate;
using HotChocolate.Execution;
using HotChocolate.Types;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Executes the installed HC16 pipeline with the real product listener and enabled session.
    /// Only the exporter, clock, identity, notice and HTTP completion feature are in-memory fakes;
    /// no DAB host, database, identity file or cloud destination is required.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    public class EngineTelemetryGraphQLIntegrationTests
    {
        private const string SENTINEL = "GRAPHQL_PRIVATE_INPUT_56b98";
        private const string VARIABLE_QUERY = "query Variables($value: String!, $fail: Boolean!) { echo(value: $value) fail @include(if: $fail) }";
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

        [DataTestMethod]
        [DataRow("{ first: echo(value: \"first\") second: echo(value: \"second\") }", 2)]
        [DataRow("query IntrospectionQuery { __schema: echo(value: \"__typename\") }", 1)]
        public async Task LogicalSuccessCountsOneRequestRatherThanFieldsOrOperationNames(string document, int fields)
        {
            await using Fixture fixture = new();
            await using IExecutionResult result = await fixture.ExecuteAsync(OperationRequestBuilder.New().SetDocument(document));
            Assert.AreEqual(0, result.ExpectOperationResult().Errors.Count);
            Assert.AreEqual(fields, fixture.ResolverRequests.Count);
            EngineTelemetryRequestScope request = ObservedRequest(fixture);
            Assert.IsTrue(request.IsEligible);
            Assert.IsTrue(request.IsCompleted);
            Assert.IsTrue(fixture.ResumedRequests.All(resumed => ReferenceEquals(request, resumed)),
                "The real session scope must flow through asynchronous resolver work.");
            Assert.IsNull(fixture.Session.CurrentRequest);

            EngineTelemetryEvent[] records = await fixture.StopAsync();
            AssertRequests(records, success: 1);
            Assert.AreEqual(1, records.Count(record => record.Name == "dab.engine.first_request_served"));
            Assert.AreEqual(1, records.Count(record => record.Name == "dab.engine.first_successful_request"));
            Assert.AreEqual(0, Summaries(records, "http_outcome").Length);
        }

        [TestMethod]
        public async Task LogicalPartialFailureUsesTheRealOperationResult()
        {
            await using Fixture fixture = new();
            await using IExecutionResult result = await fixture.ExecuteAsync(OperationRequestBuilder.New()
                .SetDocument("{ echo(value: \"" + SENTINEL + "\") fail }"));
            OperationResult operation = result.ExpectOperationResult();
            Assert.AreEqual(1, operation.Errors.Count);
            Assert.IsTrue(operation.Data is { IsValueNull: false });

            EngineTelemetryEvent[] records = await fixture.StopAsync();
            AssertRequests(records, partialFailure: 1);
            Assert.AreEqual(0, records.Count(record => record.Name == "dab.engine.first_successful_request"));
        }

        [TestMethod]
        public async Task ValidationFailureCountsWithoutExecutingResolvers()
        {
            await using Fixture fixture = new();
            await using IExecutionResult result = await fixture.ExecuteAsync(OperationRequestBuilder.New()
                .SetDocument("{ echo }"));
            Assert.IsTrue(result.ExpectOperationResult().Errors.Count > 0);
            Assert.AreEqual(0, fixture.ResolverRequests.Count);
            Assert.IsNull(fixture.Session.CurrentRequest);
            AssertRequests(await fixture.StopAsync(), failure: 1);
        }

        [DataTestMethod]
        [DataRow("{ __typename }", null)]
        [DataRow("{ echo: __schema { queryType { name } } }", null)]
        [DataRow("query echo { ...Info } fragment Info on Query { __type(name: \"echo\") { name } }", "echo")]
        [DataRow("query Data { echo(value: \"unused\") } query Discovery { __typename }", "Discovery")]
        [DataRow("{ __typename } # echo(value: \"not executed\")", null)]
        public async Task IntrospectionIsExcludedRegardlessOfAliasesNamesAndSourceText(string document, string? operationName)
        {
            await using Fixture fixture = new();
            await using IExecutionResult result = await fixture.ExecuteAsync(OperationRequestBuilder.New()
                .SetDocument(document).SetOperationName(operationName));
            Assert.AreEqual(0, result.ExpectOperationResult().Errors.Count);
            Assert.AreEqual(0, fixture.ResolverRequests.Count);
            Assert.IsNull(fixture.Session.CurrentRequest);

            EngineTelemetryEvent[] records = await fixture.StopAsync();
            AssertRequests(records);
            Assert.AreEqual(0, records.Count(record => record.Name == "dab.engine.first_request_served"));
        }

        [DataTestMethod]
        [DataRow("query echo { echo(value:")]
        [DataRow("query Data { __type(name: \"echo\") {")]
        public async Task MalformedDocumentPreservesTheParserFailureWithoutInventingUsage(string document)
        {
            // Compare the actual parser error to HC without product instrumentation, rather than
            // accepting any error (which could conceal a telemetry-induced NullReferenceException).
            ServiceCollection services = new();
            services.AddGraphQL().AddQueryType(descriptor => descriptor.Name("Query")
                .Field("echo").Type<StringType>().Resolve(_ => "unused"));
            await using ServiceProvider baselineProvider = services.BuildServiceProvider();
            IRequestExecutor baseline = await baselineProvider.GetRequestExecutorAsync();
            await using IExecutionResult baselineResult = await baseline.ExecuteAsync(OperationRequestBuilder.New().SetDocument(document).Build());

            await using Fixture fixture = new();
            await using IExecutionResult result = await fixture.ExecuteAsync(OperationRequestBuilder.New().SetDocument(document));
            OperationResult expected = baselineResult.ExpectOperationResult();
            OperationResult actual = result.ExpectOperationResult();
            Assert.IsTrue(actual.Errors.Count > 0);
            CollectionAssert.AreEqual(expected.Errors.Select(error => error.Code).ToArray(), actual.Errors.Select(error => error.Code).ToArray());
            CollectionAssert.AreEqual(expected.Errors.Select(error => error.Message).ToArray(), actual.Errors.Select(error => error.Message).ToArray());
            Assert.IsNull(actual.Data);
            Assert.AreEqual(0, fixture.ResolverRequests.Count);
            Assert.IsNull(fixture.Session.CurrentRequest);
            AssertRequests(await fixture.StopAsync());
        }

        [TestMethod]
        public async Task HttpBatchCountsEachDataExecutionOnlyAfterResponseCompletion()
        {
            await using Fixture fixture = new();
            IRequestExecutor executor = await fixture.GetExecutorAsync();
            (DefaultHttpContext context, ResponseCallbacks response) = CreateHttpContext();
            using OperationRequestBatch batch = new(new[]
            {
                OperationRequestBuilder.New().SetDocument("{ echo(value: \"first\") }").SetGlobalState(nameof(HttpContext), context).Build(),
                OperationRequestBuilder.New().SetDocument("{ echo(value: \"second\") fail }").SetGlobalState(nameof(HttpContext), context).Build(),
                OperationRequestBuilder.New().SetDocument("{ echo: __typename }").SetGlobalState(nameof(HttpContext), context).Build()
            });
            int results = 0;
            int errors = 0;
            await using (IResponseStream stream = await executor.ExecuteBatchAsync(batch))
            {
                await foreach (OperationResult operation in stream.ReadResultsAsync())
                {
                    await using (operation)
                    {
                        results++;
                        errors += operation.Errors.Count;
                    }
                }
            }

            Assert.AreEqual(3, results);
            Assert.AreEqual(1, errors);
            Assert.AreEqual(2, response.Count, "The discovery-only execution must not register a data completion.");
            Assert.AreEqual(2, fixture.ResolverRequests.Distinct().Count());
            Assert.IsTrue(fixture.ResolverRequests.All(request => request is { IsCompleted: false }));
            await fixture.AssertNoRequestTelemetryAsync();
            await response.CompleteAsync();
            await response.CompleteAsync();
            Assert.IsTrue(fixture.ResolverRequests.All(request => request is { IsCompleted: true }));
            Assert.IsNull(fixture.Session.CurrentRequest);

            EngineTelemetryEvent[] records = await fixture.StopAsync();
            AssertRequests(records, success: 1, partialFailure: 1, transport: "http");
            AssertHttpOutcomes(records, 2, "success");
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task VariableBatchCountsEachResultAndNeverCompletesTheOriginal(bool http)
        {
            await using Fixture fixture = new();
            (DefaultHttpContext context, ResponseCallbacks response) = CreateHttpContext();
            await using (IExecutionResult result = await fixture.ExecuteAsync(VariableRequest(http ? context : null)))
            {
                AssertVariableResults(result, partialFailures: 1);
            }

            Assert.AreEqual(3, fixture.ResolverRequests.Count);
            EngineTelemetryRequestScope original = ObservedRequest(fixture);
            Assert.IsFalse(original.IsCompleted, "Only forked per-execution scopes may complete a variable batch.");
            Assert.IsNull(fixture.Session.CurrentRequest);
            if (http)
            {
                Assert.AreEqual(3, response.Count);
                await fixture.AssertNoRequestTelemetryAsync();
                // Let HC reuse its pooled context and dispose the new result before invoking old
                // callbacks. Neither a context nor a raw batch result may be read at completion.
                await using (IExecutionResult discovery = await fixture.ExecuteAsync(OperationRequestBuilder.New().SetDocument("{ __typename }")))
                {
                    Assert.AreEqual(0, discovery.ExpectOperationResult().Errors.Count);
                }

                await response.CompleteAsync();
                await response.CompleteAsync();
            }

            EngineTelemetryEvent[] records = await fixture.StopAsync();
            AssertRequests(records, success: 2, partialFailure: 1, transport: http ? "http" : "in_process", role: "custom");
            Assert.IsFalse(original.IsCompleted);
            Assert.AreEqual(1, records.Count(record => record.Name == "dab.engine.first_request_served"));
            Assert.AreEqual(1, records.Count(record => record.Name == "dab.engine.first_successful_request"));
            if (http)
            {
                AssertHttpOutcomes(records, 3, "success");
            }
            else
            {
                Assert.AreEqual(0, Summaries(records, "http_outcome").Length);
            }
        }

        [TestMethod]
        public async Task VariableBatchKeepsItsCapturedConfigurationEpochAndStartAcrossReload()
        {
            await using Fixture fixture = new();
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
            fixture.EchoGate = () =>
            {
                // Do not assume HC dispatches variable sets concurrently. The first resolver
                // is enough to establish the old request capture before reloading configuration.
                entered.TrySetResult();
                return release.Task;
            };

            Task<IExecutionResult> execution = fixture.ExecuteAsync(VariableRequest(includePartialFailure: false));
            try
            {
                await entered.Task.WaitAsync(_timeout);
                EngineTelemetryRequestScope captured = ObservedRequest(fixture);
                Assert.AreSame(fixture.InitialConfiguration, captured.Config);
                Assert.AreEqual(1L, captured.Configuration.Epoch);
                fixture.Clock.Advance(TimeSpan.FromMilliseconds(250));
                fixture.Session.AcceptConfiguration(CreateConfig(DatabaseType.PostgreSQL), "hot_reload");
                fixture.Clock.Advance(TimeSpan.FromMilliseconds(750));
                Assert.IsFalse(execution.IsCompleted);
            }
            finally
            {
                release.TrySetResult();
            }

            await using (IExecutionResult result = await execution.WaitAsync(_timeout))
            {
                AssertVariableResults(result, partialFailures: 0);
            }

            Assert.AreEqual(3, fixture.ResolverRequests.Count);
            EngineTelemetryRequestScope original = ObservedRequest(fixture);
            Assert.IsFalse(original.IsCompleted);
            Assert.IsTrue(fixture.ResumedRequests.All(request => ReferenceEquals(original, request)));
            fixture.EchoGate = null;
            await using (IExecutionResult nextEpoch = await fixture.ExecuteAsync(OperationRequestBuilder.New().SetDocument("{ echo(value: \"new epoch\") }")))
            {
                Assert.AreEqual(0, nextEpoch.ExpectOperationResult().Errors.Count);
            }

            Assert.IsNull(fixture.Session.CurrentRequest);
            EngineTelemetryEvent[] records = await fixture.StopAsync();
            AssertRequests(records, success: 3, role: "custom", epoch: 1);
            AssertRequests(records, success: 1, epoch: 2);
            EngineTelemetryEvent oldEpoch = Summaries(records, "request", epoch: 1).Single();
            CollectionAssert.AreEqual(new long[] { 0, 0, 0, 0, 0, 0, 3, 0, 0, 0 },
                JsonSerializer.Deserialize<long[]>(oldEpoch.Properties["latency_buckets"])!,
                "Each fork must retain the pre-reload start, yielding a 1,000 ms duration rather than zero.");
            Assert.AreEqual(2L, records.Single(record => record.Name == "dab.engine.configuration_changed").ConfigurationEpoch);
        }

        [TestMethod]
        public async Task VariableBatchHttpAbortCompletesEachForkOnceWithoutAStatus()
        {
            await using Fixture fixture = new();
            using CancellationTokenSource cancellation = new();
            (DefaultHttpContext context, ResponseCallbacks response) = CreateHttpContext(cancellation.Token);
            await using (IExecutionResult result = await fixture.ExecuteAsync(VariableRequest(context)))
            {
                AssertVariableResults(result, partialFailures: 1);
            }

            Assert.AreEqual(3, response.Count);
            cancellation.Cancel();
            await response.CompleteAsync();
            await response.CompleteAsync();
            EngineTelemetryEvent[] records = await fixture.StopAsync();
            AssertRequests(records, canceled: 3, transport: "http", role: "custom");
            AssertHttpOutcomes(records, 3, "unknown");
            Assert.IsFalse(ObservedRequest(fixture).IsCompleted);
            Assert.AreEqual(0, records.Count(record => record.Name == "dab.engine.first_successful_request"));
        }

        private static OperationRequestBuilder VariableRequest(HttpContext? context = null, bool includePartialFailure = true)
        {
            // Identical first/third variable sets must still count as separate actual executions.
            IReadOnlyList<IReadOnlyDictionary<string, object?>> variables = new IReadOnlyDictionary<string, object?>[]
            {
                new Dictionary<string, object?> { ["value"] = SENTINEL, ["fail"] = false },
                new Dictionary<string, object?> { ["value"] = SENTINEL + "_second", ["fail"] = includePartialFailure },
                new Dictionary<string, object?> { ["value"] = SENTINEL, ["fail"] = false }
            };
            OperationRequestBuilder builder = OperationRequestBuilder.New().SetDocument(VARIABLE_QUERY)
                .SetVariableValues(variables).SetGlobalState(AuthorizationResolver.CLIENT_ROLE_HEADER, SENTINEL + "_role");
            if (context is not null)
            {
                context.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = SENTINEL + "_role";
                builder.SetGlobalState(nameof(HttpContext), context);
            }

            return builder;
        }

        private static void AssertVariableResults(IExecutionResult result, int partialFailures)
        {
            OperationResultBatch batch = result.ExpectOperationResultBatch();
            Assert.AreEqual(3, batch.Results.Count, "This must exercise HC's variable-batch result, not three independent requests.");
            OperationResult[] operations = batch.Results.Select(item => item.ExpectOperationResult()).ToArray();
            Assert.AreEqual(partialFailures, operations.Count(operation => operation.Errors.Count > 0));
            Assert.IsTrue(operations.All(operation => operation.Data is { IsValueNull: false }));
        }

        private static EngineTelemetryRequestScope ObservedRequest(Fixture fixture)
        {
            Assert.IsTrue(fixture.ResolverRequests.Count > 0);
            EngineTelemetryRequestScope? request = fixture.ResolverRequests.Distinct().Single();
            Assert.IsNotNull(request, "Resolvers must observe the real enabled product scope.");
            return request;
        }

        private static void AssertRequests(EngineTelemetryEvent[] records, long success = 0, long failure = 0,
            long partialFailure = 0, long canceled = 0, string transport = "in_process", string role = "anonymous", long? epoch = null)
        {
            EngineTelemetryEvent[] summaries = Summaries(records, "request", epoch);
            long count = success + failure + partialFailure + canceled;
            Assert.AreEqual(count, summaries.Sum(record => Counter(record, "count")));
            Assert.AreEqual(success, summaries.Sum(record => Counter(record, "success")));
            Assert.AreEqual(failure, summaries.Sum(record => Counter(record, "failure")));
            Assert.AreEqual(partialFailure, summaries.Sum(record => Counter(record, "partial_failure")));
            Assert.AreEqual(canceled, summaries.Sum(record => Counter(record, "canceled")));
            Assert.AreEqual(0L, summaries.Sum(record => Counter(record, "unknown")));
            Assert.AreEqual(count, summaries.Sum(record => Counter(record, "timed_count")));
            Assert.IsTrue(summaries.All(record => record.Properties["api"] == "graph_ql"
                && record.Properties["transport"] == transport && record.Properties["role_class"] == role));
            Assert.IsFalse(JsonSerializer.Serialize(records).Contains(SENTINEL, StringComparison.Ordinal),
                "Variables, response values, error messages and custom role names must not enter product events.");
        }

        private static void AssertHttpOutcomes(EngineTelemetryEvent[] records, long count, string statusClass)
        {
            EngineTelemetryEvent[] summaries = Summaries(records, "http_outcome");
            Assert.AreEqual(count, summaries.Sum(record => Counter(record, "count")));
            Assert.IsTrue(summaries.All(record => record.Properties["api"] == "graph_ql"
                && record.Properties["http_status_class"] == statusClass));
        }

        private static long Counter(EngineTelemetryEvent record, string name) => long.Parse(record.Properties[name], CultureInfo.InvariantCulture);

        private static EngineTelemetryEvent[] Summaries(IEnumerable<EngineTelemetryEvent> records, string family, long? epoch = null) => records
            .Where(record => record.Name == "dab.engine.usage_summary" && record.Properties["family"] == family
                && (epoch is null || record.ConfigurationEpoch == epoch)).ToArray();

        private static RuntimeConfig CreateConfig(DatabaseType databaseType = DatabaseType.MSSQL) => new(
            Schema: null,
            DataSource: new DataSource(databaseType, string.Empty),
            Entities: new RuntimeEntities(new Dictionary<string, Entity>()),
            Runtime: new RuntimeOptions(new RestRuntimeOptions(Enabled: false), new GraphQLRuntimeOptions(Enabled: true),
                new McpRuntimeOptions(Enabled: false), Host: null));

        private static (DefaultHttpContext Context, ResponseCallbacks Response) CreateHttpContext(CancellationToken aborted = default)
        {
            ResponseCallbacks response = new();
            DefaultHttpContext context = new() { RequestAborted = aborted };
            context.Features.Set<IHttpResponseFeature>(response);
            return (context, response);
        }

        private sealed class Fixture : IAsyncDisposable
        {
            private readonly ServiceProvider _provider;
            internal CapturingExporter Exporter { get; } = new();
            internal ManualClock Clock { get; } = new();
            internal RuntimeConfig InitialConfiguration { get; } = CreateConfig();
            internal EngineTelemetrySession Session { get; }
            internal ConcurrentQueue<EngineTelemetryRequestScope?> ResolverRequests { get; } = new();
            internal ConcurrentQueue<EngineTelemetryRequestScope?> ResumedRequests { get; } = new();
            internal Func<Task>? EchoGate { get; set; }

            internal Fixture()
            {
                Session = EngineTelemetrySession.Create(() => Exporter, enableSyntheticCollection: true,
                    clock: Clock, readEnvironmentVariable: _ => null, showNotice: () => { },
                    resolveIdentity: _ => new(new Guid("c4bd0757-e68a-4af6-ab1c-05e86e08f489"), "ephemeral"), startTimer: false);
                Session.AcceptConfiguration(InitialConfiguration);
                Session.MarkHostReady();
                Assert.IsTrue(Session.IsEnabled);
                Assert.IsTrue(Session.IsReady);

                // Capture this session explicitly: HC has its own schema service provider and
                // must not resolve a second product session or a diagnostic-scope stand-in.
                EngineTelemetrySession session = Session;
                ServiceCollection services = new();
                services.AddGraphQL().AddQueryType(descriptor =>
                {
                    descriptor.Name("Query");
                    descriptor.Field("echo").Argument("value", argument => argument.Type<NonNullType<StringType>>())
                        .Type<StringType>().Resolve(async context =>
                        {
                            ResolverRequests.Enqueue(session.CurrentRequest);
                            if (EchoGate is Func<Task> gate)
                            {
                                await gate();
                            }

                            await Task.Yield();
                            ResumedRequests.Enqueue(session.CurrentRequest);
                            return context.ArgumentValue<string>("value");
                        });
                    descriptor.Field("fail").Type<StringType>().Resolve(context =>
                    {
                        context.ReportError(ErrorBuilder.New().SetMessage(SENTINEL + "_error").Build());
                        return (string?)null;
                    });
                }).AddDiagnosticEventListener(_ => new EngineTelemetryGraphQLListener(session));
                _provider = services.BuildServiceProvider();
            }

            internal async Task<IRequestExecutor> GetExecutorAsync() => await _provider.GetRequestExecutorAsync();

            internal async Task<IExecutionResult> ExecuteAsync(OperationRequestBuilder builder)
            {
                IRequestExecutor executor = await GetExecutorAsync();
                using IOperationRequest request = builder.Build();
                return await executor.ExecuteAsync(request);
            }

            internal async Task AssertNoRequestTelemetryAsync()
            {
                // The exporter is asynchronous. A later heartbeat is an ordered barrier, unlike
                // observing an empty queue that might merely reflect an unscheduled worker.
                Clock.Advance(TimeSpan.FromMinutes(10));
                Session.Tick();
                await Exporter.Heartbeat.Task.WaitAsync(_timeout);
                EngineTelemetryEvent[] records = Exporter.Records.ToArray();
                Assert.AreEqual(0, Summaries(records, "request").Length);
                Assert.AreEqual(0, Summaries(records, "http_outcome").Length);
                Assert.AreEqual(0, records.Count(record => record.Name == "dab.engine.first_request_served"));
            }

            internal async Task<EngineTelemetryEvent[]> StopAsync()
            {
                Assert.IsTrue(Session.IsEnabled, "Protocol instrumentation must not disable the test session.");
                Assert.IsTrue(Session.IsReady);
                await Session.StopAsync();
                EngineTelemetryEvent[] records = Exporter.Records.ToArray();
                Assert.AreEqual(1, records.Count(record => record.Name == "dab.engine.ready"));
                Assert.AreEqual(1, records.Count(record => record.Name == "dab.engine.stopped"));
                return records;
            }

            public async ValueTask DisposeAsync()
            {
                await Session.StopAsync();
                await _provider.DisposeAsync();
                Session.Dispose();
            }
        }

        private sealed class CapturingExporter : IEngineTelemetryExporter
        {
            internal ConcurrentQueue<EngineTelemetryEvent> Records { get; } = new();
            internal TaskCompletionSource Heartbeat { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Records.Enqueue(record);
                if (record.Name == "dab.engine.heartbeat")
                {
                    Heartbeat.TrySetResult();
                }

                return ValueTask.FromResult(true);
            }

            public void Dispose() { }
        }

        private sealed class ManualClock : TimeProvider
        {
            private long _timestamp;
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
            public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());
            internal void Advance(TimeSpan duration) => Interlocked.Add(ref _timestamp, duration.Ticks);
        }

        private sealed class ResponseCallbacks : IHttpResponseFeature
        {
            private readonly ConcurrentStack<(Func<object, Task> Callback, object State)> _completed = new();
            internal int Count => _completed.Count;
            public int StatusCode { get; set; } = 200;
            public string? ReasonPhrase { get; set; }
            public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
            public Stream Body { get; set; } = Stream.Null;
            public bool HasStarted => false;
            public void OnStarting(Func<object, Task> callback, object state) { }
            public void OnCompleted(Func<object, Task> callback, object state) => _completed.Push((callback, state));

            internal async Task CompleteAsync()
            {
                // Deliberately retain callbacks so repeated completion exercises exactly-once guards.
                foreach ((Func<object, Task> callback, object state) in _completed)
                {
                    await callback(state);
                }
            }
        }
    }
}
