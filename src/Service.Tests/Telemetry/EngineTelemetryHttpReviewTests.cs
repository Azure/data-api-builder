// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Azure.DataApiBuilder.Mcp.Utils;
using Azure.DataApiBuilder.Service.Controllers;
using Azure.DataApiBuilder.Service.Telemetry;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry
{
    [TestClass]
    [TestCategory("EngineTelemetry")]
    public class EngineTelemetryHttpReviewTests
    {
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(10);

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task CompletionRegistrationDoesNotLockThePublicHttpContext(bool mcp)
        {
            using EngineTelemetrySession session = CreateSession(new());
            ResponseCallbacks response = new();
            DefaultHttpContext context = new();
            context.Features.Set<IHttpResponseFeature>(response);
            TaskCompletionSource lockHeld = Signal();
            TaskCompletionSource release = Signal();
            Task holder = Task.Factory.StartNew(() =>
            {
                lock (context)
                {
                    lockHeld.TrySetResult();
                    release.Task.GetAwaiter().GetResult();
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
            Task? registration = null;
            try
            {
                await lockHeld.Task.WaitAsync(_timeout);
                registration = Task.Run(() => Register(context, session, mcp));
                await registration.WaitAsync(_timeout);
                Assert.IsFalse(holder.IsCompleted, "No unrelated public monitor needs releasing to register completion.");
                Assert.AreEqual(1, response.Count);
            }
            finally
            {
                release.TrySetResult();
                await holder.WaitAsync(_timeout);
                if (registration is not null)
                {
                    await registration.WaitAsync(_timeout);
                }
            }

            await response.CompleteAsync();
            await session.StopAsync();
        }

        [DataTestMethod]
        [DataRow(false)]
        [DataRow(true)]
        public async Task ConcurrentCompletionRegistrationsShareOnePrivateGate(bool mcp)
        {
            ConcurrentQueue<EngineTelemetryEvent> records = new();
            using EngineTelemetrySession session = CreateSession(records);
            session.AcceptConfiguration(CreateConfig());
            session.MarkHostReady();
            ResponseCallbacks response = new();
            DefaultHttpContext context = new();
            context.Features.Set<IHttpResponseFeature>(response);
            const int count = 64;
            await Task.WhenAll(Enumerable.Range(0, count).Select(_ => Task.Run(() => Register(context, session, mcp)))).WaitAsync(_timeout);

            Assert.AreEqual(count, response.Count);
            Assert.AreEqual(0, response.ConcurrentRegistrations);
            await response.CompleteAsync();
            await response.CompleteAsync();
            await session.StopAsync();
            Assert.AreEqual(count, records.Where(record => record.Name == "dab/engine.usage_summary" && record.Properties["family"] == "request")
                .Sum(record => int.Parse(record.Properties["count"])));
        }

        [DataTestMethod]
        [DataRow("GET", 401, false, 401, true)]
        [DataRow("GET", 403, false, 403, true)]
        [DataRow("OPTIONS", 401, true, 204, false)]
        [DataRow("GET", 200, true, 200, true)]
        public async Task RoutedAttemptsObserveServerRejectionButNotBrowserCorsDecisions(
            string method, int nextStatus, bool disallowedOrigin, int expectedStatus, bool counted)
        {
            ConcurrentQueue<EngineTelemetryEvent> records = new();
            using EngineTelemetrySession session = CreateSession(records);
            RuntimeConfig config = CreateConfig();
            using RuntimeConfigProvider provider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            _ = provider.GetConfig();
            session.AcceptConfiguration(config);
            session.MarkHostReady();
            TaskCompletionSource completed = Signal();
            int rejectionMiddlewareCalls = 0;
            using IHost host = new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
                .ConfigureServices(services =>
                {
                    services.AddRouting();
                    services.AddCors(options => options.AddDefaultPolicy(policy => policy.WithOrigins("https://allowed.invalid").AllowAnyHeader().AllowAnyMethod()));
                })
                .Configure(app =>
                {
                    app.Use(async (context, next) =>
                    {
                        context.Response.OnCompleted(() => { completed.TrySetResult(); return Task.CompletedTask; });
                        await next(context);
                    });
                    app.UseRouting();
                    app.Use(next => new EngineTelemetryHttpMiddleware(next, provider, session).InvokeAsync);
                    app.UseCors();
                    // Model auth rejection at its actual pipeline position without tokens,
                    // credential acquisition or a fake successful data operation.
                    app.Use(async (context, next) =>
                    {
                        Interlocked.Increment(ref rejectionMiddlewareCalls);
                        if (nextStatus >= 400)
                        {
                            context.Response.StatusCode = nextStatus;
                            return;
                        }

                        await next(context);
                    });
                    app.UseEndpoints(endpoints => endpoints.Map("/api/items", context => context.Response.WriteAsync("synthetic"))
                        .WithMetadata(new ControllerActionDescriptor { ControllerTypeInfo = typeof(RestController).GetTypeInfo() }));
                })).Build();
            await host.StartAsync();
            using HttpClient client = host.GetTestClient();
            using HttpRequestMessage request = new(new HttpMethod(method), "/api/items");
            if (disallowedOrigin)
            {
                request.Headers.Add("Origin", "https://blocked.invalid");
                if (method == "OPTIONS")
                {
                    request.Headers.Add("Access-Control-Request-Method", "GET");
                }
            }

            using HttpResponseMessage response = await client.SendAsync(request);
            await response.Content.ReadAsByteArrayAsync();
            await completed.Task.WaitAsync(_timeout);
            await session.StopAsync();

            Assert.AreEqual(expectedStatus, (int)response.StatusCode);
            Assert.AreEqual(method == "OPTIONS" ? 0 : 1, rejectionMiddlewareCalls);
            Assert.IsFalse(response.Headers.Contains("Access-Control-Allow-Origin"));
            EngineTelemetryEvent[] requests = records.Where(record => record.Name == "dab/engine.usage_summary" && record.Properties["family"] == "request").ToArray();
            Assert.AreEqual(counted ? 1 : 0, requests.Length);
            if (counted)
            {
                Assert.AreEqual("1", requests.Single().Properties[nextStatus >= 400 ? "failure" : "success"]);
            }

            Assert.AreEqual(counted && nextStatus < 400 ? 1 : 0, records.Count(record => record.Name == "dab/engine.first_successful_request"));
            Assert.IsFalse(records.Any(record => record.Name == "dab/engine.usage_summary" && record.Properties["family"] is "operation" or "database_attempt"));
        }

        private static void Register(DefaultHttpContext context, EngineTelemetrySession session, bool mcp)
        {
            using EngineTelemetryRequestScope request = session.BeginRequest(
                mcp ? EngineTelemetryApi.Mcp : EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous);
            request.SetOutcome(EngineTelemetryOutcome.Success);
            if (mcp)
            {
                _ = new McpTelemetryHelper.ProductRequestCompletion(request, context);
            }
            else
            {
                _ = new EngineTelemetryHttpCompletion(context, request.Complete, () => request.Outcome, inferSuccessFromHttp: true);
            }
        }

        private static RuntimeConfig CreateConfig() => new(null, new(DatabaseType.MSSQL, string.Empty), new(new Dictionary<string, Entity>()));

        private static EngineTelemetrySession CreateSession(ConcurrentQueue<EngineTelemetryEvent> records) => EngineTelemetrySession.Create(
            () => new CapturingExporter(records), enableSyntheticCollection: true, readEnvironmentVariable: _ => null,
            showNotice: () => { }, resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);

        private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        private sealed class CapturingExporter(ConcurrentQueue<EngineTelemetryEvent> records) : IEngineTelemetryExporter
        {
            public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
            {
                records.Enqueue(record);
                return ValueTask.FromResult(true);
            }

            public void Dispose() { }
        }

        private sealed class ResponseCallbacks : IHttpResponseFeature
        {
            private readonly List<(Func<object, Task> Callback, object State)> _callbacks = new();
            private int _registering;
            private int _concurrentRegistrations;
            public int Count => _callbacks.Count;
            public int ConcurrentRegistrations => Volatile.Read(ref _concurrentRegistrations);
            public int StatusCode { get; set; } = 200;
            public string? ReasonPhrase { get; set; }
            public IHeaderDictionary Headers { get; set; } = new HeaderDictionary();
            public Stream Body { get; set; } = Stream.Null;
            public bool HasStarted => false;
            public void OnStarting(Func<object, Task> callback, object state) { }
            public void OnCompleted(Func<object, Task> callback, object state)
            {
                if (Interlocked.Increment(ref _registering) != 1)
                {
                    Interlocked.Increment(ref _concurrentRegistrations);
                }

                try
                {
                    Thread.Yield();
                    _callbacks.Add((callback, state));
                }
                finally
                {
                    Interlocked.Decrement(ref _registering);
                }
            }

            public async Task CompleteAsync()
            {
                foreach ((Func<object, Task> callback, object state) in _callbacks)
                {
                    await callback(state);
                }
            }
        }
    }
}
