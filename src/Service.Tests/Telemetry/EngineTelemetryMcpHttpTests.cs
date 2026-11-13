// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Http;
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
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Routing;
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
    /// <summary>Real SDK HTTP transports with synthetic tools and an in-memory exporter.</summary>
    [TestClass]
    [TestCategory("EngineTelemetry")]
    public class EngineTelemetryMcpHttpTests
    {
        private static readonly byte[] _synchronousPayload = [2, 0, 5];

        [TestMethod]
        public async Task EndpointResolvesTelemetryOnceAtMappingAndRechecksEnablementPerRequest()
        {
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(20));
            using EngineTelemetrySession session = EngineTelemetrySession.Create(() => new CapturingExporter(), enableSyntheticCollection: true,
                readEnvironmentVariable: _ => null, showNotice: () => { }, startTimer: false);
            RuntimeConfig config = new(null, new(DatabaseType.MSSQL, string.Empty), new(new Dictionary<string, Entity>()));
            using FileSystemRuntimeConfigLoader loader = new(new MockFileSystem()) { RuntimeConfig = config };
            using RuntimeConfigProvider provider = new(loader) { ProductTelemetry = session };
            McpToolRegistry registry = new();
            ConcurrentQueue<bool> wrappedRequests = new();
            ConcurrentQueue<ResolutionProbeProvider> requestProviders = new();
            ResolutionProbeProvider? mappingProvider = null;
            using IHost host = new HostBuilder().ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureWebHost(web => web.UseTestServer().ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddSingleton(session);
                    services.AddSingleton(provider);
                    services.AddSingleton(registry);
                    services.ConfigureMcpServer(null);
                }).Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        IServiceProvider original = context.RequestServices;
                        ResolutionProbeProvider requestProvider = new(original);
                        requestProviders.Enqueue(requestProvider);
                        context.RequestServices = requestProvider;
                        try
                        {
                            await next(context);
                        }
                        finally
                        {
                            context.RequestServices = original;
                        }
                    });
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        RouteGroupBuilder group = endpoints.MapGroup(string.Empty);
                        // Group conventions wrap the SDK delegate before MapDabMcp's endpoint
                        // convention, so this runs inside the product response-stream wrapper.
                        ((IEndpointConventionBuilder)group).Add(builder =>
                        {
                            RequestDelegate next = builder.RequestDelegate!;
                            builder.RequestDelegate = context =>
                            {
                                wrappedRequests.Enqueue(context.Response.Body is McpProductResponseStream);
                                return next(context);
                            };
                        });
                        mappingProvider = new(((IEndpointRouteBuilder)group).ServiceProvider);
                        new MappingRouteBuilder(group, mappingProvider).MapDabMcp(provider);
                    });
                })).Build();
            await host.StartAsync(deadline.Token);
            Assert.IsNotNull(mappingProvider);
            Assert.AreEqual(1, mappingProvider.TelemetryResolutions, "Resolve the optional singleton when mapping, not when dispatching.");
            using System.Net.Http.HttpClient http = host.GetTestClient();
            HttpClientTransport transport = new(new HttpClientTransportOptions
            {
                Endpoint = new Uri("http://localhost/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                MaxReconnectionAttempts = 0
            }, http);
            await using McpClient client = await McpClient.CreateAsync(transport, cancellationToken: deadline.Token);

            wrappedRequests.Clear();
            Assert.AreEqual(0, (await client.ListToolsAsync(cancellationToken: deadline.Token)).Count);
            Assert.IsTrue(wrappedRequests.Count > 0);
            Assert.IsTrue(wrappedRequests.All(wrapped => wrapped));
            session.Disable();
            wrappedRequests.Clear();
            Assert.AreEqual(0, (await client.ListToolsAsync(cancellationToken: deadline.Token)).Count);
            Assert.IsTrue(wrappedRequests.Count > 0);
            Assert.IsTrue(wrappedRequests.All(wrapped => !wrapped), "Capture the session, not a stale IsEnabled value.");
            Assert.AreEqual(1, mappingProvider.TelemetryResolutions);
            Assert.AreEqual(0, requestProviders.Sum(requestProvider => requestProvider.TelemetryResolutions),
                "Control/discovery requests must not resolve product telemetry from request DI.");
            await client.DisposeAsync();
            await host.StopAsync(deadline.Token);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task RealSdkFlushDeliversToolResultWhileSseRemainsOpen(bool telemetryEnabled)
        {
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(20));
            CapturingExporter exporter = new();
            using EngineTelemetrySession session = EngineTelemetrySession.Create(() => exporter,
                enableSyntheticCollection: telemetryEnabled, readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);
            RuntimeConfig config = new(null, new(DatabaseType.MSSQL, string.Empty), new(new Dictionary<string, Entity>()));
            using FileSystemRuntimeConfigLoader loader = new(new MockFileSystem()) { RuntimeConfig = config };
            using RuntimeConfigProvider provider = new(loader) { ProductTelemetry = session };
            Mock<IMcpTool> tool = new();
            tool.SetupGet(value => value.ToolType).Returns(ToolType.BuiltIn);
            tool.Setup(value => value.IsEnabled(It.IsAny<RuntimeConfig>())).Returns(true);
            tool.Setup(value => value.GetToolMetadata()).Returns(new Tool { Name = "read_records" });
            tool.Setup(value => value.ExecuteAsync(It.IsAny<JsonDocument?>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new CallToolResult { Content = [new TextContentBlock { Text = "synthetic flush result" }] });
            McpToolRegistry registry = new();
            registry.ReplaceAll([tool.Object], config);
            SseFlushProbe probe = new(deadline.Token);
            TaskCompletionSource responseSent = new(TaskCreationOptions.RunContinuationsAsynchronously);
            int responses = 0;
            using IHost host = new HostBuilder().ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureWebHost(web => web.UseKestrel(options => options.Listen(IPAddress.Loopback, 0)).ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddHttpContextAccessor();
                    services.AddSingleton(session);
                    services.AddSingleton(provider);
                    services.AddSingleton(registry);
                    services.ConfigureMcpServer(null);
                    services.PostConfigure<McpServerOptions>(options => options.Filters.Message.OutgoingFilters.Insert(0,
                        next => async (context, cancellationToken) =>
                        {
                            await next(context, cancellationToken);
                            if (context.JsonRpcMessage is JsonRpcResponse && Interlocked.Increment(ref responses) == 2)
                            {
                                responseSent.TrySetResult();
                            }
                        }));
                }).Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        if (!IsSseRequest(context))
                        {
                            await next(context);
                            return;
                        }

                        IHttpResponseBodyFeature original = context.Features.Get<IHttpResponseBodyFeature>()!;
                        using SseFlushProbeStream stream = new(original.Stream, original.Writer, probe);
                        StreamResponseBodyFeature feature = new(stream, original);
                        // Observe the SDK's Stream path below the product wrapper; the framework
                        // supplies a BodyWriter facade over that same stream, not a separate sink.
                        context.Features.Set<IHttpResponseBodyFeature>(feature);
                        probe.OriginalWriter = context.Response.BodyWriter;
                        try
                        {
                            await next(context);
                        }
                        finally
                        {
                            feature.Dispose();
                            context.Features.Set(original);
                            probe.SseEnded.TrySetResult();
                        }
                    });
                    app.UseRouting();
                    app.UseEndpoints(endpoints =>
                    {
                        RouteGroupBuilder group = endpoints.MapGroup(string.Empty);
                        ((IEndpointConventionBuilder)group).Add(builder =>
                        {
                            RequestDelegate next = builder.RequestDelegate!;
                            builder.RequestDelegate = context =>
                            {
                                if (IsSseRequest(context))
                                {
                                    Assert.AreEqual(telemetryEnabled, context.Response.Body is McpProductResponseStream);
                                    probe.WriterChanged = !ReferenceEquals(probe.OriginalWriter, context.Response.BodyWriter);
                                }

                                return next(context);
                            };
                        });
                        group.MapDabMcp(provider);
                    });
                })).Build();
            using HttpClient http = new(new SocketsHttpHandler { UseProxy = false });
            McpClient? client = null;
            try
            {
                await host.StartAsync(deadline.Token);
                session.AcceptConfiguration(config);
                session.MarkHostReady();
                http.BaseAddress = new Uri(host.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single());
                HttpClientTransport transport = new(new HttpClientTransportOptions
                {
                    Endpoint = new Uri(http.BaseAddress, "/mcp/sse"),
                    TransportMode = HttpTransportMode.Sse,
                    MaxReconnectionAttempts = 0
                }, http);
                client = await McpClient.CreateAsync(transport, cancellationToken: deadline.Token);
                Assert.AreEqual(telemetryEnabled, probe.WriterChanged, "Exercise the changed BodyWriter feature, plus its disabled control.");
                probe.Arm();
                Task<CallToolResult> calling = client.CallToolAsync("read_records", cancellationToken: deadline.Token).AsTask();
                await probe.FlushEntered.Task.WaitAsync(deadline.Token);
                Assert.IsTrue(probe.StagedBytes > 0, "The SDK's actual response bytes must reach Kestrel's pipe before the gated flush.");
                Assert.IsFalse(probe.InnerFlushReturned.Task.IsCompleted);
                Assert.IsFalse(calling.IsCompleted, "The test gate must withhold those bytes until the real inner flush runs.");
                Assert.IsFalse(probe.SseEnded.Task.IsCompleted);
                probe.ReleaseFlush.TrySetResult();
                await probe.InnerFlushReturned.Task.WaitAsync(deadline.Token);
                CallToolResult result = await calling.WaitAsync(deadline.Token);
                Assert.IsFalse(result.IsError == true);
                Assert.AreEqual("synthetic flush result", ((TextContentBlock)result.Content.Single()).Text);
                await responseSent.Task.WaitAsync(deadline.Token);
                Assert.IsFalse(probe.SseEnded.Task.IsCompleted,
                    "A tool result must reach the client over the loopback socket without closing the SSE response.");
            }
            finally
            {
                probe.ReleaseFlush.TrySetResult();
                deadline.Cancel();
                http.CancelPendingRequests();
                try
                {
                    if (client is not null)
                    {
                        await client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                    }
                }
                finally
                {
                    using CancellationTokenSource shutdown = new(TimeSpan.FromSeconds(5));
                    await host.StopAsync(shutdown.Token);
                }
            }

            await session.StopAsync().WaitAsync(TimeSpan.FromSeconds(5));
            EngineTelemetryEvent[] requests = exporter.Records.Where(record => record.Name == "dab/engine.usage_summary" &&
                record.Properties["family"] == "request").ToArray();
            Assert.AreEqual(telemetryEnabled ? 1L : 0L, requests.Sum(record => long.Parse(record.Properties["count"], CultureInfo.InvariantCulture)));
            Assert.AreEqual(telemetryEnabled ? 1L : 0L, requests.Sum(record => long.Parse(record.Properties["success"], CultureInfo.InvariantCulture)));
            Assert.AreEqual(0L, requests.Sum(record => long.Parse(record.Properties["canceled"], CultureInfo.InvariantCulture)));
            tool.Verify(value => value.ExecuteAsync(It.IsAny<JsonDocument?>(), It.IsAny<IServiceProvider>(), It.IsAny<CancellationToken>()), Times.Once);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task FrameworkBodyWriterPreservesDataBackpressureAfterBodyReplacement(bool useWriteAsync)
        {
            using CancellationTokenSource deadline = new(TimeSpan.FromSeconds(10));
            await using ResponseFixture fixture = new();
            Pipe output = new(new PipeOptions(pauseWriterThreshold: 1, resumeWriterThreshold: 1, useSynchronizationContext: false));
            using Stream originalBody = output.Writer.AsStream(leaveOpen: true);
            DefaultHttpContext context = new();
            context.Response.Body = originalBody;
            PipeWriter originalWriter = context.Response.BodyWriter;
            using McpProductResponseStream observer = new(originalBody);
            context.Response.Body = observer;
            PipeWriter writer = context.Response.BodyWriter;
            Assert.AreNotSame(originalWriter, writer, "Use the real framework adapter created by the product's Body replacement.");
            Task sending = fixture.SendAsync(async (_, _) =>
            {
                FlushResult flush;
                if (useWriteAsync)
                {
                    flush = await writer.WriteAsync(_synchronousPayload.AsMemory(), deadline.Token);
                }
                else
                {
                    _synchronousPayload.AsSpan().CopyTo(writer.GetSpan(_synchronousPayload.Length));
                    writer.Advance(_synchronousPayload.Length);
                    Assert.IsFalse(output.Reader.TryRead(out _), "Advance alone must not publish buffered response data.");
                    flush = await writer.FlushAsync(deadline.Token);
                }

                Assert.IsFalse(flush.IsCanceled || flush.IsCompleted);
                Assert.IsFalse(fixture.Request.IsCompleted, "Completion still belongs to the outgoing send boundary.");
            });
            try
            {
                ReadResult read = await output.Reader.ReadAsync(deadline.Token);
                CollectionAssert.AreEqual(_synchronousPayload, read.Buffer.ToArray());
                Assert.IsFalse(sending.IsCompleted, "Nonempty output must keep the framework write/flush pending until consumed.");
                Assert.IsFalse(fixture.Request.IsCompleted);
                output.Reader.AdvanceTo(read.Buffer.End);
                await sending.WaitAsync(deadline.Token);
                await fixture.AssertOnceAsync(EngineTelemetryOutcome.Success);
            }
            finally
            {
                deadline.Cancel();
                output.Writer.CancelPendingFlush();
                await output.Reader.CompleteAsync();
                try
                {
                    await sending.WaitAsync(TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) when (deadline.IsCancellationRequested) { }
                finally
                {
                    // Do not flush an abandoned adapter buffer during failure cleanup.
                    await writer.CompleteAsync(new OperationCanceledException()).AsTask().WaitAsync(TimeSpan.FromSeconds(5));
                    await originalWriter.CompleteAsync();
                    await output.Writer.CompleteAsync();
                    context.Response.Body = originalBody;
                }
            }
        }

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
            EngineTelemetryEvent[] requests = exporter.Records.Where(record => record.Name == "dab/engine.usage_summary" &&
                record.Properties["family"] == "request").ToArray();
            Assert.AreEqual(2L, requests.Sum(record => long.Parse(record.Properties["count"], CultureInfo.InvariantCulture)));
            Assert.AreEqual(2L, requests.Sum(record => long.Parse(record.Properties["success"], CultureInfo.InvariantCulture)));
            Assert.AreEqual(0L, requests.Sum(record => long.Parse(record.Properties["canceled"], CultureInfo.InvariantCulture)));
            Assert.IsTrue(requests.All(record => record.Properties["api"] == "mcp" && record.Properties["transport"] == "http"));
            Assert.AreEqual(1, exporter.Records.Count(record => record.Name == "dab/engine.first_successful_request"));
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
            EngineTelemetryEvent request = exporter.Records.Single(record => record.Name == "dab/engine.usage_summary" &&
                record.Properties["family"] == "request");
            Assert.AreEqual("1", request.Properties["count"]);
            Assert.AreEqual("1", request.Properties["canceled"]);
            Assert.IsFalse(exporter.Records.Any(record => record.Name == "dab/engine.first_successful_request"));
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
            EngineTelemetryEvent summary = exporter.Records.Single(record => record.Name == "dab/engine.usage_summary" &&
                record.Properties["family"] == "request");
            Assert.AreEqual("1", summary.Properties[expectedOutcome]);
            Assert.AreEqual("1", summary.Properties["count"]);
            if (behavior == "success")
            {
                CollectionAssert.AreEqual(payload, inner.ToArray(), "Observation must not alter response bytes.");
            }
        }

        [DataTestMethod]
        [DataRow("span")]
        [DataRow("byte")]
        public async Task SynchronousWritesUseTheInnerOverloadAndWaitForFlush(string method)
        {
            await using ResponseFixture fixture = new();
            using ForwardingProbeStream inner = new();
            using (McpProductResponseStream stream = new(inner))
            {
                await fixture.SendAsync(async (_, _) =>
                {
                    await Task.Yield();
                    WritePayload(stream, method);
                    Assert.IsFalse(fixture.Request.IsCompleted);
                    Assert.AreEqual(0, inner.FlushCalls, "A write must not introduce an extra flush.");
                    stream.Flush();
                    Assert.IsFalse(fixture.Request.IsCompleted, "Completion belongs to the outgoing send boundary.");
                });
            }

            Assert.IsFalse(inner.IsDisposed, "The response, not its observer, owns the inner stream.");
            Assert.AreEqual(1, inner.FlushCalls);
            AssertMatchingOverload(inner, method);
            CollectionAssert.AreEqual(method == "span" ? _synchronousPayload : new byte[] { 0 }, inner.Bytes);
            await fixture.AssertOnceAsync(EngineTelemetryOutcome.Success);
        }

        [DataTestMethod]
        [DataRow("span", false, false)]
        [DataRow("span", false, true)]
        [DataRow("span", true, false)]
        [DataRow("span", true, true)]
        [DataRow("byte", false, false)]
        [DataRow("byte", false, true)]
        [DataRow("byte", true, false)]
        [DataRow("byte", true, true)]
        [DataRow("empty_span", false, false)]
        [DataRow("empty_span", false, true)]
        [DataRow("empty_array", false, false)]
        [DataRow("empty_array", false, true)]
        public async Task SynchronousFailurePropagatesUnchangedAndIsCountedExactlyOnce(string method, bool failOnFlush, bool canceled)
        {
            await using ResponseFixture fixture = new();
            Exception expected = canceled ? new OperationCanceledException("synthetic cancellation") : new IOException("synthetic I/O failure");
            using ForwardingProbeStream inner = new() { Failure = expected, FailOnFlush = failOnFlush };
            using McpProductResponseStream stream = new(inner);
            Task sending = fixture.SendAsync(async (_, _) =>
            {
                await Task.Yield();
                WritePayload(stream, method);
                stream.Flush();
            });

            Exception actual = canceled
                ? await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => sending)
                : await Assert.ThrowsExceptionAsync<IOException>(() => sending);
            Assert.AreSame(expected, actual, "Observation must not replace or swallow a transport exception.");
            AssertMatchingOverload(inner, method);
            Assert.AreEqual(failOnFlush ? 1 : 0, inner.FlushCalls);
            // Both the stream and the outgoing filter see the same failure. Neither may add
            // a second count, and later uncorrelated writes must not keep a completed scope alive.
            inner.Failure = null;
            stream.WriteByte(0);
            stream.Flush();
            await fixture.AssertOnceAsync(canceled ? EngineTelemetryOutcome.Canceled : EngineTelemetryOutcome.Failure);
        }

        [DataTestMethod]
        [DataRow("span", false)]
        [DataRow("byte", true)]
        public async Task SwallowedSynchronousFailureCannotBecomeSuccessAfterALaterFlush(string method, bool canceled)
        {
            await using ResponseFixture fixture = new();
            Exception expected = canceled ? new OperationCanceledException() : new IOException("synthetic I/O failure");
            using ForwardingProbeStream inner = new() { Failure = expected };
            using McpProductResponseStream stream = new(inner);
            await fixture.SendAsync(async (_, _) =>
            {
                await Task.Yield();
                try
                {
                    WritePayload(stream, method);
                    Assert.Fail("The inner write must fail.");
                }
                catch (Exception actual) when (ReferenceEquals(actual, expected))
                {
                    Assert.IsTrue(fixture.Request.IsCompleted);
                }

                inner.Failure = null;
                WritePayload(stream, method);
                stream.Flush();
            });

            await fixture.AssertOnceAsync(canceled ? EngineTelemetryOutcome.Canceled : EngineTelemetryOutcome.Failure);
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task EmptyWritesAreForwardedWithoutInventingOrErasingWrittenBytes(bool includeByte)
        {
            await using ResponseFixture fixture = new();
            using ForwardingProbeStream inner = new();
            using McpProductResponseStream stream = new(inner);
            await fixture.SendAsync(async (_, _) =>
            {
                await Task.Yield();
                WritePayload(stream, "empty_span");
                WritePayload(stream, "empty_array");
                stream.Flush();
                if (includeByte)
                {
                    stream.WriteByte(0); // A zero-valued byte is still a real response byte.
                }

                WritePayload(stream, "empty_span");
                WritePayload(stream, "empty_array");
                stream.Flush();
            });

            Assert.AreEqual(2, inner.SpanWriteCalls);
            Assert.AreEqual(2, inner.ArrayWriteCalls);
            Assert.AreEqual(includeByte ? 1 : 0, inner.ByteWriteCalls);
            Assert.AreEqual(2, inner.FlushCalls);
            CollectionAssert.AreEqual(includeByte ? new byte[] { 0 } : Array.Empty<byte>(), inner.Bytes);
            await fixture.AssertOnceAsync(includeByte ? EngineTelemetryOutcome.Success : EngineTelemetryOutcome.Unknown);
        }

        private static void WritePayload(Stream stream, string method)
        {
            switch (method)
            {
                case "span":
                    stream.Write(_synchronousPayload.AsSpan());
                    break;
                case "byte":
                    stream.WriteByte(0);
                    break;
                case "empty_span":
                    stream.Write(ReadOnlySpan<byte>.Empty);
                    break;
                case "empty_array":
                    stream.Write(Array.Empty<byte>(), 0, 0);
                    break;
                default:
                    Assert.Fail("Unknown test write overload.");
                    break;
            }
        }

        private static void AssertMatchingOverload(ForwardingProbeStream inner, string method)
        {
            Assert.AreEqual(method is "span" or "empty_span" ? 1 : 0, inner.SpanWriteCalls);
            Assert.AreEqual(method == "byte" ? 1 : 0, inner.ByteWriteCalls);
            Assert.AreEqual(method == "empty_array" ? 1 : 0, inner.ArrayWriteCalls,
                "Span and byte forwarding must not fall through Stream's array-copying implementation.");
        }

        private sealed class ResponseFixture : IAsyncDisposable
        {
            private readonly CapturingExporter _exporter = new();
            private readonly EngineTelemetrySession _session;
            private readonly Dictionary<string, object?> _items = new();
            private readonly MessageContext _context;
            internal EngineTelemetryRequestScope Request { get; }

            internal ResponseFixture()
            {
                _session = EngineTelemetrySession.Create(() => _exporter, enableSyntheticCollection: true,
                    readEnvironmentVariable: _ => null, showNotice: () => { },
                    resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);
                _session.AcceptConfiguration(new(null, new(DatabaseType.MSSQL, string.Empty), new(new Dictionary<string, Entity>())));
                _session.MarkHostReady();
                Request = _session.BeginRequest(EngineTelemetryApi.Mcp, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
                Request.SetOutcome(EngineTelemetryOutcome.Success);
                JsonRpcResponse response = new()
                {
                    Id = new RequestId(1),
                    Result = new System.Text.Json.Nodes.JsonObject(),
                    Context = new JsonRpcMessageContext { Items = _items }
                };
                McpProductResponseCompletion.Attach(_items, Request);
                _context = new(Mock.Of<McpServer>(), response);
            }

            internal async Task SendAsync(McpMessageHandler next)
                => await McpProductResponseCompletion.Filter(next)(_context, CancellationToken.None);

            internal async Task AssertOnceAsync(EngineTelemetryOutcome expected)
            {
                Assert.IsTrue(Request.IsCompleted);
                Assert.AreEqual(expected, Request.Outcome);
                Assert.AreEqual(0, _items.Count);
                await _session.StopAsync();
                EngineTelemetryEvent summary = _exporter.Records.Single(record => record.Name == "dab/engine.usage_summary" &&
                    record.Properties["family"] == "request");
                Assert.AreEqual("1", summary.Properties["count"]);
                Assert.AreEqual("1", summary.Properties[expected.ToString().ToLowerInvariant()]);
                Assert.AreEqual(expected == EngineTelemetryOutcome.Success ? 1 : 0,
                    _exporter.Records.Count(record => record.Name == "dab/engine.first_successful_request"));
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    await _session.StopAsync();
                }
                finally
                {
                    Request.Dispose();
                    _session.Dispose();
                }
            }
        }

        private sealed class ForwardingProbeStream : Stream
        {
            private readonly MemoryStream _output = new();
            internal Exception? Failure { get; set; }
            internal bool FailOnFlush { get; set; }
            internal int ArrayWriteCalls { get; private set; }
            internal int SpanWriteCalls { get; private set; }
            internal int ByteWriteCalls { get; private set; }
            internal int FlushCalls { get; private set; }
            internal bool IsDisposed { get; private set; }
            internal byte[] Bytes => _output.ToArray();
            public override bool CanRead => false;
            public override bool CanSeek => false;
            public override bool CanWrite => true;
            public override long Length => _output.Length;
            public override long Position { get => _output.Position; set => throw new NotSupportedException(); }

            public override void Write(byte[] buffer, int offset, int count)
            {
                ArrayWriteCalls++;
                CheckFailure(flushing: false);
                _output.Write(buffer, offset, count);
            }

            public override void Write(ReadOnlySpan<byte> buffer)
            {
                SpanWriteCalls++;
                CheckFailure(flushing: false);
                _output.Write(buffer);
            }

            public override void WriteByte(byte value)
            {
                ByteWriteCalls++;
                CheckFailure(flushing: false);
                _output.WriteByte(value);
            }

            public override void Flush()
            {
                FlushCalls++;
                CheckFailure(flushing: true);
                _output.Flush();
            }

            private void CheckFailure(bool flushing)
            {
                if (Failure is not null && flushing == FailOnFlush)
                {
                    throw Failure;
                }
            }

            protected override void Dispose(bool disposing)
            {
                IsDisposed = true;
                if (disposing)
                {
                    _output.Dispose();
                }

                base.Dispose(disposing);
            }

            public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
        }

        private static bool IsSseRequest(HttpContext context)
            => HttpMethods.IsGet(context.Request.Method) && context.Request.Path == "/mcp/sse";

        private sealed class ResolutionProbeProvider(IServiceProvider inner) : IServiceProvider
        {
            private int _telemetryResolutions;
            internal int TelemetryResolutions => Volatile.Read(ref _telemetryResolutions);

            public object? GetService(Type serviceType)
            {
                if (serviceType == typeof(EngineTelemetrySession))
                {
                    Interlocked.Increment(ref _telemetryResolutions);
                }

                return inner.GetService(serviceType);
            }
        }

        private sealed class MappingRouteBuilder(IEndpointRouteBuilder inner, IServiceProvider services) : IEndpointRouteBuilder
        {
            public IServiceProvider ServiceProvider => services;
            public ICollection<EndpointDataSource> DataSources => inner.DataSources;
            public IApplicationBuilder CreateApplicationBuilder() => inner.CreateApplicationBuilder();
        }

        private sealed class SseFlushProbe(CancellationToken deadline)
        {
            private int _armed;
            private int _stagedBytes;
            internal PipeWriter? OriginalWriter { get; set; }
            internal bool WriterChanged { get; set; }
            internal int StagedBytes => Volatile.Read(ref _stagedBytes);
            internal TaskCompletionSource FlushEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource ReleaseFlush { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource InnerFlushReturned { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
            internal TaskCompletionSource SseEnded { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            internal void Arm() => Volatile.Write(ref _armed, 1);

            internal bool StageWrite(PipeWriter output, ReadOnlyMemory<byte> buffer)
            {
                if (Volatile.Read(ref _armed) != 1 || buffer.IsEmpty)
                {
                    return false;
                }

                // Test-only staging, not an SDK BodyWriter call: Kestrel's Stream.WriteAsync
                // itself flushes. Use its actual pipe buffer to hold these unchanged bytes
                // until the SDK's subsequent Stream.FlushAsync reaches the gate.
                buffer.Span.CopyTo(output.GetSpan(buffer.Length));
                output.Advance(buffer.Length);
                Interlocked.Add(ref _stagedBytes, buffer.Length);
                return true;
            }

            internal async Task FlushAsync(Stream inner, CancellationToken cancellationToken)
            {
                bool gated = StagedBytes > 0 && Interlocked.CompareExchange(ref _armed, 2, 1) == 1;
                if (gated)
                {
                    FlushEntered.TrySetResult();
                    using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(deadline, cancellationToken);
                    await ReleaseFlush.Task.WaitAsync(wait.Token);
                }

                await inner.FlushAsync(cancellationToken);
                if (gated)
                {
                    InnerFlushReturned.TrySetResult();
                }
            }
        }

        private sealed class SseFlushProbeStream(Stream inner, PipeWriter output, SseFlushProbe probe) : Stream
        {
            public override bool CanRead => inner.CanRead;
            public override bool CanSeek => inner.CanSeek;
            public override bool CanWrite => inner.CanWrite;
            public override long Length => inner.Length;
            public override long Position { get => inner.Position; set => inner.Position = value; }

            public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return probe.StageWrite(output, buffer) ? ValueTask.CompletedTask : inner.WriteAsync(buffer, cancellationToken);
            }

            public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
                => WriteAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
            public override Task FlushAsync(CancellationToken cancellationToken)
                => probe.FlushAsync(inner, cancellationToken);
            public override void Flush() => inner.Flush();
            public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);
            public override void Write(ReadOnlySpan<byte> buffer) => inner.Write(buffer);
            public override void WriteByte(byte value) => inner.WriteByte(value);
            public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
            public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
            public override void SetLength(long value) => inner.SetLength(value);
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
                if (record.Name == "dab/engine.usage_summary" && record.Properties["family"] == "request")
                {
                    Summary.TrySetResult();
                }

                return ValueTask.FromResult(true);
            }

            public void Dispose() { }
        }
    }
}
