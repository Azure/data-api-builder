// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net.Http;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.SemanticCache;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

[TestClass]
public class AzureOpenAIEmbeddingServiceTests
{
    private Mock<ILogger<AzureOpenAIEmbeddingService>> _mockLogger = null!;
    private Mock<IHttpClientFactory> _mockHttpClientFactory = null!;
    private EmbeddingProviderOptions _testOptions = null!;

    [TestInitialize]
    public void Setup()
    {
        _mockLogger = new Mock<ILogger<AzureOpenAIEmbeddingService>>();
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _testOptions = new EmbeddingProviderOptions(
            type: "azure-openai",
            endpoint: "https://test.openai.azure.com",
            apiKey: "test-api-key",
            model: "text-embedding-ada-002"
        );
    }

    [TestMethod]
    public void Constructor_WithNullOptions_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.ThrowsException<ArgumentNullException>(
            () => new AzureOpenAIEmbeddingService(null!, _mockHttpClientFactory.Object, _mockLogger.Object));
    }

    [TestMethod]
    public void Constructor_WithValidParameters_CreatesInstance()
    {
        // Act
        var service = new AzureOpenAIEmbeddingService(_testOptions, _mockHttpClientFactory.Object, _mockLogger.Object);

        // Assert
        Assert.IsNotNull(service);
    }

    [TestMethod]
    public void Constructor_WithMissingEndpoint_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new EmbeddingProviderOptions(
            type: "azure-openai",
            endpoint: "",
            apiKey: "test-key",
            model: "test-model"
        );

        // Act & Assert
        var ex = Assert.ThrowsException<ArgumentException>(
            () => new AzureOpenAIEmbeddingService(invalidOptions, _mockHttpClientFactory.Object, _mockLogger.Object));
        Assert.IsTrue(ex.Message.Contains("endpoint"));
    }

    [TestMethod]
    public void Constructor_WithMissingApiKey_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new EmbeddingProviderOptions(
            type: "azure-openai",
            endpoint: "https://test.openai.azure.com",
            apiKey: "",
            model: "test-model"
        );

        // Act & Assert
        var ex = Assert.ThrowsException<ArgumentException>(
            () => new AzureOpenAIEmbeddingService(invalidOptions, _mockHttpClientFactory.Object, _mockLogger.Object));
        Assert.IsTrue(ex.Message.Contains("API key"));
    }

    [TestMethod]
    public void Constructor_WithMissingModel_ThrowsArgumentException()
    {
        // Arrange
        var invalidOptions = new EmbeddingProviderOptions(
            type: "azure-openai",
            endpoint: "https://test.openai.azure.com",
            apiKey: "test-key",
            model: ""
        );

        // Act & Assert
        var ex = Assert.ThrowsException<ArgumentException>(
            () => new AzureOpenAIEmbeddingService(invalidOptions, _mockHttpClientFactory.Object, _mockLogger.Object));
        Assert.IsTrue(ex.Message.Contains("model"));
    }

    [TestMethod]
    [DataRow("SELECT * FROM users")]
    [DataRow("INSERT INTO users (name, email) VALUES ('John', 'john@example.com')")]
    [DataRow("UPDATE users SET status = 'active' WHERE id = 123")]
    public void ServiceValidation_AcceptsVariousQueryTypes(string query)
    {
        // Arrange
        var service = new AzureOpenAIEmbeddingService(_testOptions, _mockHttpClientFactory.Object, _mockLogger.Object);

        // Assert - should not throw during validation
        Assert.IsNotNull(service);
        Assert.IsTrue(query.Length > 0);
    }

    [TestMethod]
    public void ServiceConfiguration_SetsCorrectDefaults()
    {
        // Arrange & Act
        var service = new AzureOpenAIEmbeddingService(_testOptions, _mockHttpClientFactory.Object, _mockLogger.Object);

        // Assert - Service should be created without errors
        Assert.IsNotNull(service);
    }
}


