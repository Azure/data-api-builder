// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Services.Embeddings;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for <see cref="ParameterEmbeddingHelper.SubstituteEmbedParametersAsync"/>.
///
/// Covers all behaviors of the helper that converts text-valued sproc parameters
/// (declared with <c>embed: true</c> in config) into vector JSON strings via a
/// mocked <see cref="IEmbeddingService"/>.
///
/// Test groups (organized into <c>#region</c> blocks below):
///   1. No-Op Cases               — early-exit paths that never touch the service
///   2. Service Availability      — null IEmbeddingService handling
///   3. Input Type Validation     — accepted vs rejected value types
///   4. Empty And Whitespace      — empty / whitespace / null text rejection
///   5. Batching Behavior         — proves single batch call (not N sequential)
///   6. Batch Result Handling     — success, failure, length mismatch, null vector
///   7. Output Format And Cancellation — G9 + InvariantCulture; cancellation token forwarding
/// </summary>
[TestClass]
public class ParameterEmbeddingHelperTests
{
    private Mock<IEmbeddingService> _mockService = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockService = new Mock<IEmbeddingService>(MockBehavior.Strict);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helper factories — keep test bodies focused on what's being tested
    // ─────────────────────────────────────────────────────────────────────

    private static ParameterMetadata EmbedParam(string name) =>
        new() { Name = name, Embed = true };

    private static ParameterMetadata NormalParam(string name) =>
        new() { Name = name, Embed = false };

    /// <summary>
    /// Parses a JSON literal into a standalone <see cref="JsonElement"/> that survives
    /// after the source <see cref="JsonDocument"/> is disposed (via <c>Clone</c>).
    /// </summary>
    private static JsonElement JsonElementFrom(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    /// <summary>
    /// Configures the mock to return a successful batch result with the given embeddings.
    /// </summary>
    private void SetupBatch(params float[][] embeddings)
    {
        _mockService
            .Setup(s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingBatchResult(true, embeddings, null));
    }

    /// <summary>
    /// Verifies the batch method was called exactly once AND that single-text TryEmbedAsync
    /// was never called (proves we use the batched path, not sequential per-param calls).
    /// </summary>
    private void VerifyBatchedExactlyOnce()
    {
        _mockService.Verify(
            s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _mockService.Verify(
            s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    #region No-Op Cases

    /// <summary>
    /// When configParams is null (entity has no &lt;parameters&gt; section in config),
    /// the helper should return immediately without calling the embedding service.
    /// </summary>
    [TestMethod]
    public async Task NullConfigParams_ReturnsImmediately_NoServiceCall()
    {
        Dictionary<string, object?> resolvedParams = new() { { "x", "anything" } };

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams: null, _mockService.Object, CancellationToken.None);

        _mockService.VerifyNoOtherCalls();
        Assert.AreEqual("anything", resolvedParams["x"]);
    }

    /// <summary>
    /// When configParams contains parameters but none have embed:true, the helper
    /// should return without calling the embedding service.
    /// </summary>
    [TestMethod]
    public async Task NoEmbedParams_ReturnsImmediately_NoServiceCall()
    {
        Dictionary<string, object?> resolvedParams = new() { { "x", "value" }, { "y", 42 } };
        List<ParameterMetadata> configParams = new() { NormalParam("x"), NormalParam("y") };

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None);

        _mockService.VerifyNoOtherCalls();
        Assert.AreEqual("value", resolvedParams["x"]);
        Assert.AreEqual(42, resolvedParams["y"]);
    }

    /// <summary>
    /// When embed params are configured but none are supplied in the request, the
    /// helper should reach the early-return after Phase 1 collect (embedRequests.Count == 0)
    /// without calling the service.
    /// </summary>
    [TestMethod]
    public async Task EmbedParamsConfiguredButNoneSupplied_ReturnsAfterCollect_NoServiceCall()
    {
        Dictionary<string, object?> resolvedParams = new() { { "other", 5 } };
        List<ParameterMetadata> configParams = new() { EmbedParam("missing"), NormalParam("other") };

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None);

        _mockService.VerifyNoOtherCalls();
        Assert.AreEqual(5, resolvedParams["other"]);
    }

    /// <summary>
    /// When configParams is an empty list, the helper should return immediately
    /// (the "any embed param?" check exits before any iteration).
    /// </summary>
    [TestMethod]
    public async Task EmptyConfigParamsList_ReturnsImmediately_NoServiceCall()
    {
        Dictionary<string, object?> resolvedParams = new() { { "x", "value" } };
        List<ParameterMetadata> configParams = new();

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None);

        _mockService.VerifyNoOtherCalls();
    }

    #endregion

    #region Service Availability

    /// <summary>
    /// When the embedding service is null AND embed params are configured, the helper
    /// should throw a 503 immediately (defense-in-depth check beyond config validation).
    /// </summary>
    [TestMethod]
    public async Task NullService_WithEmbedParams_Throws503()
    {
        Dictionary<string, object?> resolvedParams = new() { { "q", "hello" } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };

        DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
            () => ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
                resolvedParams, configParams, embeddingService: null, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        StringAssert.Contains(ex.Message, "embed parameter");
        StringAssert.Contains(ex.Message, "embedding service");
    }

    /// <summary>
    /// When the embedding service is null but no embed params are configured, the helper
    /// should NOT throw — the no-op early exit fires before the null-service check.
    /// (Backward compat: existing entities without embed params work without an embedding service.)
    /// </summary>
    [TestMethod]
    public async Task NullService_WithoutEmbedParams_NoThrow()
    {
        Dictionary<string, object?> resolvedParams = new() { { "x", "value" } };
        List<ParameterMetadata> configParams = new() { NormalParam("x") };

        // Should not throw
        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, embeddingService: null, CancellationToken.None);

        Assert.AreEqual("value", resolvedParams["x"]);
    }

    #endregion

    #region Input Type Validation

    /// <summary>
    /// A plain System.String value is accepted directly and embedded.
    /// </summary>
    [TestMethod]
    public async Task PlainString_AcceptsAndEmbeds()
    {
        Dictionary<string, object?> resolvedParams = new() { { "q", "wireless headphones" } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };
        SetupBatch(new[] { 0.5f, 0.25f });

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None);

        _mockService.Verify(
            s => s.TryEmbedBatchAsync(
                It.Is<string[]>(arr => arr.Length == 1 && arr[0] == "wireless headphones"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.AreEqual("[0.5,0.25]", resolvedParams["q"]);
    }

    /// <summary>
    /// A JsonElement of kind String (the JSON parser's representation of body strings)
    /// is accepted and embedded.
    /// </summary>
    [TestMethod]
    public async Task JsonElementString_AcceptsAndEmbeds()
    {
        Dictionary<string, object?> resolvedParams = new() { { "q", JsonElementFrom("\"hello\"") } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };
        SetupBatch(new[] { 0.5f });

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None);

        _mockService.Verify(
            s => s.TryEmbedBatchAsync(
                It.Is<string[]>(arr => arr.Length == 1 && arr[0] == "hello"),
                It.IsAny<CancellationToken>()),
            Times.Once);
        Assert.AreEqual("[0.5]", resolvedParams["q"]);
    }

    /// <summary>
    /// A JsonElement of kind Number (e.g., the user sent a number where text was expected)
    /// must be rejected with a 400 BadRequest.
    /// </summary>
    [TestMethod]
    public async Task JsonElementNumber_Throws400()
    {
        Dictionary<string, object?> resolvedParams = new() { { "q", JsonElementFrom("12345") } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };

        DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
            () => ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
                resolvedParams, configParams, _mockService.Object, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        StringAssert.Contains(ex.Message, "Number");
        StringAssert.Contains(ex.Message, "embed: true");
    }

    /// <summary>
    /// A JsonElement of kind True/False (boolean) must be rejected with a 400 BadRequest.
    /// </summary>
    [TestMethod]
    public async Task JsonElementBoolean_Throws400()
    {
        Dictionary<string, object?> resolvedParams = new() { { "q", JsonElementFrom("true") } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };

        DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
            () => ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
                resolvedParams, configParams, _mockService.Object, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        StringAssert.Contains(ex.Message, "True");
    }

    /// <summary>
    /// A JsonElement of kind Array must be rejected with a 400 BadRequest.
    /// </summary>
    [TestMethod]
    public async Task JsonElementArray_Throws400()
    {
        Dictionary<string, object?> resolvedParams = new() { { "q", JsonElementFrom("[\"a\",\"b\"]") } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };

        DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
            () => ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
                resolvedParams, configParams, _mockService.Object, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        StringAssert.Contains(ex.Message, "Array");
    }

    /// <summary>
    /// A JsonElement of kind Object must be rejected with a 400 BadRequest.
    /// </summary>
    [TestMethod]
    public async Task JsonElementObject_Throws400()
    {
        Dictionary<string, object?> resolvedParams = new() { { "q", JsonElementFrom("{\"foo\":\"bar\"}") } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };

        DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
            () => ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
                resolvedParams, configParams, _mockService.Object, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        StringAssert.Contains(ex.Message, "Object");
    }

    /// <summary>
    /// A JsonElement of kind Null falls through to the IsNullOrWhiteSpace check and
    /// is rejected as empty/whitespace (400). The helper does not throw a different
    /// "non-string kind" message for Null since null is semantically "no value supplied".
    /// </summary>
    [TestMethod]
    public async Task JsonElementNull_Throws400AsEmpty()
    {
        Dictionary<string, object?> resolvedParams = new() { { "q", JsonElementFrom("null") } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };

        DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
            () => ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
                resolvedParams, configParams, _mockService.Object, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        StringAssert.Contains(ex.Message, "empty or whitespace");
    }

    /// <summary>
    /// A non-string, non-JsonElement value (e.g., a boxed int) must be rejected
    /// with a 400 BadRequest naming the offending CLR type.
    /// </summary>
    [TestMethod]
    public async Task NonStringNonJsonElementValue_Throws400()
    {
        Dictionary<string, object?> resolvedParams = new() { { "q", (object)42 } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };

        DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
            () => ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
                resolvedParams, configParams, _mockService.Object, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        StringAssert.Contains(ex.Message, "Int32");
    }

    #endregion

    #region Empty And Whitespace Validation

    /// <summary>
    /// An explicit empty string value for an embed parameter is rejected with a 400.
    /// </summary>
    [TestMethod]
    public async Task EmptyString_Throws400()
    {
        Dictionary<string, object?> resolvedParams = new() { { "q", "" } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };

        DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
            () => ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
                resolvedParams, configParams, _mockService.Object, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        StringAssert.Contains(ex.Message, "empty or whitespace");
    }

    /// <summary>
    /// A whitespace-only string value for an embed parameter is rejected with a 400.
    /// (IsNullOrWhiteSpace covers strings of only spaces, tabs, etc.)
    /// </summary>
    [TestMethod]
    public async Task WhitespaceString_Throws400()
    {
        Dictionary<string, object?> resolvedParams = new() { { "q", "   " } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };

        DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
            () => ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
                resolvedParams, configParams, _mockService.Object, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        StringAssert.Contains(ex.Message, "empty or whitespace");
    }

    /// <summary>
    /// A C# null value (not JsonElement Null) for an embed parameter is rejected with a 400.
    /// ExtractTextValue returns null for a null input; the IsNullOrWhiteSpace check then fires.
    /// </summary>
    [TestMethod]
    public async Task NullValue_Throws400()
    {
        Dictionary<string, object?> resolvedParams = new() { { "q", null } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };

        DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
            () => ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
                resolvedParams, configParams, _mockService.Object, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.BadRequest, ex.StatusCode);
        StringAssert.Contains(ex.Message, "empty or whitespace");
    }

    #endregion

    #region Batching Behavior

    /// <summary>
    /// A single embed param results in a single batched call (with one text), NOT a
    /// per-param TryEmbedAsync. Even single-param case goes through TryEmbedBatchAsync.
    /// </summary>
    [TestMethod]
    public async Task SingleEmbedParam_CallsBatchOnce_NotSequential()
    {
        Dictionary<string, object?> resolvedParams = new() { { "q", "hello" } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };
        SetupBatch(new[] { 0.5f });

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None);

        VerifyBatchedExactlyOnce();
        // Verify the user's text was replaced with the serialized vector JSON
        Assert.AreEqual("[0.5]", resolvedParams["q"]);
    }

    /// <summary>
    /// Multiple embed params (all supplied) result in a SINGLE batched call carrying
    /// all texts at once — not N sequential per-param calls. This is the core batching
    /// guarantee that addresses Jim's review comment #2.
    /// </summary>
    [TestMethod]
    public async Task MultipleEmbedParams_CallsBatchOnce_NotSequential()
    {
        Dictionary<string, object?> resolvedParams = new()
        {
            { "p1", "alpha" },
            { "p2", "beta" },
            { "p3", "gamma" }
        };
        List<ParameterMetadata> configParams = new() { EmbedParam("p1"), EmbedParam("p2"), EmbedParam("p3") };
        SetupBatch(
            new[] { 0.5f }, new[] { 0.25f }, new[] { 0.125f });

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None);

        // Exactly one batched call carrying all 3 texts (not 3 sequential calls)
        _mockService.Verify(
            s => s.TryEmbedBatchAsync(
                It.Is<string[]>(arr => arr.Length == 3),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _mockService.Verify(
            s => s.TryEmbedAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Verify each user text was replaced with its corresponding serialized vector
        Assert.AreEqual("[0.5]", resolvedParams["p1"]);
        Assert.AreEqual("[0.25]", resolvedParams["p2"]);
        Assert.AreEqual("[0.125]", resolvedParams["p3"]);
    }

    /// <summary>
    /// When configParams contains a mix of embed:true and embed:false params, only the
    /// embed:true ones are batched. Non-embed params pass through untouched.
    /// </summary>
    [TestMethod]
    public async Task MixedEmbedAndNonEmbed_OnlyEmbedTextsBatched()
    {
        Dictionary<string, object?> resolvedParams = new()
        {
            { "embedA", "search text 1" },
            { "normal1", 5 },
            { "embedB", "search text 2" },
            { "normal2", "literal" }
        };
        List<ParameterMetadata> configParams = new()
        {
            EmbedParam("embedA"),
            NormalParam("normal1"),
            EmbedParam("embedB"),
            NormalParam("normal2")
        };
        SetupBatch(new[] { 0.5f }, new[] { 0.25f });

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None);

        _mockService.Verify(
            s => s.TryEmbedBatchAsync(
                It.Is<string[]>(arr => arr.Length == 2 && arr[0] == "search text 1" && arr[1] == "search text 2"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // Non-embed params pass through untouched
        Assert.AreEqual(5, resolvedParams["normal1"]);
        Assert.AreEqual("literal", resolvedParams["normal2"]);
        // Embed params replaced with vector JSON
        Assert.AreEqual("[0.5]", resolvedParams["embedA"]);
        Assert.AreEqual("[0.25]", resolvedParams["embedB"]);
    }

    /// <summary>
    /// When configParams has multiple embed entries [A, B, C], the texts in the batch
    /// call must be in that same order — the Substitute phase relies on index alignment
    /// between the request list and the response list.
    /// </summary>
    [TestMethod]
    public async Task MultipleEmbedParams_OrderPreserved_BatchTextsMatchConfigOrder()
    {
        Dictionary<string, object?> resolvedParams = new()
        {
            { "C", "third" },
            { "A", "first" },
            { "B", "second" }
        };
        // configParams declared in order A, B, C — this is the order that should be preserved.
        List<ParameterMetadata> configParams = new() { EmbedParam("A"), EmbedParam("B"), EmbedParam("C") };
        SetupBatch(new[] { 0.5f }, new[] { 0.25f }, new[] { 0.125f });

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None);

        _mockService.Verify(
            s => s.TryEmbedBatchAsync(
                It.Is<string[]>(arr =>
                    arr.Length == 3 && arr[0] == "first" && arr[1] == "second" && arr[2] == "third"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        // And substitution preserves order: A → 0.5, B → 0.25, C → 0.125
        Assert.AreEqual("[0.5]", resolvedParams["A"]);
        Assert.AreEqual("[0.25]", resolvedParams["B"]);
        Assert.AreEqual("[0.125]", resolvedParams["C"]);
    }

    /// <summary>
    /// When 3 embed params are configured but only 2 are supplied in the request,
    /// the batch should contain only the supplied subset. The third param is left
    /// for downstream handling (SQL-level required-param error).
    /// </summary>
    [TestMethod]
    public async Task PartiallySuppliedEmbedParams_BatchesSubsetOnly()
    {
        Dictionary<string, object?> resolvedParams = new()
        {
            { "p1", "supplied 1" },
            { "p3", "supplied 3" }
            // p2 is missing
        };
        List<ParameterMetadata> configParams = new() { EmbedParam("p1"), EmbedParam("p2"), EmbedParam("p3") };
        SetupBatch(new[] { 0.5f }, new[] { 0.125f });

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None);

        _mockService.Verify(
            s => s.TryEmbedBatchAsync(
                It.Is<string[]>(arr => arr.Length == 2 && arr[0] == "supplied 1" && arr[1] == "supplied 3"),
                It.IsAny<CancellationToken>()),
            Times.Once);

        Assert.AreEqual("[0.5]", resolvedParams["p1"]);
        Assert.AreEqual("[0.125]", resolvedParams["p3"]);
        Assert.IsFalse(resolvedParams.ContainsKey("p2"));
    }

    #endregion

    #region Batch Result Handling

    /// <summary>
    /// A successful batch result should produce vector JSON substitutions in resolvedParams
    /// — happy-path coverage for the substitute phase.
    /// </summary>
    [TestMethod]
    public async Task BatchSuccess_VectorsSubstitutedInResolvedParams()
    {
        Dictionary<string, object?> resolvedParams = new() { { "p1", "x" }, { "p2", "y" } };
        List<ParameterMetadata> configParams = new() { EmbedParam("p1"), EmbedParam("p2") };
        SetupBatch(new[] { 1.0f, 2.0f }, new[] { 3.0f, 4.0f });

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None);

        Assert.AreEqual("[1,2]", resolvedParams["p1"]);
        Assert.AreEqual("[3,4]", resolvedParams["p2"]);
    }

    /// <summary>
    /// A failed batch result (Success: false) should throw a 500 InternalServerError
    /// listing all involved param names so the caller can identify request context.
    /// </summary>
    [TestMethod]
    public async Task BatchFailure_Throws500_WithAllParamNames()
    {
        Dictionary<string, object?> resolvedParams = new() { { "p1", "a" }, { "p2", "b" } };
        List<ParameterMetadata> configParams = new() { EmbedParam("p1"), EmbedParam("p2") };
        _mockService
            .Setup(s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingBatchResult(false, null, "OpenAI rate limit"));

        DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
            () => ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
                resolvedParams, configParams, _mockService.Object, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.InternalServerError, ex.StatusCode);
        StringAssert.Contains(ex.Message, "p1");
        StringAssert.Contains(ex.Message, "p2");
    }

    /// <summary>
    /// A successful batch result with the wrong embedding count (service contract violation)
    /// should throw a 500 InternalServerError mentioning the count mismatch, instead of
    /// silently using the wrong vectors.
    /// </summary>
    [TestMethod]
    public async Task BatchLengthMismatch_Throws500()
    {
        Dictionary<string, object?> resolvedParams = new() { { "p1", "a" }, { "p2", "b" } };
        List<ParameterMetadata> configParams = new() { EmbedParam("p1"), EmbedParam("p2") };
        // Service returns ONE embedding when TWO were requested
        SetupBatch(new[] { 0.1f });

        DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
            () => ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
                resolvedParams, configParams, _mockService.Object, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.InternalServerError, ex.StatusCode);
        StringAssert.Contains(ex.Message, "1");
        StringAssert.Contains(ex.Message, "2");
    }

    /// <summary>
    /// A batch with one valid and one empty embedding (e.g., empty array slot) should
    /// throw a 500 naming the specific param whose embedding was empty.
    /// </summary>
    [TestMethod]
    public async Task IndividualEmbeddingEmpty_Throws500_NamingFailedParam()
    {
        Dictionary<string, object?> resolvedParams = new() { { "p1", "a" }, { "p2", "b" } };
        List<ParameterMetadata> configParams = new() { EmbedParam("p1"), EmbedParam("p2") };
        // Second embedding is empty — defensive check should fire
        SetupBatch(new[] { 0.1f }, System.Array.Empty<float>());

        DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
            () => ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
                resolvedParams, configParams, _mockService.Object, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.InternalServerError, ex.StatusCode);
        StringAssert.Contains(ex.Message, "p2");
    }

    #endregion

    #region Output Format And Cancellation

    /// <summary>
    /// The vector JSON output must use G9 float formatting and InvariantCulture to ensure
    /// round-trippability and locale-independent decimal separators. This is critical for
    /// embedding precision (G7 default would lose ~30% of values per Microsoft docs).
    ///
    /// Tests:
    ///   - No spaces between values
    ///   - Period (.) as decimal separator regardless of thread culture
    ///   - G9 precision preserved (a value that requires G9 to round-trip)
    /// </summary>
    [TestMethod]
    public async Task VectorJson_UsesG9AndInvariantCulture()
    {
        Dictionary<string, object?> resolvedParams = new() { { "q", "any" } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };

        // 0.1f cannot be exactly represented in binary float; G9 preserves enough digits
        // to round-trip. G7 (the "G" default) would give "0.1" which loses precision.
        SetupBatch(new[] { 0.1f, -0.2f, 0.0001234567f });

        // Save and switch culture to ensure InvariantCulture is enforced
        CultureInfo originalCulture = CultureInfo.CurrentCulture;
        try
        {
            // German locale uses comma as decimal separator — proves we're using InvariantCulture
            CultureInfo.CurrentCulture = new CultureInfo("de-DE");

            await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
                resolvedParams, configParams, _mockService.Object, CancellationToken.None);
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
        }

        string vectorJson = (string)resolvedParams["q"]!;

        // No spaces
        Assert.IsFalse(vectorJson.Contains(" "), "Vector JSON should not contain spaces");
        // Period decimal separator (not German comma)
        Assert.IsTrue(vectorJson.Contains("0.1"), "Vector JSON should use period as decimal separator");
        // Bracketed
        Assert.IsTrue(vectorJson.StartsWith("["), "Vector JSON should start with [");
        Assert.IsTrue(vectorJson.EndsWith("]"), "Vector JSON should end with ]");
        // Comma-separated values
        Assert.AreEqual(3, vectorJson.Split(',').Length, "Vector JSON should have 3 comma-separated values");

        // Round-trip the parsed values to confirm G9 precision
        string trimmed = vectorJson.Substring(1, vectorJson.Length - 2);
        string[] parts = trimmed.Split(',');
        Assert.AreEqual(0.1f, float.Parse(parts[0], CultureInfo.InvariantCulture));
        Assert.AreEqual(-0.2f, float.Parse(parts[1], CultureInfo.InvariantCulture));
        Assert.AreEqual(0.0001234567f, float.Parse(parts[2], CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The CancellationToken passed to the helper must be forwarded to the embedding
    /// service's batch call so request cancellation propagates correctly.
    /// </summary>
    [TestMethod]
    public async Task CancellationToken_ForwardedToEmbeddingService()
    {
        Dictionary<string, object?> resolvedParams = new() { { "q", "hello" } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };
        SetupBatch(new[] { 0.1f });

        using CancellationTokenSource cts = new();
        CancellationToken token = cts.Token;

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, token);

        _mockService.Verify(
            s => s.TryEmbedBatchAsync(
                It.IsAny<string[]>(),
                It.Is<CancellationToken>(ct => ct == token)),
            Times.Once);
    }

    #endregion
}
