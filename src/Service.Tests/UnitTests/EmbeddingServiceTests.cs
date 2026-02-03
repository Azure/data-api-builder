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

    #endregion
}
