// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Azure.DataApiBuilder.Core.Services.Embeddings;
using Azure.DataApiBuilder.Core.Telemetry.Product;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ZiggyCreatures.Caching.Fusion;

namespace Azure.DataApiBuilder.Service.Tests.Telemetry;

/// <summary>
/// Exercises the real embedding service and memory cache with synthetic HTTP responses and an
/// in-memory product exporter. No provider connections, identity files, or host are required.
/// </summary>
[TestClass]
[TestCategory("EngineTelemetry")]
public class EngineTelemetryEmbeddingTests
{
    private const string SENTINEL = "PRIVATE_EMBEDDING_INPUT_726f1b";
    private static readonly float[] _embedding = [741852.125f, 0.25f];

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task EachPublicInvocationCountsOnceIncludingCacheHits(bool tryMethod, bool batch)
    {
        using Fixture fixture = new();
        using EngineTelemetryRequestScope request = BeginRequest(fixture);
        string[] texts = batch ? [SENTINEL, SENTINEL + "_second", SENTINEL] : [SENTINEL];

        AssertSuccess(await InvokeAsync(fixture.Service, tryMethod, batch, texts), texts.Length);
        AssertSuccess(await InvokeAsync(fixture.Service, tryMethod, batch, texts), texts.Length);

        Assert.AreEqual(1, fixture.Handler.CallCount, "The second invocation must use cached vectors.");
        Assert.AreEqual(batch ? 2 : 1, fixture.Handler.LastTextCount, "Batch deduplication must be preserved.");
        request.Complete(EngineTelemetryOutcome.Success, 200);
        await AssertEmbeddingCountsAsync(fixture, success: 2);
        Assert.AreEqual(1L, Sum(fixture.Exporter.Events, "request", "count"),
            "Batch elements and nested core calls must not become additional requests.");

        string exported = JsonSerializer.Serialize(fixture.Exporter.Events.ToArray());
        Assert.IsFalse(exported.Contains(SENTINEL, StringComparison.Ordinal),
            "Text, model, endpoint, and API key values must not reach product events.");
        Assert.IsFalse(exported.Contains("741852.125", StringComparison.Ordinal),
            "Embedding vectors must not reach product events.");
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ProviderFailuresPreserveThrowOrFallbackAndCountOneFailure(bool tryMethod, bool batch)
    {
        using Fixture fixture = new();
        fixture.Handler.Response = (_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent(SENTINEL)
        });
        using EngineTelemetryRequestScope request = BeginRequest(fixture);

        if (tryMethod)
        {
            AssertFallback(await InvokeAsync(fixture.Service, tryMethod, batch, [SENTINEL]),
                batch ? "Failed to generate embeddings." : "Failed to generate embedding.");
        }
        else
        {
            HttpRequestException exception = await Assert.ThrowsExceptionAsync<HttpRequestException>(
                () => InvokeAsync(fixture.Service, tryMethod, batch, [SENTINEL]));
            Assert.AreEqual("Embedding request failed with status code 500.", exception.Message);
        }

        Assert.AreEqual(1, fixture.Handler.CallCount);
        await AssertEmbeddingCountsAsync(fixture, failure: 1);
        Assert.IsFalse(JsonSerializer.Serialize(fixture.Exporter.Events.ToArray()).Contains(SENTINEL, StringComparison.Ordinal));
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task CancellationPreservesThrowOrFallbackAndCountsOneCancellation(bool tryMethod, bool batch)
    {
        using Fixture fixture = new();
        using CancellationTokenSource cancellation = new();
        fixture.Handler.Response = (_, _) =>
        {
            cancellation.Cancel();
            return Task.FromCanceled<HttpResponseMessage>(cancellation.Token);
        };
        using EngineTelemetryRequestScope request = BeginRequest(fixture);

        if (tryMethod)
        {
            AssertFallback(await InvokeAsync(fixture.Service, tryMethod, batch, [SENTINEL], cancellation.Token),
                batch ? "Failed to generate embeddings." : "Failed to generate embedding.");
        }
        else
        {
            try
            {
                await InvokeAsync(fixture.Service, tryMethod, batch, [SENTINEL], cancellation.Token);
                Assert.Fail("Cancellation must still propagate from the throwing APIs.");
            }
            catch (OperationCanceledException)
            {
                // Both OperationCanceledException and HttpClient's TaskCanceledException are valid.
            }
        }

        Assert.AreEqual(1, fixture.Handler.CallCount);
        await AssertEmbeddingCountsAsync(fixture, canceled: 1);
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task ValidationFailuresCountWithoutCallingTheProvider(bool tryMethod, bool batch)
    {
        using Fixture fixture = new();
        using EngineTelemetryRequestScope request = BeginRequest(fixture);

        if (tryMethod)
        {
            AssertFallback(await InvokeAsync(fixture.Service, tryMethod, batch, [string.Empty]),
                batch ? "Texts array must not contain null or empty entries." : "Text cannot be null or empty.");
        }
        else
        {
            ArgumentException exception = await Assert.ThrowsExceptionAsync<ArgumentException>(
                () => InvokeAsync(fixture.Service, tryMethod, batch, [string.Empty]));
            Assert.AreEqual(batch ? "texts" : "text", exception.ParamName);
        }

        Assert.AreEqual(0, fixture.Handler.CallCount);
        await AssertEmbeddingCountsAsync(fixture, failure: 1);
    }

    [DataTestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public async Task DisabledEmbeddingServiceStillPreservesItsFailureContract(bool tryMethod, bool batch)
    {
        using Fixture fixture = new(embeddingsEnabled: false);
        using EngineTelemetryRequestScope request = BeginRequest(fixture);

        if (tryMethod)
        {
            AssertFallback(await InvokeAsync(fixture.Service, tryMethod, batch, [SENTINEL]), "Embedding service is disabled.");
        }
        else
        {
            InvalidOperationException exception = await Assert.ThrowsExceptionAsync<InvalidOperationException>(
                () => InvokeAsync(fixture.Service, tryMethod, batch, [SENTINEL]));
            Assert.AreEqual("Embedding service is disabled.", exception.Message);
        }

        Assert.AreEqual(0, fixture.Handler.CallCount);
        await AssertEmbeddingCountsAsync(fixture, failure: 1);
    }

    [DataTestMethod]
    [DataRow("unwired")]
    [DataRow("disabled")]
    [DataRow("no_request")]
    [DataRow("ineligible")]
    public async Task AbsentOrInactiveProductTelemetryDoesNotAffectEmbedding(string mode)
    {
        using Fixture fixture = new(telemetryEnabled: mode != "disabled");
        if (mode == "unwired")
        {
            fixture.Service.ProductTelemetry = null;
        }

        using EngineTelemetryRequestScope? request = mode == "no_request" ? null : BeginRequest(fixture, eligible: mode != "ineligible");
        foreach (bool tryMethod in new[] { false, true })
        {
            foreach (bool batch in new[] { false, true })
            {
                AssertSuccess(await InvokeAsync(fixture.Service, tryMethod, batch, [SENTINEL]), 1);
            }
        }

        Assert.AreEqual(1, fixture.Handler.CallCount);
        await AssertEmbeddingCountsAsync(fixture);
    }

    [TestMethod]
    public async Task OverlappingPublicCallsAreIndependentMeasurements()
    {
        using Fixture fixture = new();
        using EngineTelemetryRequestScope request = BeginRequest(fixture);
        TaskCompletionSource bothEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int entered = 0;
        fixture.Handler.Response = async (_, _) =>
        {
            if (Interlocked.Increment(ref entered) == 2)
            {
                bothEntered.SetResult();
            }

            await release.Task;
            return CreateResponse(1);
        };

        Task<object> first = InvokeAsync(fixture.Service, tryMethod: true, batch: true, texts: [SENTINEL]);
        Task<object> second = InvokeAsync(fixture.Service, tryMethod: false, batch: false, texts: [SENTINEL + "_second"]);
        try
        {
            await bothEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }
        finally
        {
            release.TrySetResult();
        }

        foreach (object result in await Task.WhenAll(first, second))
        {
            AssertSuccess(result, 1);
        }

        await AssertEmbeddingCountsAsync(fixture, success: 2);
    }

    private static EngineTelemetryRequestScope BeginRequest(Fixture fixture, bool eligible = true) =>
        fixture.Session.BeginRequest(EngineTelemetryApi.Rest, EngineTelemetryTransport.Http, EngineTelemetryRole.Anonymous, eligible);

    private static async Task<object> InvokeAsync(EmbeddingService service, bool tryMethod, bool batch,
        string[] texts, CancellationToken cancellationToken = default)
    {
        if (batch)
        {
            if (tryMethod)
            {
                return await service.TryEmbedBatchAsync(texts, cancellationToken);
            }

            return await service.EmbedBatchAsync(texts, cancellationToken);
        }

        if (tryMethod)
        {
            return await service.TryEmbedAsync(texts[0], cancellationToken);
        }

        return await service.EmbedAsync(texts[0], cancellationToken);
    }

    private static void AssertSuccess(object result, int expectedCount)
    {
        float[][] embeddings;
        if (result is EmbeddingResult single)
        {
            Assert.IsTrue(single.Success);
            Assert.IsNotNull(single.Embedding);
            Assert.IsNull(single.ErrorMessage);
            embeddings = [single.Embedding];
        }
        else if (result is EmbeddingBatchResult batch)
        {
            Assert.IsTrue(batch.Success);
            Assert.IsNotNull(batch.Embeddings);
            Assert.IsNull(batch.ErrorMessage);
            embeddings = batch.Embeddings;
        }
        else
        {
            embeddings = result is float[] vector ? [vector] : (float[][])result;
        }

        Assert.AreEqual(expectedCount, embeddings.Length);
        foreach (float[] embedding in embeddings)
        {
            CollectionAssert.AreEqual(_embedding, embedding);
        }
    }

    private static void AssertFallback(object result, string expectedMessage)
    {
        if (result is EmbeddingResult single)
        {
            Assert.IsFalse(single.Success);
            Assert.IsNull(single.Embedding);
            Assert.AreEqual(expectedMessage, single.ErrorMessage);
        }
        else
        {
            EmbeddingBatchResult batch = (EmbeddingBatchResult)result;
            Assert.IsFalse(batch.Success);
            Assert.IsNull(batch.Embeddings);
            Assert.AreEqual(expectedMessage, batch.ErrorMessage);
        }
    }

    private static async Task AssertEmbeddingCountsAsync(Fixture fixture, long success = 0, long failure = 0, long canceled = 0)
    {
        await fixture.Session.StopAsync();
        EngineTelemetryEvent[] records = fixture.Exporter.Events.ToArray();
        Assert.AreEqual(success + failure + canceled, Sum(records, "embedding", "count"));
        Assert.AreEqual(success, Sum(records, "embedding", "success"));
        Assert.AreEqual(failure, Sum(records, "embedding", "failure"));
        Assert.AreEqual(canceled, Sum(records, "embedding", "canceled"));
        Assert.AreEqual(0L, Sum(records, "embedding", "unknown"));
        Assert.AreEqual(0L, Sum(records, "embedding", "partial_failure"));
    }

    private static long Sum(IEnumerable<EngineTelemetryEvent> records, string family, string counter) => records
        .Where(record => record.Name == "dab.engine.usage_summary"
            && record.Properties.TryGetValue("family", out string? value) && value == family)
        .Sum(record => long.Parse(record.Properties[counter], CultureInfo.InvariantCulture));

    private static HttpResponseMessage CreateResponse(int count) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(JsonSerializer.Serialize(new
        {
            data = Enumerable.Range(0, count).Select(index => new { index, embedding = _embedding })
        }), Encoding.UTF8, "application/json")
    };

    private sealed class Fixture : IDisposable
    {
        private readonly HttpClient _httpClient;
        private readonly FusionCache _cache = new(new FusionCacheOptions());
        internal EmbeddingHandler Handler { get; } = new();
        internal CapturingExporter Exporter { get; } = new();
        internal EngineTelemetrySession Session { get; }
        internal EmbeddingService Service { get; }

        internal Fixture(bool embeddingsEnabled = true, bool telemetryEnabled = true)
        {
            Session = EngineTelemetrySession.Create(() => Exporter, enableSyntheticCollection: telemetryEnabled,
                readEnvironmentVariable: _ => null, showNotice: () => { },
                resolveIdentity: _ => new(Guid.NewGuid(), "ephemeral"), startTimer: false);
            Session.AcceptConfiguration(new RuntimeConfig(
                Schema: null,
                DataSource: new DataSource(DatabaseType.MSSQL, string.Empty),
                Runtime: new RuntimeOptions(new RestRuntimeOptions(), new GraphQLRuntimeOptions(), new McpRuntimeOptions(), Host: null),
                Entities: new RuntimeEntities(new Dictionary<string, Entity>())));
            Session.MarkHostReady();
            Assert.AreEqual(telemetryEnabled, Session.IsReady);

            _httpClient = new HttpClient(Handler);
            Service = new EmbeddingService(_httpClient, new EmbeddingsOptions(
                Provider: EmbeddingProviderType.OpenAI, BaseUrl: "https://synthetic.invalid/" + SENTINEL,
                ApiKey: SENTINEL, Enabled: embeddingsEnabled, Model: SENTINEL),
                NullLogger<EmbeddingService>.Instance, _cache)
            {
                ProductTelemetry = Session
            };
        }

        public void Dispose()
        {
            Session.Dispose();
            _httpClient.Dispose();
            _cache.Dispose();
        }
    }

    private sealed class EmbeddingHandler : HttpMessageHandler
    {
        private int _callCount;
        internal int CallCount => Volatile.Read(ref _callCount);
        internal int LastTextCount { get; private set; }
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? Response { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            if (Response is not null)
            {
                return await Response(request, cancellationToken);
            }

            using JsonDocument body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            JsonElement input = body.RootElement.GetProperty("input");
            LastTextCount = input.ValueKind == JsonValueKind.String ? 1 : input.GetArrayLength();
            return CreateResponse(LastTextCount);
        }
    }

    private sealed class CapturingExporter : IEngineTelemetryExporter
    {
        internal ConcurrentQueue<EngineTelemetryEvent> Events { get; } = new();

        public ValueTask<bool> ExportAsync(EngineTelemetryEvent record, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Events.Enqueue(record);
            return ValueTask.FromResult(true);
        }

        public void Dispose() { }
    }
}
