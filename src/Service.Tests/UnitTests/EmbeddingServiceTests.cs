// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Azure.DataApiBuilder.Core.Services.Embeddings;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;
using ZiggyCreatures.Caching.Fusion;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for EmbeddingService.
/// </summary>
[TestClass]
public class EmbeddingServiceTests
{
    private Mock<ILogger<EmbeddingService>> _mockLogger = null!;
    private Mock<IFusionCache> _mockCache = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<EmbeddingService>>();
        _mockCache = new Mock<IFusionCache>();
    }

    /// <summary>
    /// Tests that IsEnabled returns true when embeddings are enabled.
    /// </summary>
    [TestMethod]
    public void IsEnabled_ReturnsTrue_WhenEnabled()
    {
        // Arrange
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        HttpClient httpClient = new();
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, _mockCache.Object);

        // Assert
        Assert.IsTrue(service.IsEnabled);
    }

    /// <summary>
    /// Tests that IsEnabled returns false when embeddings are disabled.
    /// </summary>
    [TestMethod]
    public void IsEnabled_ReturnsFalse_WhenDisabled()
    {
        // Arrange
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.AzureOpenAI,
            BaseUrl: "https://test.openai.azure.com",
            ApiKey: "test-api-key",
            Enabled: false,
            Model: "text-embedding-ada-002");
        HttpClient httpClient = new();
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, _mockCache.Object);

        // Assert
        Assert.IsFalse(service.IsEnabled);
    }

    /// <summary>
    /// Tests that TryEmbedAsync returns failure when service is disabled.
    /// </summary>
    [TestMethod]
    public async Task TryEmbedAsync_ReturnsFailure_WhenDisabled()
    {
        // Arrange
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.AzureOpenAI,
            BaseUrl: "https://test.openai.azure.com",
            ApiKey: "test-api-key",
            Enabled: false,
            Model: "text-embedding-ada-002");
        HttpClient httpClient = new();
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, _mockCache.Object);

        // Act
        EmbeddingResult result = await service.TryEmbedAsync("test");

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Embedding);
        Assert.IsNotNull(result.ErrorMessage);
    }

    /// <summary>
    /// Tests that TryEmbedAsync returns failure for null or empty text.
    /// </summary>
    [DataTestMethod]
    [DataRow(null, DisplayName = "Null text returns failure")]
    [DataRow("", DisplayName = "Empty text returns failure")]
    public async Task TryEmbedAsync_ReturnsFailure_ForNullOrEmptyText(string text)
    {
        // Arrange
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        HttpClient httpClient = new();
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, _mockCache.Object);

        // Act
        EmbeddingResult result = await service.TryEmbedAsync(text!);

        // Assert
        Assert.IsFalse(result.Success);
    }

    /// <summary>
    /// Tests that EffectiveModel returns the default model for OpenAI when not specified.
    /// </summary>
    [TestMethod]
    public void EmbeddingsOptions_OpenAI_DefaultModel()
    {
        // Arrange
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: "https://api.openai.com",
            ApiKey: "test-key");

        // Assert
        Assert.IsNull(options.Model);
        Assert.AreEqual(EmbeddingsOptions.DEFAULT_OPENAI_MODEL, options.EffectiveModel);
    }

    /// <summary>
    /// Tests that EffectiveModel returns null for Azure OpenAI when model not specified.
    /// </summary>
    [TestMethod]
    public void EmbeddingsOptions_AzureOpenAI_NoDefaultModel()
    {
        // Arrange
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.AzureOpenAI,
            BaseUrl: "https://my.openai.azure.com",
            ApiKey: "test-key");

        // Assert
        Assert.IsNull(options.Model);
        Assert.IsNull(options.EffectiveModel);
    }

    /// <summary>
    /// Tests that EffectiveTimeoutMs returns the default timeout when not specified.
    /// </summary>
    [TestMethod]
    public void EmbeddingsOptions_DefaultTimeout()
    {
        // Arrange
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: "https://api.openai.com",
            ApiKey: "test-key");

        // Assert
        Assert.IsNull(options.TimeoutMs);
        Assert.AreEqual(EmbeddingsOptions.DEFAULT_TIMEOUT_MS, options.EffectiveTimeoutMs);
    }

    /// <summary>
    /// Tests that custom timeout is used when specified.
    /// </summary>
    [TestMethod]
    public void EmbeddingsOptions_CustomTimeout()
    {
        // Arrange
        int customTimeout = 60000;
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: "https://api.openai.com",
            ApiKey: "test-key",
            TimeoutMs: customTimeout);

        // Assert
        Assert.AreEqual(customTimeout, options.TimeoutMs);
        Assert.AreEqual(customTimeout, options.EffectiveTimeoutMs);
        Assert.IsTrue(options.UserProvidedTimeoutMs);
    }

    #region Successful API Call Tests

    /// <summary>
    /// Tests that TryEmbedAsync returns a successful result with correct embedding values
    /// when the Azure OpenAI API returns a valid response.
    /// </summary>
    [TestMethod]
    public async Task TryEmbedAsync_ReturnsSuccess_WithValidAzureOpenAIResponse()
    {
        // Arrange
        float[] expectedEmbedding = new[] { 0.1f, 0.2f, 0.3f };
        string responseJson = CreateEmbeddingResponseJson(expectedEmbedding);

        Mock<HttpMessageHandler> mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.OK, responseJson);
        HttpClient httpClient = new(mockHandler.Object);

        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        EmbeddingResult result = await service.TryEmbedAsync("test text");

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Embedding);
        Assert.IsNull(result.ErrorMessage);
        CollectionAssert.AreEqual(expectedEmbedding, result.Embedding);

        // Verify HTTP call was made
        mockHandler.Protected().Verify(
            "SendAsync",
            Times.Once(),
            ItExpr.IsAny<HttpRequestMessage>(),
            ItExpr.IsAny<CancellationToken>());
    }

    /// <summary>
    /// Tests that TryEmbedAsync returns a successful result with correct embedding values
    /// when the OpenAI API returns a valid response.
    /// </summary>
    [TestMethod]
    public async Task TryEmbedAsync_ReturnsSuccess_WithValidOpenAIResponse()
    {
        // Arrange
        float[] expectedEmbedding = new[] { 0.4f, 0.5f, 0.6f, 0.7f };
        string responseJson = CreateEmbeddingResponseJson(expectedEmbedding);

        Mock<HttpMessageHandler> mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.OK, responseJson);
        HttpClient httpClient = new(mockHandler.Object);

        EmbeddingsOptions options = CreateOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        EmbeddingResult result = await service.TryEmbedAsync("test text");

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Embedding);
        CollectionAssert.AreEqual(expectedEmbedding, result.Embedding);
    }

    /// <summary>
    /// Tests that EmbedAsync returns the expected embedding array on a successful API call.
    /// </summary>
    [TestMethod]
    public async Task EmbedAsync_ReturnsEmbedding_OnSuccessfulApiCall()
    {
        // Arrange
        float[] expectedEmbedding = new[] { 1.0f, 2.0f, 3.0f };
        string responseJson = CreateEmbeddingResponseJson(expectedEmbedding);

        Mock<HttpMessageHandler> mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.OK, responseJson);
        HttpClient httpClient = new(mockHandler.Object);

        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        float[] result = await service.EmbedAsync("test text");

        // Assert
        CollectionAssert.AreEqual(expectedEmbedding, result);
    }

    #endregion

    #region HTTP Error Handling Tests

    /// <summary>
    /// Tests that TryEmbedAsync returns failure with error message when the API returns an HTTP error.
    /// </summary>
    [DataTestMethod]
    [DataRow(HttpStatusCode.BadRequest, "Bad Request", DisplayName = "400 Bad Request")]
    [DataRow(HttpStatusCode.Unauthorized, "Invalid API key", DisplayName = "401 Unauthorized")]
    [DataRow(HttpStatusCode.TooManyRequests, "Rate limit exceeded", DisplayName = "429 Too Many Requests")]
    [DataRow(HttpStatusCode.InternalServerError, "Internal server error", DisplayName = "500 Internal Server Error")]
    public async Task TryEmbedAsync_ReturnsFailure_OnHttpError(HttpStatusCode statusCode, string errorBody)
    {
        // Arrange
        Mock<HttpMessageHandler> mockHandler = CreateMockHttpMessageHandler(statusCode, errorBody);
        HttpClient httpClient = new(mockHandler.Object);

        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        EmbeddingResult result = await service.TryEmbedAsync("test text");

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Embedding);
        Assert.IsNotNull(result.ErrorMessage);
        // The error message contains the StatusCode enum name (e.g., "BadRequest") and the error body
        Assert.IsTrue(result.ErrorMessage.Contains(statusCode.ToString()),
            $"Error message should contain status code name '{statusCode}'. Actual: {result.ErrorMessage}");
        Assert.IsTrue(result.ErrorMessage.Contains(errorBody),
            $"Error message should contain error body '{errorBody}'. Actual: {result.ErrorMessage}");
    }

    /// <summary>
    /// Tests that EmbedAsync throws an exception when the API returns an HTTP error.
    /// </summary>
    [TestMethod]
    public async Task EmbedAsync_ThrowsException_OnHttpError()
    {
        // Arrange
        Mock<HttpMessageHandler> mockHandler = CreateMockHttpMessageHandler(
            HttpStatusCode.InternalServerError, "Server error");
        HttpClient httpClient = new(mockHandler.Object);

        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act & Assert
        await Assert.ThrowsExceptionAsync<HttpRequestException>(
            () => service.EmbedAsync("test text"));
    }

    #endregion

    #region Response Parsing and Validation Tests

    /// <summary>
    /// Tests that TryEmbedAsync returns failure when the API returns an empty data array.
    /// </summary>
    [TestMethod]
    public async Task TryEmbedAsync_ReturnsFailure_WhenApiReturnsEmptyData()
    {
        // Arrange
        string responseJson = JsonSerializer.Serialize(new { data = Array.Empty<object>(), model = "test" });

        Mock<HttpMessageHandler> mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.OK, responseJson);
        HttpClient httpClient = new(mockHandler.Object);

        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        EmbeddingResult result = await service.TryEmbedAsync("test text");

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.ErrorMessage);
    }

    /// <summary>
    /// Tests that TryEmbedAsync returns failure when the API returns null data.
    /// </summary>
    [TestMethod]
    public async Task TryEmbedAsync_ReturnsFailure_WhenApiReturnsNullData()
    {
        // Arrange
        string responseJson = JsonSerializer.Serialize(new { model = "test" });

        Mock<HttpMessageHandler> mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.OK, responseJson);
        HttpClient httpClient = new(mockHandler.Object);

        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        EmbeddingResult result = await service.TryEmbedAsync("test text");

        // Assert
        Assert.IsFalse(result.Success);
    }

    /// <summary>
    /// Tests that TryEmbedBatchAsync returns failure when the API returns a mismatched number
    /// of embeddings compared to the input count.
    /// </summary>
    [TestMethod]
    public async Task TryEmbedBatchAsync_ReturnsFailure_WhenEmbeddingCountMismatches()
    {
        // Arrange - send 2 texts but API returns 1 embedding
        string responseJson = CreateEmbeddingResponseJson(new[] { 0.1f, 0.2f }); // single embedding

        Mock<HttpMessageHandler> mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.OK, responseJson);
        HttpClient httpClient = new(mockHandler.Object);

        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        EmbeddingBatchResult result = await service.TryEmbedBatchAsync(new[] { "text1", "text2" });

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.ErrorMessage);
    }

    /// <summary>
    /// Tests that TryEmbedBatchAsync returns failure when the API returns out-of-range indices.
    /// </summary>
    [TestMethod]
    public async Task TryEmbedBatchAsync_ReturnsFailure_WhenIndicesOutOfRange()
    {
        // Arrange - 1 text but embedding has index 5
        string responseJson = CreateEmbeddingResponseJsonWithIndices(
            new[] { (5, new[] { 0.1f, 0.2f }) });

        Mock<HttpMessageHandler> mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.OK, responseJson);
        HttpClient httpClient = new(mockHandler.Object);

        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        EmbeddingBatchResult result = await service.TryEmbedBatchAsync(new[] { "text1" });

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.ErrorMessage);
    }

    /// <summary>
    /// Tests that TryEmbedBatchAsync returns failure when the API returns duplicate indices.
    /// </summary>
    [TestMethod]
    public async Task TryEmbedBatchAsync_ReturnsFailure_WhenDuplicateIndices()
    {
        // Arrange - 2 texts but both embeddings have index 0
        string responseJson = CreateEmbeddingResponseJsonWithIndices(
            new[] { (0, new[] { 0.1f, 0.2f }), (0, new[] { 0.3f, 0.4f }) });

        Mock<HttpMessageHandler> mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.OK, responseJson);
        HttpClient httpClient = new(mockHandler.Object);

        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        EmbeddingBatchResult result = await service.TryEmbedBatchAsync(new[] { "text1", "text2" });

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNotNull(result.ErrorMessage);
    }

    /// <summary>
    /// Tests that batch embeddings are returned in the correct order even when the API
    /// returns them out of order (by index).
    /// </summary>
    [TestMethod]
    public async Task TryEmbedBatchAsync_ReturnsCorrectOrder_WhenApiReturnsOutOfOrder()
    {
        // Arrange - API returns index 1 before index 0
        float[] embedding0 = new[] { 0.1f, 0.2f };
        float[] embedding1 = new[] { 0.3f, 0.4f };
        string responseJson = CreateEmbeddingResponseJsonWithIndices(
            new[] { (1, embedding1), (0, embedding0) });

        Mock<HttpMessageHandler> mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.OK, responseJson);
        HttpClient httpClient = new(mockHandler.Object);

        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        EmbeddingBatchResult result = await service.TryEmbedBatchAsync(new[] { "text0", "text1" });

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Embeddings);
        Assert.AreEqual(2, result.Embeddings.Length);
        CollectionAssert.AreEqual(embedding0, result.Embeddings[0]);
        CollectionAssert.AreEqual(embedding1, result.Embeddings[1]);
    }

    #endregion

    #region Cache Hit/Miss Tests

    /// <summary>
    /// Tests that the second call to TryEmbedAsync with the same text returns the cached result
    /// and does not make a second API call.
    /// </summary>
    [TestMethod]
    public async Task TryEmbedAsync_ReturnsCachedResult_OnSecondCallWithSameText()
    {
        // Arrange
        float[] expectedEmbedding = new[] { 0.1f, 0.2f, 0.3f };
        string responseJson = CreateEmbeddingResponseJson(expectedEmbedding);

        int callCount = 0;
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                };
            });

        HttpClient httpClient = new(mockHandler.Object);
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act - first call triggers API
        EmbeddingResult result1 = await service.TryEmbedAsync("same text");
        // Act - second call should use cache
        EmbeddingResult result2 = await service.TryEmbedAsync("same text");

        // Assert
        Assert.IsTrue(result1.Success);
        Assert.IsTrue(result2.Success);
        CollectionAssert.AreEqual(expectedEmbedding, result1.Embedding);
        CollectionAssert.AreEqual(expectedEmbedding, result2.Embedding);
        Assert.AreEqual(1, callCount, "HTTP API should only be called once; second call should use cache.");
    }

    /// <summary>
    /// Tests that different texts result in separate API calls (cache misses).
    /// </summary>
    [TestMethod]
    public async Task TryEmbedAsync_MakesSeparateApiCalls_ForDifferentTexts()
    {
        // Arrange
        float[] embedding1 = new[] { 0.1f, 0.2f };
        float[] embedding2 = new[] { 0.3f, 0.4f };

        int callCount = 0;
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(() =>
            {
                callCount++;
                float[] embedding = callCount == 1 ? embedding1 : embedding2;
                string json = CreateEmbeddingResponseJson(embedding);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            });

        HttpClient httpClient = new(mockHandler.Object);
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        EmbeddingResult result1 = await service.TryEmbedAsync("text one");
        EmbeddingResult result2 = await service.TryEmbedAsync("text two");

        // Assert
        Assert.IsTrue(result1.Success);
        Assert.IsTrue(result2.Success);
        Assert.AreEqual(2, callCount, "Each unique text should trigger a separate API call.");
    }

    #endregion

    #region Batch Embedding Tests

    /// <summary>
    /// Tests that TryEmbedBatchAsync returns success with correct embeddings for multiple texts.
    /// </summary>
    [TestMethod]
    public async Task TryEmbedBatchAsync_ReturnsSuccess_ForMultipleTexts()
    {
        // Arrange
        float[] embedding0 = new[] { 0.1f, 0.2f };
        float[] embedding1 = new[] { 0.3f, 0.4f };
        float[] embedding2 = new[] { 0.5f, 0.6f };

        string responseJson = CreateEmbeddingResponseJsonWithIndices(new[]
        {
            (0, embedding0),
            (1, embedding1),
            (2, embedding2)
        });

        Mock<HttpMessageHandler> mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.OK, responseJson);
        HttpClient httpClient = new(mockHandler.Object);

        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        EmbeddingBatchResult result = await service.TryEmbedBatchAsync(new[] { "text0", "text1", "text2" });

        // Assert
        Assert.IsTrue(result.Success);
        Assert.IsNotNull(result.Embeddings);
        Assert.AreEqual(3, result.Embeddings.Length);
        CollectionAssert.AreEqual(embedding0, result.Embeddings[0]);
        CollectionAssert.AreEqual(embedding1, result.Embeddings[1]);
        CollectionAssert.AreEqual(embedding2, result.Embeddings[2]);
    }

    /// <summary>
    /// Tests that TryEmbedBatchAsync returns failure when the service is disabled.
    /// </summary>
    [TestMethod]
    public async Task TryEmbedBatchAsync_ReturnsFailure_WhenDisabled()
    {
        // Arrange
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.AzureOpenAI,
            BaseUrl: "https://test.openai.azure.com",
            ApiKey: "test-api-key",
            Enabled: false,
            Model: "text-embedding-ada-002");
        HttpClient httpClient = new();
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, _mockCache.Object);

        // Act
        EmbeddingBatchResult result = await service.TryEmbedBatchAsync(new[] { "text1" });

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Embeddings);
        Assert.IsNotNull(result.ErrorMessage);
    }

    /// <summary>
    /// Tests that TryEmbedBatchAsync returns failure for null texts array.
    /// </summary>
    [TestMethod]
    public async Task TryEmbedBatchAsync_ReturnsFailure_ForNullTexts()
    {
        // Arrange
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        HttpClient httpClient = new();
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, _mockCache.Object);

        // Act
        EmbeddingBatchResult result = await service.TryEmbedBatchAsync(null!);

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Embeddings);
    }

    /// <summary>
    /// Tests that TryEmbedBatchAsync returns failure for empty texts array.
    /// </summary>
    [TestMethod]
    public async Task TryEmbedBatchAsync_ReturnsFailure_ForEmptyTexts()
    {
        // Arrange
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        HttpClient httpClient = new();
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, _mockCache.Object);

        // Act
        EmbeddingBatchResult result = await service.TryEmbedBatchAsync(Array.Empty<string>());

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Embeddings);
    }

    /// <summary>
    /// Tests that EmbedBatchAsync throws when the service is disabled.
    /// </summary>
    [TestMethod]
    public async Task EmbedBatchAsync_Throws_WhenDisabled()
    {
        // Arrange
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.AzureOpenAI,
            BaseUrl: "https://test.openai.azure.com",
            ApiKey: "test-api-key",
            Enabled: false,
            Model: "text-embedding-ada-002");
        HttpClient httpClient = new();
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, _mockCache.Object);

        // Act & Assert
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(
            () => service.EmbedBatchAsync(new[] { "text1" }));
    }

    /// <summary>
    /// Tests that EmbedBatchAsync uses cached results for previously embedded texts
    /// and only calls the API for uncached texts.
    /// </summary>
    [TestMethod]
    public async Task EmbedBatchAsync_OnlyCallsApiForUncachedTexts()
    {
        // Arrange
        float[] embeddingA = new[] { 0.1f, 0.2f };
        float[] embeddingB = new[] { 0.3f, 0.4f };
        float[] embeddingC = new[] { 0.5f, 0.6f };

        int apiCallCount = 0;
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                apiCallCount++;
                string body = request.Content!.ReadAsStringAsync().Result;

                string json;
                if (apiCallCount == 1)
                {
                    // First call embeds "textA" via TryEmbedAsync
                    json = CreateEmbeddingResponseJson(embeddingA);
                }
                else
                {
                    // Second call should only embed "textB" and "textC" (textA is cached)
                    json = CreateEmbeddingResponseJsonWithIndices(new[]
                    {
                        (0, embeddingB),
                        (1, embeddingC)
                    });
                }

                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
            });

        HttpClient httpClient = new(mockHandler.Object);
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // First: embed "textA" so it's cached
        EmbeddingResult preResult = await service.TryEmbedAsync("textA");
        Assert.IsTrue(preResult.Success);
        Assert.AreEqual(1, apiCallCount);

        // Act: batch embed ["textA", "textB", "textC"] - textA should come from cache
        float[][] batchResults = await service.EmbedBatchAsync(new[] { "textA", "textB", "textC" });

        // Assert
        Assert.AreEqual(2, apiCallCount, "Only 1 additional API call should be made for the 2 uncached texts.");
        Assert.AreEqual(3, batchResults.Length);
        CollectionAssert.AreEqual(embeddingA, batchResults[0], "textA should come from cache.");
        CollectionAssert.AreEqual(embeddingB, batchResults[1]);
        CollectionAssert.AreEqual(embeddingC, batchResults[2]);
    }

    #endregion

    #region Provider-Specific URL Construction Tests

    /// <summary>
    /// Tests that the Azure OpenAI provider constructs the correct URL format:
    /// {baseUrl}/openai/deployments/{deployment}/embeddings?api-version={version}
    /// </summary>
    [TestMethod]
    public async Task AzureOpenAI_BuildsCorrectRequestUrl()
    {
        // Arrange
        float[] expectedEmbedding = new[] { 0.1f, 0.2f };
        string responseJson = CreateEmbeddingResponseJson(expectedEmbedding);

        Uri capturedUri = null!;
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                capturedUri = request.RequestUri!;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                };
            });

        HttpClient httpClient = new(mockHandler.Object);
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.AzureOpenAI,
            BaseUrl: "https://myservice.openai.azure.com",
            ApiKey: "test-key",
            Model: "my-deployment",
            ApiVersion: "2024-06-01");

        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        await service.TryEmbedAsync("test");

        // Assert
        Assert.IsNotNull(capturedUri);
        Assert.AreEqual(
            "https://myservice.openai.azure.com/openai/deployments/my-deployment/embeddings?api-version=2024-06-01",
            capturedUri.ToString());
    }

    /// <summary>
    /// Tests that the OpenAI provider constructs the correct URL format:
    /// {baseUrl}/v1/embeddings
    /// </summary>
    [TestMethod]
    public async Task OpenAI_BuildsCorrectRequestUrl()
    {
        // Arrange
        float[] expectedEmbedding = new[] { 0.1f, 0.2f };
        string responseJson = CreateEmbeddingResponseJson(expectedEmbedding);

        Uri capturedUri = null!;
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                capturedUri = request.RequestUri!;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                };
            });

        HttpClient httpClient = new(mockHandler.Object);
        EmbeddingsOptions options = CreateOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        await service.TryEmbedAsync("test");

        // Assert
        Assert.IsNotNull(capturedUri);
        Assert.AreEqual("https://api.openai.com/v1/embeddings", capturedUri.ToString());
    }

    /// <summary>
    /// Tests that Azure OpenAI uses the default API version when none is specified.
    /// </summary>
    [TestMethod]
    public async Task AzureOpenAI_UsesDefaultApiVersion_WhenNotSpecified()
    {
        // Arrange
        float[] expectedEmbedding = new[] { 0.1f, 0.2f };
        string responseJson = CreateEmbeddingResponseJson(expectedEmbedding);

        Uri capturedUri = null!;
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                capturedUri = request.RequestUri!;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                };
            });

        HttpClient httpClient = new(mockHandler.Object);
        EmbeddingsOptions options = CreateAzureOpenAIOptions(); // no explicit api-version
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        await service.TryEmbedAsync("test");

        // Assert
        Assert.IsNotNull(capturedUri);
        Assert.IsTrue(capturedUri.ToString().Contains($"api-version={EmbeddingsOptions.DEFAULT_AZURE_API_VERSION}"));
    }

    #endregion

    #region Request Body Building Tests

    /// <summary>
    /// Tests that the OpenAI request body includes the model name.
    /// </summary>
    [TestMethod]
    public async Task OpenAI_RequestBody_IncludesModel()
    {
        // Arrange
        float[] expectedEmbedding = new[] { 0.1f, 0.2f };
        string responseJson = CreateEmbeddingResponseJson(expectedEmbedding);

        string capturedRequestBody = null!;
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                capturedRequestBody = request.Content!.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                };
            });

        HttpClient httpClient = new(mockHandler.Object);
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: "https://api.openai.com",
            ApiKey: "test-key",
            Model: "text-embedding-3-large");
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        await service.TryEmbedAsync("test");

        // Assert
        Assert.IsNotNull(capturedRequestBody);
        using JsonDocument doc = JsonDocument.Parse(capturedRequestBody);
        Assert.IsTrue(doc.RootElement.TryGetProperty("model", out JsonElement modelElement));
        Assert.AreEqual("text-embedding-3-large", modelElement.GetString());
    }

    /// <summary>
    /// Tests that the Azure OpenAI request body does NOT include the model name
    /// (it's in the URL as the deployment name instead).
    /// </summary>
    [TestMethod]
    public async Task AzureOpenAI_RequestBody_DoesNotIncludeModel()
    {
        // Arrange
        float[] expectedEmbedding = new[] { 0.1f, 0.2f };
        string responseJson = CreateEmbeddingResponseJson(expectedEmbedding);

        string capturedRequestBody = null!;
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                capturedRequestBody = request.Content!.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                };
            });

        HttpClient httpClient = new(mockHandler.Object);
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        await service.TryEmbedAsync("test");

        // Assert
        Assert.IsNotNull(capturedRequestBody);
        using JsonDocument doc = JsonDocument.Parse(capturedRequestBody);
        Assert.IsFalse(doc.RootElement.TryGetProperty("model", out _),
            "Azure OpenAI request body should not contain 'model' property.");
    }

    /// <summary>
    /// Tests that dimensions are included in the request body when specified.
    /// </summary>
    [TestMethod]
    public async Task RequestBody_IncludesDimensions_WhenSpecified()
    {
        // Arrange
        float[] expectedEmbedding = new[] { 0.1f, 0.2f };
        string responseJson = CreateEmbeddingResponseJson(expectedEmbedding);

        string capturedRequestBody = null!;
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                capturedRequestBody = request.Content!.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                };
            });

        HttpClient httpClient = new(mockHandler.Object);
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: "https://api.openai.com",
            ApiKey: "test-key",
            Dimensions: 256);
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        await service.TryEmbedAsync("test");

        // Assert
        Assert.IsNotNull(capturedRequestBody);
        using JsonDocument doc = JsonDocument.Parse(capturedRequestBody);
        Assert.IsTrue(doc.RootElement.TryGetProperty("dimensions", out JsonElement dimElement));
        Assert.AreEqual(256, dimElement.GetInt32());
    }

    /// <summary>
    /// Tests that dimensions are NOT included in the request body when not specified.
    /// </summary>
    [TestMethod]
    public async Task RequestBody_ExcludesDimensions_WhenNotSpecified()
    {
        // Arrange
        float[] expectedEmbedding = new[] { 0.1f, 0.2f };
        string responseJson = CreateEmbeddingResponseJson(expectedEmbedding);

        string capturedRequestBody = null!;
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                capturedRequestBody = request.Content!.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                };
            });

        HttpClient httpClient = new(mockHandler.Object);
        EmbeddingsOptions options = CreateAzureOpenAIOptions(); // no dimensions
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        await service.TryEmbedAsync("test");

        // Assert
        Assert.IsNotNull(capturedRequestBody);
        using JsonDocument doc = JsonDocument.Parse(capturedRequestBody);
        Assert.IsFalse(doc.RootElement.TryGetProperty("dimensions", out _),
            "Request body should not contain 'dimensions' when not specified.");
    }

    /// <summary>
    /// Tests that a single text is sent as a string (not an array) in the request body.
    /// </summary>
    [TestMethod]
    public async Task RequestBody_SendsSingleTextAsString()
    {
        // Arrange
        float[] expectedEmbedding = new[] { 0.1f, 0.2f };
        string responseJson = CreateEmbeddingResponseJson(expectedEmbedding);

        string capturedRequestBody = null!;
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                capturedRequestBody = request.Content!.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                };
            });

        HttpClient httpClient = new(mockHandler.Object);
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        await service.TryEmbedAsync("single text input");

        // Assert
        Assert.IsNotNull(capturedRequestBody);
        using JsonDocument doc = JsonDocument.Parse(capturedRequestBody);
        Assert.IsTrue(doc.RootElement.TryGetProperty("input", out JsonElement inputElement));
        Assert.AreEqual(JsonValueKind.String, inputElement.ValueKind,
            "Single text should be sent as a string, not an array.");
        Assert.AreEqual("single text input", inputElement.GetString());
    }

    /// <summary>
    /// Tests that multiple texts in a batch are sent as an array in the request body.
    /// </summary>
    [TestMethod]
    public async Task RequestBody_SendsBatchTextsAsArray()
    {
        // Arrange
        string responseJson = CreateEmbeddingResponseJsonWithIndices(new[]
        {
            (0, new[] { 0.1f, 0.2f }),
            (1, new[] { 0.3f, 0.4f })
        });

        string capturedRequestBody = null!;
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                capturedRequestBody = request.Content!.ReadAsStringAsync().Result;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                };
            });

        HttpClient httpClient = new(mockHandler.Object);
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        await service.TryEmbedBatchAsync(new[] { "text one", "text two" });

        // Assert
        Assert.IsNotNull(capturedRequestBody);
        using JsonDocument doc = JsonDocument.Parse(capturedRequestBody);
        Assert.IsTrue(doc.RootElement.TryGetProperty("input", out JsonElement inputElement));
        Assert.AreEqual(JsonValueKind.Array, inputElement.ValueKind,
            "Batch texts should be sent as an array.");
        Assert.AreEqual(2, inputElement.GetArrayLength());
    }

    #endregion

    #region Authentication Header Tests

    /// <summary>
    /// Tests that Azure OpenAI uses the api-key header for authentication.
    /// </summary>
    [TestMethod]
    public async Task AzureOpenAI_UsesApiKeyHeader()
    {
        // Arrange
        float[] expectedEmbedding = new[] { 0.1f, 0.2f };
        string responseJson = CreateEmbeddingResponseJson(expectedEmbedding);

        HttpRequestMessage capturedRequest = null!;
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync((HttpRequestMessage request, CancellationToken _) =>
            {
                capturedRequest = request;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
                };
            });

        HttpClient httpClient = new(mockHandler.Object);
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.AzureOpenAI,
            BaseUrl: "https://test.openai.azure.com",
            ApiKey: "my-azure-key",
            Model: "text-embedding-ada-002");
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        await service.TryEmbedAsync("test");

        // Assert
        Assert.IsTrue(httpClient.DefaultRequestHeaders.Contains("api-key"),
            "Azure OpenAI should use api-key header.");
        IEnumerable<string> values = httpClient.DefaultRequestHeaders.GetValues("api-key");
        Assert.AreEqual("my-azure-key", values.First());
    }

    /// <summary>
    /// Tests that OpenAI uses the Bearer token Authorization header.
    /// </summary>
    [TestMethod]
    public async Task OpenAI_UsesBearerAuthorizationHeader()
    {
        // Arrange
        float[] expectedEmbedding = new[] { 0.1f, 0.2f };
        string responseJson = CreateEmbeddingResponseJson(expectedEmbedding);

        Mock<HttpMessageHandler> mockHandler = CreateMockHttpMessageHandler(HttpStatusCode.OK, responseJson);
        HttpClient httpClient = new(mockHandler.Object);

        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: "https://api.openai.com",
            ApiKey: "my-openai-key");
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        await service.TryEmbedAsync("test");

        // Assert
        Assert.IsNotNull(httpClient.DefaultRequestHeaders.Authorization);
        Assert.AreEqual("Bearer", httpClient.DefaultRequestHeaders.Authorization.Scheme);
        Assert.AreEqual("my-openai-key", httpClient.DefaultRequestHeaders.Authorization.Parameter);
    }

    #endregion

    #region Timeout Tests

    /// <summary>
    /// Tests that TryEmbedAsync returns failure when the HTTP request times out.
    /// </summary>
    [TestMethod]
    public async Task TryEmbedAsync_ReturnsFailure_OnTimeout()
    {
        // Arrange
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout."));

        HttpClient httpClient = new(mockHandler.Object);
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        using IFusionCache cache = new FusionCache(new FusionCacheOptions());
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, cache);

        // Act
        EmbeddingResult result = await service.TryEmbedAsync("test text");

        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsNull(result.Embedding);
        Assert.IsNotNull(result.ErrorMessage);
    }

    /// <summary>
    /// Tests that the HttpClient timeout is set from the EmbeddingsOptions configuration.
    /// </summary>
    [TestMethod]
    public void Constructor_SetsHttpClientTimeout_FromOptions()
    {
        // Arrange
        int customTimeoutMs = 15000;
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.AzureOpenAI,
            BaseUrl: "https://test.openai.azure.com",
            ApiKey: "test-key",
            Model: "text-embedding-ada-002",
            TimeoutMs: customTimeoutMs);
        HttpClient httpClient = new();

        // Act
        EmbeddingService service = new(httpClient, options, _mockLogger.Object, _mockCache.Object);

        // Assert
        Assert.AreEqual(TimeSpan.FromMilliseconds(customTimeoutMs), httpClient.Timeout);
    }

    #endregion

    #region Constructor Validation Tests

    /// <summary>
    /// Tests that constructor throws when BaseUrl is empty.
    /// </summary>
    [TestMethod]
    public void Constructor_Throws_WhenBaseUrlIsEmpty()
    {
        // Arrange & Act & Assert
        Assert.ThrowsException<ArgumentException>(() =>
            new EmbeddingService(
                new HttpClient(),
                new EmbeddingsOptions(
                    Provider: EmbeddingProviderType.OpenAI,
                    BaseUrl: "",
                    ApiKey: "key"),
                _mockLogger.Object,
                _mockCache.Object));
    }

    /// <summary>
    /// Tests that constructor throws when ApiKey is empty.
    /// </summary>
    [TestMethod]
    public void Constructor_Throws_WhenApiKeyIsEmpty()
    {
        // Arrange & Act & Assert
        Assert.ThrowsException<ArgumentException>(() =>
            new EmbeddingService(
                new HttpClient(),
                new EmbeddingsOptions(
                    Provider: EmbeddingProviderType.OpenAI,
                    BaseUrl: "https://api.openai.com",
                    ApiKey: ""),
                _mockLogger.Object,
                _mockCache.Object));
    }

    /// <summary>
    /// Tests that constructor throws when Azure OpenAI provider is used without a model.
    /// </summary>
    [TestMethod]
    public void Constructor_Throws_WhenAzureOpenAIHasNoModel()
    {
        // Arrange & Act & Assert
        Assert.ThrowsException<InvalidOperationException>(() =>
            new EmbeddingService(
                new HttpClient(),
                new EmbeddingsOptions(
                    Provider: EmbeddingProviderType.AzureOpenAI,
                    BaseUrl: "https://test.openai.azure.com",
                    ApiKey: "key"),
                _mockLogger.Object,
                _mockCache.Object));
    }

    #endregion

    #region Helper Methods

    private static EmbeddingsOptions CreateAzureOpenAIOptions()
    {
        return new EmbeddingsOptions(
            Provider: EmbeddingProviderType.AzureOpenAI,
            BaseUrl: "https://test.openai.azure.com",
            ApiKey: "test-api-key",
            Model: "text-embedding-ada-002");
    }

    private static EmbeddingsOptions CreateOpenAIOptions()
    {
        return new EmbeddingsOptions(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: "https://api.openai.com",
            ApiKey: "test-api-key");
    }

    /// <summary>
    /// Creates a mock HttpMessageHandler that returns the specified status code and response body.
    /// </summary>
    private static Mock<HttpMessageHandler> CreateMockHttpMessageHandler(HttpStatusCode statusCode, string responseBody)
    {
        Mock<HttpMessageHandler> mockHandler = new(MockBehavior.Strict);
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            });

        return mockHandler;
    }

    /// <summary>
    /// Creates an embedding API response JSON with a single embedding at index 0.
    /// </summary>
    private static string CreateEmbeddingResponseJson(float[] embedding)
    {
        return CreateEmbeddingResponseJsonWithIndices(new[] { (0, embedding) });
    }

    /// <summary>
    /// Creates an embedding API response JSON with multiple embeddings at specified indices.
    /// </summary>
    private static string CreateEmbeddingResponseJsonWithIndices((int Index, float[] Embedding)[] embeddings)
    {
        var data = embeddings.Select(e => new
        {
            index = e.Index,
            embedding = e.Embedding,
            @object = "embedding"
        }).ToArray();

        var response = new
        {
            data,
            model = "text-embedding-ada-002",
            @object = "list",
            usage = new
            {
                prompt_tokens = 5,
                total_tokens = 5
            }
        };

        return JsonSerializer.Serialize(response, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });
    }

    #endregion
}
