// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        // All real-service tests assume the service is enabled. Tests that exercise the
        // disabled-service path use NullEmbeddingService.Instance directly.
        _mockService.Setup(s => s.IsEnabled).Returns(true);
    }

    // ─────────────────────────────────────────────────────────────────────
    // Helper factories — keep test bodies focused on what's being tested
    // ─────────────────────────────────────────────────────────────────────

    private static ParameterMetadata EmbedParam(string name) =>
        new() { Name = name, AutoEmbed = true };

    private static ParameterMetadata NormalParam(string name) =>
        new() { Name = name, AutoEmbed = false };

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
    /// without making any batch API call to the embedding provider.
    /// </summary>
    [TestMethod]
    public async Task EmbedParamsConfiguredButNoneSupplied_ReturnsAfterCollect_NoServiceCall()
    {
        Dictionary<string, object?> resolvedParams = new() { { "other", 5 } };
        List<ParameterMetadata> configParams = new() { EmbedParam("missing"), NormalParam("other") };

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None);

        // The helper consults IsEnabled before doing work; that's an implementation detail.
        // The behavioral assertion is "no batch embedding call was made to the provider."
        _mockService.Verify(
            s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()),
            Times.Never);
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
    /// When the embedding service reports IsEnabled=false (e.g., NullEmbeddingService
    /// is injected because runtime.embeddings is absent or disabled) AND embed params
    /// are configured, the helper should throw a 503 immediately (defense-in-depth
    /// check beyond startup config validation).
    /// </summary>
    [TestMethod]
    public async Task DisabledService_WithEmbedParams_Throws503()
    {
        Dictionary<string, object?> resolvedParams = new() { { "q", "hello" } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };

        DataApiBuilderException ex = await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
            () => ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
                resolvedParams, configParams, NullEmbeddingService.Instance, CancellationToken.None));

        Assert.AreEqual(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        StringAssert.Contains(ex.Message, "embed parameter");
        StringAssert.Contains(ex.Message, "embedding service");
    }

    /// <summary>
    /// When the embedding service is disabled (NullEmbeddingService) but no embed params
    /// are configured, the helper should NOT throw — the no-op early exit fires before
    /// the IsEnabled check. (Backward compat: existing entities without embed params
    /// work fine when embeddings are disabled.)
    /// </summary>
    [TestMethod]
    public async Task DisabledService_WithoutEmbedParams_NoThrow()
    {
        Dictionary<string, object?> resolvedParams = new() { { "x", "value" } };
        List<ParameterMetadata> configParams = new() { NormalParam("x") };

        // Should not throw
        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, NullEmbeddingService.Instance, CancellationToken.None);

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
        StringAssert.Contains(ex.Message, "auto-embed: true");
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

    #region Empty, Whitespace, Null, and Default Handling

    /// <summary>
    /// Per spec #3331 Value behavior: null, empty, whitespace-only, and JsonElement.Null
    /// all skip embedding and pass "" to the stored procedure. Consolidated into a single
    /// data-driven test to avoid repetition — all four hit the same IsNullOrWhiteSpace path.
    /// </summary>
    [DataTestMethod]
    [DataRow("empty", DisplayName = "empty string → passes empty string to sproc")]
    [DataRow("whitespace", DisplayName = "whitespace → passes empty string to sproc")]
    [DataRow("null", DisplayName = "C# null → passes empty string to sproc")]
    [DataRow("json-null", DisplayName = "JsonElement Null → passes empty string to sproc")]
    public async Task NullOrEmptyInput_PassesEmptyStringToSproc(string inputKind)
    {
        object? value = inputKind switch
        {
            "empty" => "",
            "whitespace" => "   ",
            "null" => null,
            "json-null" => JsonElementFrom("null"),
            _ => throw new ArgumentException($"Unknown input kind: {inputKind}")
        };

        Dictionary<string, object?> resolvedParams = new() { { "q", value } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None);

        Assert.AreEqual(string.Empty, resolvedParams["q"],
            $"Input kind '{inputKind}' should pass empty string to sproc without embedding");
    }

    /// <summary>
    /// Per spec #3331: when the caller omits an auto-embed param that has a non-empty
    /// configured default, DAB injects the default and embeds it.
    /// </summary>
    [TestMethod]
    public async Task DefaultValue_EmbeddedWhenCallerOmitsParam()
    {
        Dictionary<string, object?> resolvedParams = new();
        ParameterMetadata embedParam = new() { Name = "q", AutoEmbed = true, Default = "electronics" };
        List<ParameterMetadata> configParams = new() { embedParam };
        SetupBatch(new[] { 0.5f, 0.25f });

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None);

        // The default "electronics" was injected and embedded
        Assert.IsTrue(resolvedParams.ContainsKey("q"), "Default should be injected into resolvedParams");
        string result = (string)resolvedParams["q"]!;
        Assert.AreEqual("[0.5,0.25]", result, "Default should be embedded to vector JSON matching mock setup");

        // Verify the embedding service was called with the default text
        _mockService.Verify(
            s => s.TryEmbedBatchAsync(
                It.Is<string[]>(texts => texts.Length == 1 && texts[0] == "electronics"),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "Embedding service should be called with the default value 'electronics'");
    }

    /// <summary>
    /// Per spec #3331: when the caller omits a param whose configured default is empty,
    /// DAB injects "" without embedding.
    /// </summary>
    [TestMethod]
    public async Task EmptyDefault_PassesEmptyStringWhenCallerOmitsParam()
    {
        Dictionary<string, object?> resolvedParams = new();
        ParameterMetadata embedParam = new() { Name = "q", AutoEmbed = true, Default = "" };
        List<ParameterMetadata> configParams = new() { embedParam };

        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None);

        Assert.IsTrue(resolvedParams.ContainsKey("q"), "Empty default should inject \"\" into resolvedParams");
        Assert.AreEqual(string.Empty, resolvedParams["q"]);
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

    #region Telemetry

    /// <summary>
    /// On a successful substitution, the helper must emit an Activity span with
    /// expected telemetry tags: entity, sproc, param_names, outcome=success, and
    /// a non-negative duration_ms. This test uses an <see cref="ActivityListener"/>
    /// to capture the span created by the helper.
    /// </summary>
    [TestMethod]
    public async Task SuccessfulSubstitution_EmitsActivityWithExpectedTags()
    {
        // Arrange
        Dictionary<string, object?> resolvedParams = new() { { "q", "wireless headphones" } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };
        SetupBatch(new[] { 0.5f, 0.25f });

        Activity? captured = null;
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == "DataApiBuilder",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (a.OperationName == "AutoEmbed.Substitute")
                {
                    captured = a;
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        // Act
        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None,
            entityName: "SearchProducts",
            sprocName: "dbo.SearchProducts",
            provider: "azure-openai",
            model: "text-embedding-3-small");

        // Assert
        Assert.IsNotNull(captured, "Expected an AutoEmbed.Substitute activity to be emitted");
        Assert.AreEqual(ActivityStatusCode.Ok, captured.Status);
        Assert.AreEqual("SearchProducts", captured.GetTagItem("entity"));
        Assert.AreEqual("dbo.SearchProducts", captured.GetTagItem("sproc"));
        Assert.AreEqual("q", captured.GetTagItem("param_names"));
        Assert.AreEqual("success", captured.GetTagItem("outcome"));
        Assert.AreEqual("azure-openai", captured.GetTagItem("embedding.provider"));
        Assert.AreEqual("text-embedding-3-small", captured.GetTagItem("embedding.model"));
        Assert.IsNotNull(captured.GetTagItem("duration_ms"), "Expected duration_ms tag");
        Assert.IsTrue((double)captured.GetTagItem("duration_ms")! >= 0, "Duration should be non-negative");
        Assert.AreEqual(1, captured.GetTagItem("param_count"));
    }

    /// <summary>
    /// When the embedding batch fails, the helper must emit an Activity span with
    /// outcome=batch_failure and error tags (type + message).
    /// </summary>
    [TestMethod]
    public async Task FailedSubstitution_EmitsActivityWithErrorTags()
    {
        // Arrange
        Dictionary<string, object?> resolvedParams = new() { { "q", "hello" } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };
        _mockService
            .Setup(s => s.TryEmbedBatchAsync(It.IsAny<string[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EmbeddingBatchResult(false, null, "Quota exceeded"));

        Activity? captured = null;
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == "DataApiBuilder",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (a.OperationName == "AutoEmbed.Substitute")
                {
                    captured = a;
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        // Act + Assert (throws)
        await Assert.ThrowsExceptionAsync<DataApiBuilderException>(
            () => ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
                resolvedParams, configParams, _mockService.Object, CancellationToken.None,
                entityName: "SearchProducts"));

        // Assert activity
        Assert.IsNotNull(captured, "Expected an AutoEmbed.Substitute activity to be emitted on failure");
        Assert.AreEqual(ActivityStatusCode.Error, captured.Status);
        Assert.AreEqual("batch_failure", captured.GetTagItem("outcome"));
        Assert.AreEqual("DataApiBuilderException", captured.GetTagItem("error.type"));
        StringAssert.Contains((string)captured.GetTagItem("error.message")!, "Quota exceeded");
    }

    /// <summary>
    /// The telemetry must NEVER include the original input text or the embedding
    /// vector values in Activity tags, events, or status descriptions. This is
    /// an explicit spec requirement (#3331, "Never include" section) to prevent
    /// sensitive user data from leaking into trace backends.
    /// </summary>
    [TestMethod]
    public async Task Telemetry_NeverIncludesInputTextOrEmbeddingValue()
    {
        // Arrange — use distinctive text and vector values that are easy to search for
        string sensitiveInput = "SENSITIVE_USER_SEARCH_QUERY_12345";
        float[] sensitiveVector = new[] { 0.999f, -0.888f, 0.777f };
        Dictionary<string, object?> resolvedParams = new() { { "q", sensitiveInput } };
        List<ParameterMetadata> configParams = new() { EmbedParam("q") };
        SetupBatch(sensitiveVector);

        Activity? captured = null;
        using ActivityListener listener = new()
        {
            ShouldListenTo = source => source.Name == "DataApiBuilder",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (a.OperationName == "AutoEmbed.Substitute")
                {
                    captured = a;
                }
            }
        };
        ActivitySource.AddActivityListener(listener);

        // Act
        await ParameterEmbeddingHelper.SubstituteEmbedParametersAsync(
            resolvedParams, configParams, _mockService.Object, CancellationToken.None,
            entityName: "TestEntity");

        // Assert
        Assert.IsNotNull(captured, "Expected an activity to be captured");

        // Collect ALL tag values and the status description into one searchable string
        string allTagValues = string.Join("|",
            captured.Tags.Select(t => $"{t.Key}={t.Value}"));
        string statusDesc = captured.StatusDescription ?? string.Empty;
        string allEvents = string.Join("|",
            captured.Events.SelectMany(e => e.Tags.Select(t => $"{t.Key}={t.Value}")));
        string fullTrace = $"{allTagValues}|{statusDesc}|{allEvents}";

        // The original input text must not appear anywhere in the trace
        Assert.IsFalse(fullTrace.Contains(sensitiveInput),
            $"Telemetry must not contain original input text. Found in: {fullTrace}");

        // The embedding vector values must not appear anywhere in the trace
        string vectorJson = resolvedParams["q"] as string ?? string.Empty;
        Assert.IsFalse(fullTrace.Contains(vectorJson),
            $"Telemetry must not contain embedding vector values. Found in: {fullTrace}");

        // Individual float components should also not appear
        foreach (float f in sensitiveVector)
        {
            string floatStr = f.ToString("G9", CultureInfo.InvariantCulture);
            Assert.IsFalse(fullTrace.Contains(floatStr),
                $"Telemetry must not contain embedding component '{floatStr}'. Found in: {fullTrace}");
        }
    }

    #endregion
}
