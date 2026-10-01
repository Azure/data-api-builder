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
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Mcp.Core;
using Azure.DataApiBuilder.Mcp.Model;
using Azure.DataApiBuilder.Mcp.Utils;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Moq;
using static Azure.DataApiBuilder.Mcp.Model.McpEnums;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    /// <summary>Real SDK HTTP transports in TestServer, with synthetic tools and an in-memory exporter.</summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    public class EngineTelemetryMcpHttpTests
    {
        [DataTestMethod]
        [DataRow(HttpTransportMode.Sse)]
        [DataRow(HttpTransportMode.StreamableHttp)]
        public async Task ToolResponsesCompleteBeforeHttpSessionDisconnect(HttpTransportMode mode)
        {
            using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
            CapturingExporter exporter = new();
            ManualClock clock = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(() => exporter,
                enableSyntheticCollection: true, clock: clock, readEnvironmentVariable: _ => null,
                showNotice: () => { }, resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);
            RuntimeConfig config = new(null, new(DatabaseType.MSSQL, string.Empty), new(new Dictionary<string, Entity>()));
            using FileSystemRuntimeConfigLoader loader = new(new MockFileSystem()) { RuntimeConfig = config };
            using RuntimeConfigProvider provider = new(loader) { ProductTelemetry = session };
            Mock<IMcpTool> tool = new();
            tool.SetupGet(value => value.ToolType).Returns(ToolType.BuiltIn);
            tool.Setup(value => value.IsEnabled(It.IsAny<RuntimeConfig>())).Returns(true);
            tool.Setup(value => value.GetToolMetadata()).Returns(new Tool { Name = "read_records" });
            tool.Setup(value => value.ExecuteAsync(It.IsAny<JsonDocument?>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallToolResult { Content = [new TextContentBlock { Text = "synthetic result" }] });
            McpToolRegistry registry = new();
            registry.ReplaceAll([tool.Object], config);
            int responses = 0;
            TaskCompletionSource sent = new(TaskCreationOptions.RunContinuationsAsynchronously);
            using IHost host = new HostBuilder().ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddSingleton(session);
                    services.AddSingleton(provider);
                    services.AddSingleton(registry);
                    services.ConfigureMcpServer(instructions: null);
                    services.PostConfigure<McpServerOptions>(options => options.Filters.Message.OutgoingFilters.Insert(0,
                        next => async (context, cancellationToken) =>
                        {
                            await next(context, cancellationToken);
                            if (context.JsonRpcMessage is JsonRpcResponse && Interlocked.Increment(ref responses) == 3)
                            {
                                sent.TrySetResult();
                            }
                        }));
                }).Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapDabMcp(provider));
                })).Build();
            await host.StartAsync(timeout.Token);
            session.AcceptConfiguration(config);
            session.MarkHostReady();
            using System.Net.Http.HttpClient http = host.GetTestClient();
            HttpClientTransport transport = new(new HttpClientTransportOptions
            {
                Endpoint = new Uri(mode == HttpTransportMode.Sse ? "http://localhost/mcp/sse" : "http://localhost/mcp"),
                TransportMode = mode,
                MaxReconnectionAttempts = 0
            }, http);
            await using McpClient client = await McpClient.CreateAsync(transport, cancellationToken: timeout.Token);
            for (int index = 0; index < 2; index++)
            {
                CallToolResult result = await client.CallToolAsync("read_records", cancellationToken: timeout.Token);
                Assert.IsFalse(result.IsError == true);
            }

            await sent.Task.WaitAsync(timeout.Token);
            clock.Advance(TimeSpan.FromHours(6));
            session.Tick();
            // Flush the completed window while the HTTP session is still open. A request's
            // response has been sent; it must not wait for, or be canceled by, SSE disconnect.
            await exporter.Summary.Task.WaitAsync(TimeSpan.FromSeconds(2));
            await client.DisposeAsync();
            await session.StopAsync(timeout.Token);
            EngineTelemetryEvent[] requests = exporter.Records.Where(record => record.Name == "dab.engine.usage_summary" &&
                record.Properties["family"] == "request").ToArray();
            Assert.AreEqual(2L, requests.Sum(record => long.Parse(record.Properties["count"], CultureInfo.InvariantCulture)));
            Assert.AreEqual(2L, requests.Sum(record => long.Parse(record.Properties["success"], CultureInfo.InvariantCulture)));
            Assert.AreEqual(0L, requests.Sum(record => long.Parse(record.Properties["canceled"], CultureInfo.InvariantCulture)));
            Assert.IsTrue(requests.All(record => record.Properties["api"] == "mcp" && record.Properties["transport"] == "http"));
            Assert.AreEqual(1, exporter.Records.Count(record => record.Name == "dab.engine.first_successful_request"));
            await host.StopAsync(timeout.Token);
        }

        [DataTestMethod]
        [DataRow(HttpTransportMode.Sse, false)]
        [DataRow(HttpTransportMode.StreamableHttp, false)]
        [DataRow(HttpTransportMode.Sse, true)]
        [DataRow(HttpTransportMode.StreamableHttp, true)]
        public async Task SdkCancellationCountsRequestEvenWhenNoResponseFilterRuns(HttpTransportMode mode, bool returnsError)
        {
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(15));
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(() => exporter, enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);
            RuntimeConfig config = new(null, new(DatabaseType.MSSQL, string.Empty), new(new Dictionary<string, Entity>()));
            using FileSystemRuntimeConfigLoader loader = new(new MockFileSystem()) { RuntimeConfig = config };
            using RuntimeConfigProvider provider = new(loader) { ProductTelemetry = session };
            TaskCompletionSource entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource canceled = new(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<RequestId> requestId = new(TaskCreationOptions.RunContinuationsAsynchronously);
            Mock<IMcpTool> tool = new();
            tool.SetupGet(value => value.ToolType).Returns(ToolType.BuiltIn);
            tool.Setup(value => value.IsEnabled(It.IsAny<RuntimeConfig>())).Returns(true);
            tool.Setup(value => value.GetToolMetadata()).Returns(new Tool { Name = "read_records" });
            tool.Setup(value => value.ExecuteAsync(It.IsAny<JsonDocument?>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
                .Returns(async (JsonDocument? _, IServiceProvider _, CancellationToken token) =>
                {
                    entered.TrySetResult();
                    await Task.Delay(Timeout.InfiniteTimeSpan, token).ContinueWith(_ => { }, TaskScheduler.Default);
                    canceled.TrySetResult();
                    return new CallToolResult { IsError = returnsError, Content = [] };
                });
            McpToolRegistry registry = new();
            registry.ReplaceAll([tool.Object], config);
            using IHost host = new HostBuilder().ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddSingleton(session);
                    services.AddSingleton(provider);
                    services.AddSingleton(registry);
                    services.ConfigureMcpServer(null);
                    services.PostConfigure<McpServerOptions>(options => options.Filters.Message.IncomingFilters.Insert(0,
                        next => (context, cancellationToken) =>
                        {
                            if (context.JsonRpcMessage is JsonRpcRequest { Method: RequestMethods.ToolsCall } request)
                            {
                                requestId.TrySetResult(request.Id);
                            }

                            return next(context, cancellationToken);
                        }));
                }).Configure(app =>
                {
                    app.UseRouting();
                    app.UseEndpoints(endpoints => endpoints.MapDabMcp(provider));
                })).Build();
            await host.StartAsync(deadline.Token);
            session.AcceptConfiguration(config);
            session.MarkHostReady();
            using System.Net.Http.HttpClient http = host.GetTestClient();
            HttpClientTransport transport = new(new HttpClientTransportOptions
            {
                Endpoint = new Uri(mode == HttpTransportMode.Sse ? "http://localhost/mcp/sse" : "http://localhost/mcp"),
                TransportMode = mode,
                MaxReconnectionAttempts = 0
            }, http);
            await using McpClient client = await McpClient.CreateAsync(transport, cancellationToken: deadline.Token);
            using CancellationTokenSource callCancellation = CancellationTokenSource.CreateLinkedTokenSource(deadline.Token);
            Task<CallToolResult> calling = client.CallToolAsync("read_records", cancellationToken: callCancellation.Token).AsTask();
            await entered.Task.WaitAsync(deadline.Token);
            // Send a protocol cancellation explicitly; canceling the client HTTP wait alone
            // need not deliver a notification in this SDK/transport combination.
            await client.SendNotificationAsync(NotificationMethods.CancelledNotification,
                new CancelledNotificationParams { RequestId = await requestId.Task.WaitAsync(deadline.Token) },
                cancellationToken: deadline.Token);
            await canceled.Task.WaitAsync(deadline.Token);
            callCancellation.Cancel();
            try
            {
                await calling;
                Assert.Fail("The client call must observe cancellation.");
            }
            catch (OperationCanceledException) { }

            await client.DisposeAsync();
            await host.StopAsync(deadline.Token);
            await session.StopAsync();
            EngineTelemetryEvent request = exporter.Records.Single(record => record.Name == "dab.engine.usage_summary" &&
                record.Properties["family"] == "request");
            Assert.AreEqual("1", request.Properties["count"]);
            Assert.AreEqual("1", request.Properties["canceled"]);
            Assert.IsFalse(exporter.Records.Any(record => record.Name == "dab.engine.first_successful_request"));
        }

        [DataTestMethod]
        [DataRow("success", "success")]
        [DataRow("dropped", "unknown")]
        [DataRow("swallowed_write_failure", "failure")]
        [DataRow("flush_failure", "failure")]
        [DataRow("canceled", "canceled")]
        public async Task ResponseCompletionRequiresObservedWriteAndFlush(string behavior, string expectedOutcome)
        {
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(() => exporter, enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);
            session.AcceptConfiguration(new(null, new(DatabaseType.MSSQL, string.Empty), new(new Dictionary<string, Entity>())));
            session.MarkHostReady();
            using EngineTelemetryRequestScope request = session.BeginRequest(
                EngineTelemetryApi.Mcp, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            request.SetOutcome(EngineTelemetryOutcome.Success);
            JsonRpcResponse response = new()
            {
                Id = new RequestId(1),
                Result = new System.Text.Json.Nodes.JsonObject(),
                Context = new JsonRpcMessageContext { Items = new Dictionary<string, object?>() }
            };
            McpProductResponseCompletion.Attach(response.Context.Items, request);
            MessageContext context = new(Mock.Of<McpServer>(), response);
            using BoundaryStream inner = new(behavior);
            using McpProductResponseStream stream = new(inner);
            byte[] payload = [1, 2, 3];
            McpMessageHandler send = McpProductResponseCompletion.Filter(async (_, cancellationToken) =>
            {
                if (behavior == "dropped")
                {
                    return;
                }

                try
                {
                    await stream.WriteAsync(payload.AsMemory(), cancellationToken);
                    await stream.FlushAsync(cancellationToken);
                }
                catch (IOException) when (behavior == "swallowed_write_failure")
                {
                    // The pinned Streamable HTTP SDK records this in its HTTP task but may
                    // return normally to the outgoing filter. It still cannot mean success.
                }
            });
            if (behavior == "flush_failure")
            {
                await Assert.ThrowsExceptionAsync<IOException>(() => send(context, CancellationToken.None));
            }
            else if (behavior == "canceled")
            {
                await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => send(context, CancellationToken.None));
            }
            else
            {
                await send(context, CancellationToken.None);
            }

            Assert.IsTrue(request.IsCompleted);
            Assert.AreEqual(0, response.Context.Items.Count, "The SDK context must not retain a completed product scope.");
            await session.StopAsync();
            EngineTelemetryEvent summary = exporter.Records.Single(record => record.Name == "dab.engine.usage_summary" &&
                record.Properties["family"] == "request");
            Assert.AreEqual("1", summary.Properties[expectedOutcome]);
            Assert.AreEqual("1", summary.Properties["count"]);
            if (behavior == "success")
            {
                CollectionAssert.AreEqual(payload, inner.ToArray(), "Observation must not alter response bytes.");
            }
        }

        private sealed class BoundaryStream(string behavior) : MemoryStream
        {
            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                if (behavior == "swallowed_write_failure")
                {
                    throw new IOException("synthetic write failure");
                }

                if (behavior == "canceled")
                {
                    throw new OperationCanceledException();
                }

                return base.WriteAsync(buffer, cancellationToken);
            }

            public override Task FlushAsync(CancellationToken cancellationToken)
                => behavior == "flush_failure" ? throw new IOException("synthetic flush failure") : base.FlushAsync(cancellationToken);
        }

        private sealed class ManualClock : TimeProvider
        {
            private long _ticks;
            public override long TimestampFrequency => TimeSpan.TicksPerSecond;
            public override long GetTimestamp() => Interlocked.Read(ref _ticks);
            public override DateTimeOffset GetUtcNow() => new DateTimeOffset(2026, 9, 24, 0, 0, 0, TimeSpan.Zero).AddTicks(GetTimestamp());
            internal void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
        }

        private sealed class CapturingExporter : IEngineTelemetryExporter
        {
            internal ConcurrentQueue<EngineTelemetryEvent> Records { get; } = new();
            internal TaskCompletionSource Summary { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
            {
                Records.Enqueue(record);
                if (record.Name == "dab.engine.usage_summary" && record.Properties["family"] == "request")
                {
                    Summary.TrySetResult();
                }

                return ValueTask.FromResult(true);
            }

            public void Dispose() { }
        }
    }
}
