// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for SemanticCacheService
/// Note: These tests focus on validation and error handling logic.
/// Integration tests with actual Redis and Azure OpenAI should be done separately.
/// </summary>
[TestClass]
public class SemanticCacheServiceTests
{
    [TestMethod]
    public void SemanticCacheOptions_DefaultValues_AreCorrect()
    {
        // Assert
        Assert.AreEqual(0.85, SemanticCacheOptions.DEFAULT_SIMILARITY_THRESHOLD);
        Assert.AreEqual(5, SemanticCacheOptions.DEFAULT_MAX_RESULTS);
        Assert.AreEqual(86400, SemanticCacheOptions.DEFAULT_EXPIRE_SECONDS);
    }

    [TestMethod]
    public void SemanticCacheOptions_WithValidValues_CreatesInstance()
    {
        // Arrange & Act
        var options = new SemanticCacheOptions(
            enabled: true,
            similarityThreshold: 0.90,
            maxResults: 10,
            expireSeconds: 7200,
            azureManagedRedis: new AzureManagedRedisOptions("test-connection"),
            embeddingProvider: new EmbeddingProviderOptions(
                type: "azure-openai",
                endpoint: "https://test.openai.azure.com",
                apiKey: "test-key",
                model: "text-embedding-ada-002")
        );

        // Assert
        Assert.IsNotNull(options);
        Assert.IsTrue(options.Enabled);
        Assert.AreEqual(0.90, options.SimilarityThreshold);
        Assert.AreEqual(10, options.MaxResults);
        Assert.AreEqual(7200, options.ExpireSeconds);
    }

    [TestMethod]
    public void AzureManagedRedisOptions_WithValidConnection_CreatesInstance()
    {
        // Arrange & Act
        var options = new AzureManagedRedisOptions(
            connectionString: "test-redis.cache.windows.net:6380,password=xyz,ssl=True",
            vectorIndex: "custom-index",
            keyPrefix: "dab:sc:"
        );

        // Assert
        Assert.IsNotNull(options);
        Assert.IsNotNull(options.ConnectionString);
        Assert.AreEqual("custom-index", options.VectorIndex);
        Assert.AreEqual("dab:sc:", options.KeyPrefix);
    }

    [TestMethod]
    public void EmbeddingProviderOptions_WithValidValues_CreatesInstance()
    {
        // Arrange & Act
        var options = new EmbeddingProviderOptions(
            type: "azure-openai",
            endpoint: "https://test.openai.azure.com",
            apiKey: "test-api-key",
            model: "text-embedding-ada-002"
        );

        // Assert
        Assert.IsNotNull(options);
        Assert.AreEqual("azure-openai", options.Type);
        Assert.AreEqual("https://test.openai.azure.com", options.Endpoint);
        Assert.AreEqual("test-api-key", options.ApiKey);
        Assert.AreEqual("text-embedding-ada-002", options.Model);
    }

    [TestMethod]
    public void SemanticCacheResult_WithValidData_CreatesInstance()
    {
        // Arrange & Act
        var result = new SemanticCacheResult(
            response: "{\"data\":\"test\"}",
            similarity: 0.95,
            originalQuery: "SELECT * FROM users"
        );

        // Assert
        Assert.IsNotNull(result);
        Assert.AreEqual("{\"data\":\"test\"}", result.Response);
        Assert.AreEqual(0.95, result.Similarity);
        Assert.AreEqual("SELECT * FROM users", result.OriginalQuery);
    }

    [TestMethod]
    public void SemanticCacheOptions_DefaultsApplied_WhenNotProvided()
    {
        // Arrange & Act
        var options = new SemanticCacheOptions(
            enabled: true,
            similarityThreshold: null, // Will use default
            maxResults: null, // Will use default
            expireSeconds: null, // Will use default
            azureManagedRedis: new AzureManagedRedisOptions("test"),
            embeddingProvider: new EmbeddingProviderOptions("azure-openai", "https://test.com", "key", "model")
        );

        // Assert - Defaults should be applied at usage time
        Assert.IsTrue(options.Enabled);
        Assert.IsNull(options.SimilarityThreshold); // Stored as null, default applied at usage
        Assert.IsNull(options.MaxResults);
        Assert.IsNull(options.ExpireSeconds);
    }
}


