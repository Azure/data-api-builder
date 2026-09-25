// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>
    /// Exercises the real MCP wrapper, stdio server, DI scopes and enabled product session.
    /// Tool execution, config files, output, exporter, clock and identity are in-memory fakes.
    /// Mocked built-in names do not execute ReadRecordsTool or SQL; database-attempt scopes below
    /// are synthetic observations, not evidence of actual commands or database retries.
    /// </summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    public class EngineTelemetryMcpIntegrationTests
    {
        private const string SENTINEL = "MCP_PRIVATE_INPUT_31ae82";
        private const string ENTITY = SENTINEL + "_entity";
        private const string FIRST_SERVED = "dab.engine.first_request_served";
        private const string FIRST_SUCCESS = "dab.engine.first_successful_request";
        private const string SUMMARY = "dab.engine.usage_summary";
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);
        private static readonly Guid _apiId = new("cb11c0e5-b94b-455a-9ab6-712130f51af1");

        [TestMethod]
        public async Task StdioServerCountsRequestOnlyAfterItsActualWriterReturns()
        {
            await using Fixture fixture = new();
            TaskCompletionSource writerEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource releaseWriter = new(TaskCreationOptions.RunContinuationsAsynchronously);
            EngineTelemetryRequestScope? observed = null;
            HttpContext? authorizationContext = null;
            Mock<IMcpTool> tool = fixture.RegisterTool("read_records", ToolType.BuiltIn, (_, services, _) =>
            {
                observed = ObserveRequest(fixture, services);
                authorizationContext = services.GetRequiredService<IHttpContextAccessor>().HttpContext;
                Assert.IsNotNull(authorizationContext, "The real stdio role shim must be exercised.");
                Assert.AreEqual("anonymous", authorizationContext.Request.Headers["X-MS-API-ROLE"].ToString());
                // This is an authorization context, not an HTTP response. Poison its status so
                // an accidental HTTP completion path cannot pass as a successful stdio request.
                authorizationContext.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return Task.FromResult(ToolResult());
            });
            fixture.Output.BeforeReturn = _ =>
            {
                writerEntered.TrySetResult();
                releaseWriter.Task.WaitAsync(_timeout).GetAwaiter().GetResult();
            };

            // McpStdoutWriter.WriteLine is synchronous. Block its injected StringWriter on a
            // worker, not the test thread, without redirecting either process console stream.
            Task running = Task.Run(() => fixture.RunAsync(ToolCall(1)));
            try
            {
                await writerEntered.Task.WaitAsync(_timeout);
                Assert.IsNotNull(observed);
                Assert.IsFalse(observed.IsCompleted);
                Assert.AreEqual(EngineTelemetryOutcome.Success, observed.Outcome);
                Assert.IsFalse(running.IsCompleted);
                AssertToolResponse(fixture.ReadResponses().Single(), 1, isError: false);

                // Seal an actual aggregation window and wait for the ordered exporter barrier.
                // An empty exporter queue alone would not prove that nothing was counted.
                EngineTelemetryEvent[] beforeWriteReturned = await fixture.CheckpointAsync();
                AssertNoRequestTelemetry(beforeWriteReturned);
                AssertCounts(Summaries(beforeWriteReturned, "operation"), success: 1);
                Assert.IsFalse(observed.IsCompleted);
            }
            finally
            {
                releaseWriter.TrySetResult();
                await running.WaitAsync(_timeout);
            }

            Assert.IsNotNull(observed);
            Assert.IsTrue(observed.IsCompleted);
            Assert.AreEqual(EngineTelemetryOutcome.Success, observed.Outcome);
            Assert.IsNotNull(authorizationContext);
            Assert.AreEqual(StatusCodes.Status503ServiceUnavailable, authorizationContext.Response.StatusCode);
            VerifyExecutions(tool, 1);
            AssertAmbientCleared(fixture);
            EngineTelemetryEvent[] records = await fixture.StopAsync();
            AssertRequests(records, success: 1);
            AssertCounts(Summaries(records, "operation"), success: 1);
            Assert.AreEqual(1, records.Count(record => record.Name == FIRST_SERVED));
            Assert.AreEqual(1, records.Count(record => record.Name == FIRST_SUCCESS));
            AssertFamilies(records, "operation", "request");
        }

        [TestMethod]
        public async Task StdioWrapperWithoutWriterDoesNotCountAServedRequest()
        {
            await using Fixture fixture = new();
            EngineTelemetryRequestScope? observed = null;
            CallToolResult expected = ToolResult();
            Mock<IMcpTool> tool = fixture.RegisterTool("read_records", ToolType.BuiltIn, (_, services, _) =>
            {
                observed = ObserveRequest(fixture, services);
                return Task.FromResult(expected);
            });
            using IServiceScope scope = fixture.Services.CreateScope();
            using JsonDocument arguments = ToolArguments();

            CallToolResult actual = await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                tool.Object, "read_records", arguments, scope.ServiceProvider, CancellationToken.None);

            Assert.AreSame(expected, actual);
            Assert.IsNotNull(observed);
            Assert.IsFalse(observed.IsCompleted, "Configured stdio cannot fall back to in-process completion.");
            Assert.AreEqual(EngineTelemetryOutcome.Success, observed.Outcome);
            Assert.AreEqual(string.Empty, fixture.Output.ToString());
            VerifyExecutions(tool, 1);
            AssertAmbientCleared(fixture);
            EngineTelemetryEvent[] records = await fixture.StopAsync();
            AssertNoRequestTelemetry(records);
            AssertCounts(Summaries(records, "operation"), success: 1);
            AssertFamilies(records, "operation");
        }

        [TestMethod]
        public async Task StdioServerExcludesDiscoveryControlAndRejectedToolCalls()
        {
            await using Fixture fixture = new();
            Mock<IMcpTool> dataTool = fixture.RegisterTool("read_records", ToolType.BuiltIn,
                (_, _, _) => Task.FromResult(ToolResult()));
            Mock<IMcpTool> discoveryTool = fixture.RegisterTool("describe_entities", ToolType.BuiltIn, (_, services, _) =>
            {
                Assert.AreSame(fixture.Session, services.GetRequiredService<EngineTelemetrySession>());
                Assert.IsNotNull(services.GetRequiredService<IHttpContextAccessor>().HttpContext);
                Assert.IsNull(fixture.Session.CurrentRequest);
                Assert.IsNull(fixture.Session.BeginOperation(ENTITY, EngineTelemetryOperation.Read));
                Assert.IsNull(fixture.Session.BeginDatabaseAttempt(DatabaseType.MSSQL));
                return Task.FromResult(ToolResult());
            });

            await fixture.RunAsync(
                Rpc(1, "initialize", new { protocolVersion = "2025-03-26", capabilities = new { }, clientInfo = new { name = SENTINEL, version = "1.0" } }),
                """{"jsonrpc":"2.0","method":"notifications/initialized"}""",
                Rpc(2, "tools/list"),
                ToolCall(3, "describe_entities"),
                Rpc(4, "ping"),
                ToolCall(5, SENTINEL + "_missing_tool"),
                Rpc(6, "tools/call", new { }),
                Rpc(7, "unknown/method"),
                Rpc(8, "shutdown")).WaitAsync(_timeout);

            JsonElement[] responses = fixture.ReadResponses();
            CollectionAssert.AreEqual(Enumerable.Range(1, 8).ToArray(), responses.Select(response => response.GetProperty("id").GetInt32()).ToArray());
            Assert.IsTrue(responses.All(response => response.GetProperty("jsonrpc").GetString() == "2.0"));
            Assert.AreEqual("2025-03-26", responses[0].GetProperty("result").GetProperty("protocolVersion").GetString());
            CollectionAssert.AreEquivalent(new[] { "read_records", "describe_entities" }, responses[1]
                .GetProperty("result").GetProperty("tools").EnumerateArray().Select(tool => tool.GetProperty("name").GetString()).ToArray());
            AssertToolResponse(responses[2], 3, isError: false);
            Assert.IsTrue(responses[3].GetProperty("result").GetProperty("ok").GetBoolean());
            Assert.AreEqual(-32602, responses[4].GetProperty("error").GetProperty("code").GetInt32());
            Assert.AreEqual(-32602, responses[5].GetProperty("error").GetProperty("code").GetInt32());
            Assert.AreEqual(-32601, responses[6].GetProperty("error").GetProperty("code").GetInt32());
            Assert.IsTrue(responses[7].GetProperty("result").GetProperty("ok").GetBoolean());
            VerifyExecutions(dataTool, 0);
            VerifyExecutions(discoveryTool, 1);
            AssertAmbientCleared(fixture);

            EngineTelemetryEvent[] records = await fixture.StopAsync();
            AssertNoRequestTelemetry(records);
            AssertFamilies(records);
            CollectionAssert.AreEqual(new[] { "dab.engine.process_started", "dab.engine.ready", "dab.engine.stopped" },
                records.Select(record => record.Name).ToArray());
        }

        [TestMethod]
        public async Task ToolErrorThenSuccessKeepDistinctFirstMilestonesAndCapturedEpochs()
        {
            await using Fixture fixture = new();
            TaskCompletionSource firstEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource releaseFirst = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource secondEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource releaseSecond = new(TaskCreationOptions.RunContinuationsAsynchronously);
            ConcurrentQueue<EngineTelemetryRequestScope> requests = new();
            ConcurrentQueue<ScopeMarker> scopes = new();
            ConcurrentQueue<HttpContext> contexts = new();
            int calls = 0;
            Mock<IMcpTool> tool = fixture.RegisterTool("read_records", ToolType.BuiltIn, async (_, services, cancellationToken) =>
            {
                EngineTelemetryRequestScope request = ObserveRequest(fixture, services);
                requests.Enqueue(request);
                scopes.Enqueue(services.GetRequiredService<ScopeMarker>());
                HttpContext? context = services.GetRequiredService<IHttpContextAccessor>().HttpContext;
                Assert.IsNotNull(context);
                contexts.Enqueue(context);
                int call = Interlocked.Increment(ref calls);
                if (call == 1)
                {
                    firstEntered.TrySetResult();
                    await releaseFirst.Task.WaitAsync(_timeout, cancellationToken);
                    Assert.AreSame(fixture.InitialConfiguration, request.Config);
                    Assert.AreEqual(1L, request.Configuration.Epoch);
                }
                else if (call == 2)
                {
                    secondEntered.TrySetResult();
                    await releaseSecond.Task.WaitAsync(_timeout, cancellationToken);
                }

                Assert.AreSame(request, fixture.Session.CurrentRequest, "The real request must survive asynchronous tool execution.");
                return ToolResult(isError: call == 1);
            });

            Task running = fixture.RunAsync(ToolCall(1), ToolCall(2), ToolCall(3));
            try
            {
                await firstEntered.Task.WaitAsync(_timeout);
                Assert.IsFalse(running.IsCompleted);
                fixture.Clock.Advance(TimeSpan.FromMilliseconds(10));
                fixture.AcceptReload("postgresql", "view");
                releaseFirst.TrySetResult();
                await secondEntered.Task.WaitAsync(_timeout);

                EngineTelemetryEvent[] afterError = await fixture.CheckpointAsync();
                EngineTelemetryEvent firstServed = afterError.Single(record => record.Name == FIRST_SERVED);
                Assert.AreEqual("failure", firstServed.Properties["outcome"]);
                Assert.AreEqual(1L, firstServed.ConfigurationEpoch, "Reload must not relabel an in-flight request.");
                Assert.AreEqual(0, afterError.Count(record => record.Name == FIRST_SUCCESS));
                AssertRequests(afterError, failure: 1);
                AssertCounts(Summaries(afterError, "operation"), failure: 1);
                Assert.IsTrue(requests.First().IsCompleted);
                Assert.IsFalse(requests.Last().IsCompleted);
            }
            finally
            {
                releaseFirst.TrySetResult();
                releaseSecond.TrySetResult();
                await running.WaitAsync(_timeout);
            }

            JsonElement[] responses = fixture.ReadResponses();
            Assert.AreEqual(3, responses.Length);
            AssertToolResponse(responses[0], 1, isError: true);
            AssertToolResponse(responses[1], 2, isError: false);
            AssertToolResponse(responses[2], 3, isError: false);
            VerifyExecutions(tool, 3);
            Assert.AreEqual(3, scopes.Distinct().Count(), "The real server must create a fresh DI scope for each role-bound call.");
            Assert.AreEqual(3, contexts.Distinct().Count());
            CollectionAssert.AreEqual(new long[] { 1, 2, 2 }, requests.Select(request => request.Configuration.Epoch).ToArray());
            Assert.IsTrue(requests.All(request => request.IsCompleted));
            Assert.IsTrue(requests.Skip(1).All(request => ReferenceEquals(fixture.ConfigProvider.GetConfig(), request.Config)));
            AssertAmbientCleared(fixture);

            EngineTelemetryEvent[] records = await fixture.StopAsync();
            AssertRequests(records, success: 2, failure: 1);
            AssertRequests(records, failure: 1, epoch: 1);
            AssertRequests(records, success: 2, epoch: 2);
            EngineTelemetryEvent oldOperation = Summaries(records, "operation", epoch: 1).Single();
            EngineTelemetryEvent newOperation = Summaries(records, "operation", epoch: 2).Single();
            AssertCounts([oldOperation], failure: 1);
            AssertCounts([newOperation], success: 2);
            Assert.AreEqual("ms_sql", oldOperation.Properties["provider"]);
            Assert.AreEqual("table", oldOperation.Properties["object_type"]);
            Assert.AreEqual("postgre_sql", newOperation.Properties["provider"]);
            Assert.AreEqual("view", newOperation.Properties["object_type"]);
            EngineTelemetryEvent served = records.Single(record => record.Name == FIRST_SERVED);
            EngineTelemetryEvent success = records.Single(record => record.Name == FIRST_SUCCESS);
            Assert.AreEqual("failure", served.Properties["outcome"]);
            Assert.AreEqual("success", success.Properties["outcome"]);
            Assert.AreEqual(1L, served.ConfigurationEpoch);
            Assert.AreEqual(2L, success.ConfigurationEpoch);
            Assert.IsTrue(served.Sequence < success.Sequence);
            Assert.IsTrue(new[] { served, success }.All(record => record.Properties["api"] == "mcp" && record.Properties["transport"] == "stdio"));
            Assert.AreEqual(1L, records.Single(record => record.Name == "dab.engine.ready").ConfigurationEpoch);
            EngineTelemetryEvent changed = records.Single(record => record.Name == "dab.engine.configuration_changed");
            Assert.AreEqual(2L, changed.ConfigurationEpoch);
            Assert.AreEqual("hot_reload", changed.Properties["configuration_delivery"]);
            Assert.AreEqual(2L, records.Single(record => record.Name == "dab.engine.stopped").ConfigurationEpoch);
            AssertFamilies(records, "operation", "request");
        }

        [DataTestMethod]
        [DataRow(ToolType.Custom, SENTINEL + "_custom_tool", "execute")]
        [DataRow(ToolType.Custom, "describe_entities", "execute")]
        [DataRow(ToolType.BuiltIn, "read_records", "read")]
        [DataRow(ToolType.BuiltIn, "aggregate_records", "read")]
        [DataRow(ToolType.BuiltIn, "create_record", "write")]
        [DataRow(ToolType.BuiltIn, SENTINEL + "_unknown_builtin", null)]
        [DataRow(ToolType.BuiltIn, "describe_entities", null)]
        [DataRow((ToolType)99, "read_records", null)]
        public async Task EnabledWrapperUsesClosedEligibilityWithoutExportingRawToolNames(ToolType type, string name, string? operation)
        {
            await using Fixture fixture = new();
            CallToolResult expected = ToolResult();
            EngineTelemetryRequestScope? observed = null;
            Mock<IMcpTool> tool = fixture.RegisterTool(name, type, (arguments, services, _) =>
            {
                Assert.AreSame(fixture.Session, services.GetRequiredService<EngineTelemetrySession>());
                Assert.AreSame(fixture.ConfigProvider, services.GetRequiredService<RuntimeConfigProvider>());
                Assert.IsNotNull(arguments);
                Assert.AreEqual(ENTITY, arguments.RootElement.GetProperty("entity").GetString());
                observed = fixture.Session.CurrentRequest;
                return Task.FromResult(expected);
            });
            using IServiceScope scope = fixture.Services.CreateScope();
            using JsonDocument arguments = ToolArguments();
            int writes = 0;

            CallToolResult actual = await McpTelemetryHelper.ExecuteWithTelemetryAsync(
                tool.Object, name, arguments, scope.ServiceProvider, CancellationToken.None,
                result =>
                {
                    Assert.AreSame(expected, result);
                    Assert.IsFalse(observed?.IsCompleted ?? false);
                    fixture.Stdout.WriteLine(JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, result = new { content = result.Content } }));
                    writes++;
                    return Task.CompletedTask;
                });

            Assert.AreSame(expected, actual);
            Assert.AreEqual(1, writes, "Product eligibility must not suppress execution or the actual response callback.");
            AssertToolResponse(fixture.ReadResponses().Single(), 1, isError: false);
            VerifyExecutions(tool, 1);
            AssertAmbientCleared(fixture);
            EngineTelemetryEvent[] records = await fixture.StopAsync();
            if (operation is null)
            {
                Assert.IsNull(observed);
                AssertNoRequestTelemetry(records);
                AssertFamilies(records);
            }
            else
            {
                Assert.IsNotNull(observed);
                Assert.IsTrue(observed.IsEligible);
                Assert.IsTrue(observed.IsCompleted);
                AssertRequests(records, success: 1);
                EngineTelemetryEvent summary = Summaries(records, "operation").Single();
                AssertCounts([summary], success: 1);
                Assert.AreEqual(operation, summary.Properties["operation"]);
                Assert.AreEqual("mcp", summary.Properties["api"]);
                Assert.AreEqual("ms_sql", summary.Properties["provider"]);
                Assert.AreEqual("table", summary.Properties["object_type"]);
                AssertFamilies(records, "operation", "request");
            }

            Assert.IsFalse(JsonSerializer.Serialize(records).Contains(JsonSerializer.Serialize(name), StringComparison.Ordinal),
                "Custom and built-in tool names must not be retained as product event values or dimension keys.");
        }

        [TestMethod]
        public async Task NestedFakeOperationsAreNotDoubleCountedAndSyntheticAttemptsStayIndependent()
        {
            await using Fixture fixture = new();
            ConcurrentQueue<EngineTelemetryRequestScope> requests = new();
            int calls = 0;
            Mock<IMcpTool> tool = fixture.RegisterTool("read_records", ToolType.BuiltIn, async (_, services, _) =>
            {
                EngineTelemetryRequestScope request = ObserveRequest(fixture, services);
                requests.Enqueue(request);
                if (Interlocked.Increment(ref calls) == 1)
                {
                    using EngineTelemetryMeasurementScope? nested = fixture.Session.BeginOperation(ENTITY, EngineTelemetryOperation.Write);
                    Assert.IsNotNull(nested);
                    // These are two independent synthetic attempts, not execution of SQL or a
                    // real retry policy. Nested operation suppression must not suppress them.
                    using (EngineTelemetryMeasurementScope? failedAttempt = fixture.Session.BeginDatabaseAttempt(DatabaseType.MSSQL))
                    {
                        Assert.IsNotNull(failedAttempt);
                        failedAttempt.Complete(EngineTelemetryOutcome.Failure);
                    }

                    await Task.Yield();
                    using (EngineTelemetryMeasurementScope? successfulAttempt = fixture.Session.BeginDatabaseAttempt(DatabaseType.MSSQL))
                    {
                        Assert.IsNotNull(successfulAttempt);
                        successfulAttempt.Complete(EngineTelemetryOutcome.Success);
                    }

                    nested.Complete(EngineTelemetryOutcome.Failure);
                }

                Assert.AreSame(request, fixture.Session.CurrentRequest);
                return ToolResult();
            });

            // The second invocation also proves operation depth was restored after the first.
            await fixture.RunAsync(ToolCall(1), ToolCall(2)).WaitAsync(_timeout);
            JsonElement[] responses = fixture.ReadResponses();
            Assert.AreEqual(2, responses.Length);
            AssertToolResponse(responses[0], 1, isError: false);
            AssertToolResponse(responses[1], 2, isError: false);
            VerifyExecutions(tool, 2);
            Assert.AreEqual(2, requests.Distinct().Count());
            Assert.IsTrue(requests.All(request => request.IsCompleted));
            AssertAmbientCleared(fixture);

            EngineTelemetryEvent[] records = await fixture.StopAsync();
            AssertRequests(records, success: 2);
            EngineTelemetryEvent operation = Summaries(records, "operation").Single();
            AssertCounts([operation], success: 2);
            Assert.AreEqual("read", operation.Properties["operation"], "The failing nested write is not another logical operation.");
            Assert.AreEqual("mcp", operation.Properties["api"]);
            Assert.AreEqual("ms_sql", operation.Properties["provider"]);
            Assert.AreEqual("table", operation.Properties["object_type"]);
            EngineTelemetryEvent attempts = Summaries(records, "database_attempt").Single();
            AssertCounts([attempts], success: 1, failure: 1);
            Assert.AreEqual("ms_sql", attempts.Properties["provider"]);
            Assert.AreEqual(1L, attempts.ConfigurationEpoch);
            AssertFamilies(records, "operation", "request", "database_attempt");
        }

        [TestMethod]
        public async Task StdioWriteFailureFailsRequestWithoutReclassifyingExecutedOperation()
        {
            await using Fixture fixture = new();
            EngineTelemetryRequestScope? observed = null;
            Mock<IMcpTool> tool = fixture.RegisterTool("read_records", ToolType.BuiltIn, (_, services, _) =>
            {
                observed = ObserveRequest(fixture, services);
                return Task.FromResult(ToolResult());
            });
            int writeAttempts = 0;
            fixture.Output.BeforeWrite = _ =>
            {
                if (Interlocked.Increment(ref writeAttempts) == 1)
                {
                    throw new IOException(SENTINEL + "_writer_failure");
                }
            };

            await fixture.RunAsync(ToolCall(1), Rpc(2, "ping")).WaitAsync(_timeout);

            JsonElement[] responses = fixture.ReadResponses();
            Assert.AreEqual(2, responses.Length);
            Assert.AreEqual(3, writeAttempts, "The failed tool response is followed by a JSON-RPC error and a ping response.");
            Assert.AreEqual(1, responses[0].GetProperty("id").GetInt32());
            Assert.AreEqual(-32603, responses[0].GetProperty("error").GetProperty("code").GetInt32());
            Assert.IsFalse(responses[0].TryGetProperty("result", out _));
            Assert.AreEqual(2, responses[1].GetProperty("id").GetInt32());
            Assert.IsTrue(responses[1].GetProperty("result").GetProperty("ok").GetBoolean());
            Assert.IsNotNull(observed);
            Assert.IsTrue(observed.IsCompleted);
            Assert.AreEqual(EngineTelemetryOutcome.Failure, observed.Outcome);
            VerifyExecutions(tool, 1);
            AssertAmbientCleared(fixture);
            EngineTelemetryEvent[] records = await fixture.StopAsync();
            AssertRequests(records, failure: 1);
            AssertCounts(Summaries(records, "operation"), success: 1);
            Assert.AreEqual("failure", records.Single(record => record.Name == FIRST_SERVED).Properties["outcome"]);
            Assert.AreEqual(0, records.Count(record => record.Name == FIRST_SUCCESS));
            AssertFamilies(records, "operation", "request");
        }

        private static EngineTelemetryRequestScope ObserveRequest(Fixture fixture, IServiceProvider services)
        {
            Assert.AreSame(fixture.Session, services.GetRequiredService<EngineTelemetrySession>());
            Assert.AreSame(fixture.ConfigProvider, services.GetRequiredService<RuntimeConfigProvider>());
            Assert.AreSame(fixture.Session, fixture.ConfigProvider.ProductTelemetry);
            // ValidateScopes rejects this if the server accidentally supplies its root provider.
            Assert.IsNotNull(services.GetRequiredService<ScopeMarker>());
            EngineTelemetryRequestScope? request = fixture.Session.CurrentRequest;
            Assert.IsNotNull(request, "The fake tool must observe the real enabled product scope.");
            Assert.IsTrue(request.IsEligible);
            Assert.IsFalse(request.IsCompleted);
            Assert.AreEqual(EngineTelemetryApi.Mcp, request.Api);
            Assert.AreEqual(EngineTelemetryTransport.Stdio, request.Transport);
            Assert.AreEqual(EngineTelemetryRole.Anonymous, request.Role);
            Assert.AreSame(fixture.ConfigProvider.GetConfig(), request.Config);
            return request;
        }

        private static void AssertAmbientCleared(Fixture fixture)
        {
            Assert.IsNull(fixture.Session.CurrentRequest);
            Assert.IsNull(fixture.Services.GetRequiredService<IHttpContextAccessor>().HttpContext);
        }

        private static void VerifyExecutions(Mock<IMcpTool> tool, int count) => tool.Verify(
            candidate => candidate.ExecuteAsync(It.IsAny<JsonDocument?>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()), Times.Exactly(count));

        private static string Rpc(int id, string method, object? parameters = null)
            => JsonSerializer.Serialize(new { jsonrpc = "2.0", id, method, @params = parameters });

        private static string ToolCall(int id, string name = "read_records")
            => Rpc(id, "tools/call", new { name, arguments = new { entity = ENTITY, filter = SENTINEL + "_argument" } });

        private static JsonDocument ToolArguments()
            => JsonDocument.Parse(JsonSerializer.Serialize(new { entity = ENTITY, filter = SENTINEL + "_argument" }));

        private static CallToolResult ToolResult(bool isError = false) => new()
        {
            IsError = isError,
            Content = new List<ContentBlock>
            {
                new TextContentBlock
                {
                    Text = isError ? JsonSerializer.Serialize(new { code = SENTINEL + "_code", message = SENTINEL + "_error" }) : SENTINEL + "_result"
                }
            }
        };

        private static void AssertToolResponse(JsonElement response, int id, bool isError)
        {
            Assert.AreEqual("2.0", response.GetProperty("jsonrpc").GetString());
            Assert.AreEqual(id, response.GetProperty("id").GetInt32());
            Assert.IsFalse(response.TryGetProperty("error", out _), "A transport/configuration error must not masquerade as an executed tool.");
            JsonElement result = response.GetProperty("result");
            Assert.AreEqual(isError, result.TryGetProperty("isError", out JsonElement flag) && flag.GetBoolean());
            JsonElement content = result.GetProperty("content");
            Assert.AreEqual(1, content.GetArrayLength());
            Assert.AreEqual("text", content[0].GetProperty("type").GetString());
            Assert.AreEqual(((TextContentBlock)ToolResult(isError).Content[0]).Text, content[0].GetProperty("text").GetString());
        }

        private static EngineTelemetryEvent[] Summaries(IEnumerable<EngineTelemetryEvent> records, string family, long? epoch = null)
            => records.Where(record => record.Name == SUMMARY && record.Properties["family"] == family
                && (epoch is null || record.ConfigurationEpoch == epoch)).ToArray();

        private static long Counter(EngineTelemetryEvent record, string name)
            => long.Parse(record.Properties[name], CultureInfo.InvariantCulture);

        private static void AssertCounts(EngineTelemetryEvent[] summaries, long success = 0, long failure = 0)
        {
            Assert.AreEqual(success + failure, summaries.Sum(record => Counter(record, "count")));
            Assert.AreEqual(success, summaries.Sum(record => Counter(record, "success")));
            Assert.AreEqual(failure, summaries.Sum(record => Counter(record, "failure")));
            Assert.AreEqual(0L, summaries.Sum(record => Counter(record, "unknown")));
            Assert.AreEqual(0L, summaries.Sum(record => Counter(record, "partial_failure")));
            Assert.AreEqual(0L, summaries.Sum(record => Counter(record, "canceled")));
        }

        private static void AssertRequests(EngineTelemetryEvent[] records, long success = 0, long failure = 0, long? epoch = null)
        {
            EngineTelemetryEvent[] requests = Summaries(records, "request", epoch);
            AssertCounts(requests, success, failure);
            Assert.AreEqual(success + failure, requests.Sum(record => Counter(record, "timed_count")));
            Assert.IsTrue(requests.All(record => record.Properties["api"] == "mcp"
                && record.Properties["transport"] == "stdio" && record.Properties["role_class"] == "anonymous"));
            Assert.AreEqual(0, Summaries(records, "http_outcome").Length, "The stdio authorization shim must never create an HTTP count family.");
        }

        private static void AssertNoRequestTelemetry(EngineTelemetryEvent[] records)
        {
            AssertRequests(records);
            Assert.AreEqual(0, records.Count(record => record.Name == FIRST_SERVED));
            Assert.AreEqual(0, records.Count(record => record.Name == FIRST_SUCCESS));
        }

        private static void AssertFamilies(EngineTelemetryEvent[] records, params string[] families)
            => CollectionAssert.AreEquivalent(families, records.Where(record => record.Name == SUMMARY)
                .Select(record => record.Properties["family"]).Distinct().ToArray());

        private static string ConfigurationJson(string databaseType = "mssql", string sourceType = "table") => $$"""
            {
              "data-source": { "database-type": "{{databaseType}}", "connection-string": "" },
              "runtime": {
                "rest": { "enabled": false },
                "graphql": { "enabled": false },
                "mcp": { "enabled": true, "description": "{{SENTINEL}}_description" },
                "host": { "mode": "development", "authentication": { "provider": "Simulator" } }
              },
              "entities": {
                "{{ENTITY}}": {
                  "source": { "type": "{{sourceType}}", "object": "{{SENTINEL}}_object" },
                  "rest": false,
                  "graphql": false,
                  "mcp": true,
                  "permissions": [ { "role": "anonymous", "actions": [ "read" ] } ]
                }
              }
            }
            """;

        private sealed class Fixture : IAsyncDisposable
        {
            private readonly MockFileSystem _files = new();
            private readonly List<IMcpTool> _tools = new();
            private readonly string _configPath = Path.Combine(Path.GetTempPath(), SENTINEL, "dab-config.json");
            private readonly FileSystemRuntimeConfigLoader _loader;
            private readonly IConfigurationRoot _configuration;
            private int _identityResolutions;
            internal CapturingExporter Exporter { get; } = new();
            internal ManualClock Clock { get; } = new();
            internal ObservingStringWriter Output { get; } = new();
            internal McpStdoutWriter Stdout { get; }
            internal EngineTelemetrySession Session { get; }
            internal RuntimeConfigProvider ConfigProvider { get; }
            internal RuntimeConfig InitialConfiguration { get; }
            internal McpToolRegistry Registry { get; } = new();
            internal ServiceProvider Services { get; }

            internal Fixture()
            {
                _files.AddFile(_configPath, new MockFileData(ConfigurationJson()));
                // Load real config without environment expansion, connection-string injection,
                // a file watcher or the shared startup log buffer. No global settings are changed.
                _loader = new(_files, baseConfigFilePath: _configPath, isCliLoader: true,
                    logger: NullLogger<FileSystemRuntimeConfigLoader>.Instance);
                Assert.IsTrue(_loader.TryLoadKnownConfig(out RuntimeConfig? config, replaceEnvVar: false));
                Assert.IsNotNull(config);
                InitialConfiguration = config;
                Session = EngineTelemetrySession.Create(() => Exporter, enableSyntheticCollection: true,
                    configPath: _configPath, executionMode: "mcp_stdio", clock: Clock,
                    readEnvironmentVariable: _ => null, showNotice: () => { },
                    resolveIdentity: _ =>
                    {
                        Interlocked.Increment(ref _identityResolutions);
                        return new(_apiId, "ephemeral");
                    }, startTimer: false);
                ConfigProvider = new(_loader) { ProductTelemetry = Session };
                _configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["MCP:StdioMode"] = "true",
                    ["MCP:Role"] = "anonymous"
                }).Build();
                Stdout = new(Output);
                Services = new ServiceCollection()
                    .AddSingleton(Session)
                    .AddSingleton(ConfigProvider)
                    .AddSingleton<IConfiguration>(_configuration)
                    .AddSingleton(Stdout)
                    .AddHttpContextAccessor()
                    .AddScoped(_ => new ScopeMarker())
                    .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
                Session.AcceptConfiguration(ConfigProvider.GetConfig());
                Session.MarkHostReady();
                Assert.IsTrue(Session.IsEnabled);
                Assert.IsTrue(Session.IsReady);
                Assert.AreSame(InitialConfiguration, ConfigProvider.GetConfig());
            }

            internal Mock<IMcpTool> RegisterTool(string name, ToolType type,
                Func<JsonDocument?, IServiceProvider, CancellationToken, Task<CallToolResult>> execute)
            {
                Mock<IMcpTool> tool = new(MockBehavior.Strict);
                tool.SetupGet(candidate => candidate.ToolType).Returns(type);
                tool.Setup(candidate => candidate.GetToolMetadata()).Returns(new Tool
                {
                    Name = name,
                    Description = SENTINEL + "_tool_description",
                    InputSchema = JsonSerializer.SerializeToElement(new { type = "object" })
                });
                tool.Setup(candidate => candidate.IsEnabled(It.IsAny<RuntimeConfig>())).Returns(true);
                tool.Setup(candidate => candidate.ExecuteAsync(It.IsAny<JsonDocument?>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
                    .Returns(execute);
                _tools.Add(tool.Object);
                Registry.ReplaceAll(_tools, ConfigProvider.GetConfig());
                return tool;
            }

            internal async Task RunAsync(params string[] requests)
            {
                using StringReader input = new(string.Join(Environment.NewLine, requests));
                McpStdioServer server = new(Registry, Services, input);
                await server.RunAsync(CancellationToken.None);
            }

            internal JsonElement[] ReadResponses() => Output.ToString()
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line =>
                {
                    using JsonDocument response = JsonDocument.Parse(line);
                    return response.RootElement.Clone();
                }).ToArray();

            internal void AcceptReload(string databaseType, string sourceType)
            {
                // Exercise an accepted-config transition, not filesystem watcher/SQL metadata initialization.
                _files.File.WriteAllText(_configPath, ConfigurationJson(databaseType, sourceType));
                Assert.IsTrue(_loader.TryLoadKnownConfig(out RuntimeConfig? config, replaceEnvVar: false));
                Assert.IsNotNull(config);
                Assert.AreNotSame(InitialConfiguration, config);
                Assert.AreSame(config, ConfigProvider.GetConfig());
                Session.AcceptConfiguration(config, "hot_reload");
            }

            internal async Task<EngineTelemetryEvent[]> CheckpointAsync()
            {
                Clock.Advance(TimeSpan.FromHours(6));
                Session.Tick();
                // Tick enqueues sealed summaries before its heartbeat; the single delivery
                // worker makes that heartbeat an ordered barrier, not a timing-based guess.
                await Exporter.Heartbeats.Reader.ReadAsync().AsTask().WaitAsync(_timeout);
                return Exporter.Records.ToArray();
            }

            internal async Task<EngineTelemetryEvent[]> StopAsync()
            {
                Assert.IsTrue(Session.IsEnabled, "Instrumentation must not silently disable collection.");
                Assert.IsTrue(Session.IsReady);
                await Session.StopAsync().WaitAsync(_timeout);
                await Exporter.Stopped.Task.WaitAsync(_timeout);
                EngineTelemetryEvent[] records = Exporter.Records.ToArray();
                Assert.AreEqual(1, records.Count(record => record.Name == "dab.engine.ready"));
                Assert.AreEqual(1, records.Count(record => record.Name == "dab.engine.stopped"));
                Assert.AreEqual(1, Volatile.Read(ref _identityResolutions));
                Assert.AreNotEqual(Guid.Empty, records[0].SessionId);
                Assert.AreEqual(1, records.Select(record => record.SessionId).Distinct().Count());
                Assert.AreEqual(records.Length, records.Select(record => record.EventId).Distinct().Count());
                CollectionAssert.AreEqual(Enumerable.Range(1, records.Length).Select(index => (long)index).ToArray(),
                    records.Select(record => record.Sequence).ToArray());
                Assert.IsTrue(records.All(record => record.IsSynthetic && record.Properties["execution_mode"] == "mcp_stdio"
                    && record.Properties["sender_dropped_events"] == "0"));
                Assert.IsFalse(records.Single(record => record.Name == "dab.engine.process_started").Properties.ContainsKey("dab_api_id"));
                Assert.IsTrue(records.Where(record => record.Name != "dab.engine.process_started")
                    .All(record => record.Properties["dab_api_id"] == _apiId.ToString("D") && record.Properties["dab_api_id_stability"] == "ephemeral"));
                Assert.AreEqual(0, Summaries(records, "http_outcome").Length);
                Assert.IsFalse(JsonSerializer.Serialize(records).Contains(SENTINEL, StringComparison.Ordinal),
                    "Names, entity/source identifiers, arguments, results, errors, descriptions and config paths must not enter product events.");
                return records;
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await Session.StopAsync().WaitAsync(_timeout);
                }
                finally
                {
                    await Services.DisposeAsync();
                    ConfigProvider.Dispose();
                    _loader.Dispose();
                    (_configuration as IDisposable)?.Dispose();
                    Stdout.Dispose();
                    Session.Dispose();
                }
            }
        }

        private sealed class CapturingExporter : IEngineTelemetryExporter
        {
            internal ConcurrentQueue<EngineTelemetryEvent> Records { get; } = new();
            internal Channel<EngineTelemetryEvent> Heartbeats { get; } = Channel.CreateUnbounded<EngineTelemetryEvent>();
            internal TaskCompletionSource Stopped { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Records.Enqueue(record);
                if (record.Name == "dab.engine.heartbeat")
                {
                    Heartbeats.Writer.TryWrite(record);
                }
                else if (record.Name == "dab.engine.stopped")
                {
                    Stopped.TrySetResult();
                }

                return ValueTask.FromResult(true);
            }

            public void Dispose() => Heartbeats.Writer.TryComplete();
        }

        private sealed class ManualClock : TimeProvider
        {
            private long _timestamp;
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() => Interlocked.Read(ref _timestamp);
            public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 9, 21, 12, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());
            internal void Advance(TimeSpan duration) => Interlocked.Add(ref _timestamp, duration.Ticks);
        }

        private sealed class ObservingStringWriter : StringWriter
        {
            internal Action<string?>? BeforeWrite { get; set; }
            internal Action<string?>? BeforeReturn { get; set; }

            public override void WriteLine(string? value)
            {
                BeforeWrite?.Invoke(value);
                base.WriteLine(value);
                BeforeReturn?.Invoke(value);
            }
        }

        private sealed class ScopeMarker { }
    }
}
