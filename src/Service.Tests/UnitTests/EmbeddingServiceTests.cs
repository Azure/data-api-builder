// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for EmbeddingService.
/// </summary>
[TestClass]
public class EmbeddingServiceTests
{
    private Mock<ILogger<EmbeddingService>> _mockLogger = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<EmbeddingService>>();
    }

    /// <summary>
    /// Tests that EmbedAsync returns embedding for a single text input.
    /// </summary>
    [TestMethod]
    public async Task EmbedAsync_SingleText_ReturnsEmbedding()
    {
        // Arrange
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        float[] expectedEmbedding = new[] { 0.1f, 0.2f, 0.3f, 0.4f, 0.5f };
        HttpClient httpClient = CreateMockHttpClient(CreateSuccessResponse(expectedEmbedding));
        EmbeddingService service = new(httpClient, options, _mockLogger.Object);

        // Act
        float[] result = await service.EmbedAsync("Hello world");

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(expectedEmbedding.Length, result.Length);
        for (int i = 0; i < expectedEmbedding.Length; i++)
        {
            Assert.AreEqual(expectedEmbedding[i], result[i]);
        }
    }

    /// <summary>
    /// Tests that EmbedBatchAsync returns embeddings for multiple text inputs.
    /// </summary>
    [TestMethod]
    public async Task EmbedBatchAsync_MultipleTexts_ReturnsEmbeddings()
    {
        // Arrange
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        float[][] expectedEmbeddings = new[]
        {
            new[] { 0.1f, 0.2f, 0.3f },
            new[] { 0.4f, 0.5f, 0.6f },
            new[] { 0.7f, 0.8f, 0.9f }
        };
        HttpClient httpClient = CreateMockHttpClient(CreateBatchSuccessResponse(expectedEmbeddings));
        EmbeddingService service = new(httpClient, options, _mockLogger.Object);

        // Act
        float[][] result = await service.EmbedBatchAsync(new[] { "Text 1", "Text 2", "Text 3" });

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual(expectedEmbeddings.Length, result.Length);
        for (int i = 0; i < expectedEmbeddings.Length; i++)
        {
            Assert.AreEqual(expectedEmbeddings[i].Length, result[i].Length);
        }
    }

    /// <summary>
    /// Tests that EmbedAsync throws ArgumentException for null or empty text.
    /// </summary>
    [DataTestMethod]
    [DataRow(null, DisplayName = "Null text throws ArgumentException")]
    [DataRow("", DisplayName = "Empty text throws ArgumentException")]
    public async Task EmbedAsync_NullOrEmptyText_ThrowsArgumentException(string text)
    {
        // Arrange
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        HttpClient httpClient = CreateMockHttpClient(CreateSuccessResponse(new[] { 0.1f }));
        EmbeddingService service = new(httpClient, options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.EmbedAsync(text!));
    }

    /// <summary>
    /// Tests that EmbedBatchAsync throws ArgumentException for null or empty texts array.
    /// </summary>
    [TestMethod]
    public async Task EmbedBatchAsync_EmptyTexts_ThrowsArgumentException()
    {
        // Arrange
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        HttpClient httpClient = CreateMockHttpClient(CreateSuccessResponse(new[] { 0.1f }));
        EmbeddingService service = new(httpClient, options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsExceptionAsync<ArgumentException>(() => service.EmbedBatchAsync(Array.Empty<string>()));
    }

    /// <summary>
    /// Tests that HttpRequestException is thrown when API returns an error.
    /// </summary>
    [TestMethod]
    public async Task EmbedAsync_ApiError_ThrowsHttpRequestException()
    {
        // Arrange
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        HttpClient httpClient = CreateMockHttpClient(CreateErrorResponse(HttpStatusCode.Unauthorized, "Invalid API key"));
        EmbeddingService service = new(httpClient, options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsExceptionAsync<HttpRequestException>(() => service.EmbedAsync("Test text"));
    }

    /// <summary>
    /// Tests that InvalidOperationException is thrown when API returns empty data.
    /// </summary>
    [TestMethod]
    public async Task EmbedAsync_EmptyResponse_ThrowsInvalidOperationException()
    {
        // Arrange
        EmbeddingsOptions options = CreateAzureOpenAIOptions();
        string emptyResponse = JsonSerializer.Serialize(new { data = Array.Empty<object>() });
        HttpClient httpClient = CreateMockHttpClient(CreateSuccessResponseWithContent(emptyResponse));
        EmbeddingService service = new(httpClient, options, _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsExceptionAsync<InvalidOperationException>(() => service.EmbedAsync("Test text"));
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
            Endpoint: "https://api.openai.com",
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
            Endpoint: "https://my.openai.azure.com",
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
            Endpoint: "https://api.openai.com",
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
            Endpoint: "https://api.openai.com",
            ApiKey: "test-key",
            TimeoutMs: customTimeout);

        // Assert
        Assert.AreEqual(customTimeout, options.TimeoutMs);
        Assert.AreEqual(customTimeout, options.EffectiveTimeoutMs);
        Assert.IsTrue(options.UserProvidedTimeoutMs);
    }

    #region Helper Methods

    private static EmbeddingsOptions CreateAzureOpenAIOptions()
    {
        return new EmbeddingsOptions(
            Provider: EmbeddingProviderType.AzureOpenAI,
            Endpoint: "https://test.openai.azure.com",
            ApiKey: "test-api-key",
            Model: "text-embedding-ada-002");
    }

    private static HttpClient CreateMockHttpClient(HttpResponseMessage response)
    {
        Mock<HttpMessageHandler> mockHandler = new();
        mockHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        return new HttpClient(mockHandler.Object);
    }

    private static HttpResponseMessage CreateSuccessResponse(float[] embedding)
    {
        var response = new
        {
            data = new[]
            {
                new
                {
                    index = 0,
                    embedding = embedding
                }
            },
            model = "text-embedding-ada-002",
            usage = new
            {
                prompt_tokens = 5,
                total_tokens = 5
            }
        };

        string content = JsonSerializer.Serialize(response);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage CreateBatchSuccessResponse(float[][] embeddings)
    {
        var data = new object[embeddings.Length];
        for (int i = 0; i < embeddings.Length; i++)
        {
            data[i] = new
            {
                index = i,
                embedding = embeddings[i]
            };
        }

        var response = new
        {
            data,
            model = "text-embedding-ada-002",
            usage = new
            {
                prompt_tokens = 15,
                total_tokens = 15
            }
        };

        string content = JsonSerializer.Serialize(response);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage CreateSuccessResponseWithContent(string content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage CreateErrorResponse(HttpStatusCode statusCode, string errorMessage)
    {
        var errorContent = new
        {
            error = new
            {
                message = errorMessage,
                type = "invalid_request_error"
            }
        };

        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(JsonSerializer.Serialize(errorContent), Encoding.UTF8, "application/json")
        };
    }

    #endregion
}
