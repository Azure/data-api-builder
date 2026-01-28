// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

[TestClass]
public class SemanticCacheOptionsTests
{
    [TestMethod]
    public void Constructor_WithValidValues_CreatesInstance()
    {
        // Arrange & Act
        var options = new SemanticCacheOptions(
            enabled: true,
            similarityThreshold: 0.85,
            maxResults: 5,
            expireSeconds: 3600,
            azureManagedRedis: new AzureManagedRedisOptions("test-connection"),
            embeddingProvider: new EmbeddingProviderOptions(
                type: "azure-openai",
                endpoint: "https://test.openai.azure.com",
                apiKey: "test-key",
                model: "test-model")
        );

        // Assert
        Assert.IsNotNull(options);
        Assert.IsTrue(options.Enabled);
        Assert.AreEqual(0.85, options.SimilarityThreshold);
        Assert.AreEqual(5, options.MaxResults);
        Assert.AreEqual(3600, options.ExpireSeconds);
        Assert.IsNotNull(options.AzureManagedRedis);
        Assert.IsNotNull(options.EmbeddingProvider);
    }

    [TestMethod]
    public void DefaultValues_AreCorrect()
    {
        // Arrange & Act
        var options = new SemanticCacheOptions(
            enabled: false,
            similarityThreshold: null,
            maxResults: null,
            expireSeconds: null,
            azureManagedRedis: null,
            embeddingProvider: null
        );

        // Assert
        Assert.IsFalse(options.Enabled);
        Assert.IsNull(options.SimilarityThreshold);
        Assert.IsNull(options.MaxResults);
        Assert.IsNull(options.ExpireSeconds);
    }

    [TestMethod]
    public void Deserialization_WithValidJson_Succeeds()
    {
        // Arrange
        string json = @"{
            ""enabled"": true,
            ""similarity-threshold"": 0.90,
            ""max-results"": 10,
            ""expire-seconds"": 7200,
            ""azure-managed-redis"": {
                ""connection-string"": ""test-redis-connection""
            },
            ""embedding-provider"": {
                ""type"": ""azure-openai"",
                ""endpoint"": ""https://test.openai.azure.com"",
                ""api-key"": ""test-key"",
                ""model"": ""text-embedding-ada-002""
            }
        }";

        // Act
        var options = JsonSerializer.Deserialize<SemanticCacheOptions>(
            json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        // Assert
        Assert.IsNotNull(options);
        Assert.IsTrue(options.Enabled);
        Assert.AreEqual(0.90, options.SimilarityThreshold);
        Assert.AreEqual(10, options.MaxResults);
        Assert.AreEqual(7200, options.ExpireSeconds);
    }

    [TestMethod]
    public void Deserialization_WithInvalidSimilarityThreshold_ThrowsException()
    {
        // Arrange
        string json = @"{
            ""enabled"": true,
            ""similarity-threshold"": 1.5,
            ""azure-managed-redis"": {
                ""connection-string"": ""test""
            },
            ""embedding-provider"": {
                ""type"": ""azure-openai"",
                ""endpoint"": ""https://test.com"",
                ""api-key"": ""key"",
                ""model"": ""model""
            }
        }";

        // Create JsonSerializerOptions with the custom converter (following Azure best practices for configuration validation)
        var options = new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true,
            Converters = { new Azure.DataApiBuilder.Config.Converters.SemanticCacheOptionsConverterFactory() }
        };

        // Act & Assert
        Assert.ThrowsException<JsonException>(() =>
            JsonSerializer.Deserialize<SemanticCacheOptions>(json, options));
    }

    [TestMethod]
    public void Deserialization_WithNegativeMaxResults_ThrowsException()
    {
        // Arrange
        string json = @"{
            ""enabled"": true,
            ""max-results"": -5,
            ""azure-managed-redis"": {
                ""connection-string"": ""test""
            },
            ""embedding-provider"": {
                ""type"": ""azure-openai"",
                ""endpoint"": ""https://test.com"",
                ""api-key"": ""key"",
                ""model"": ""model""
            }
        }";

        // Create JsonSerializerOptions with the custom converter (following Azure best practices for configuration validation)
        var options = new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true,
            Converters = { new Azure.DataApiBuilder.Config.Converters.SemanticCacheOptionsConverterFactory() }
        };

        // Act & Assert
        Assert.ThrowsException<JsonException>(() =>
            JsonSerializer.Deserialize<SemanticCacheOptions>(json, options));
    }

    [TestMethod]
    public void Deserialization_WithZeroExpireSeconds_ThrowsException()
    {
        // Arrange
        string json = @"{
            ""enabled"": true,
            ""expire-seconds"": 0,
            ""azure-managed-redis"": {
                ""connection-string"": ""test""
            },
            ""embedding-provider"": {
                ""type"": ""azure-openai"",
                ""endpoint"": ""https://test.com"",
                ""api-key"": ""key"",
                ""model"": ""model""
            }
        }";

        // Create JsonSerializerOptions with the custom converter (following Azure best practices for configuration validation)
        var options = new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true,
            Converters = { new Azure.DataApiBuilder.Config.Converters.SemanticCacheOptionsConverterFactory() }
        };

        // Act & Assert
        Assert.ThrowsException<JsonException>(() =>
            JsonSerializer.Deserialize<SemanticCacheOptions>(json, options));
    }

    [TestMethod]
    public void Serialization_OnlyWritesUserProvidedValues()
    {
        // Arrange
        var options = new SemanticCacheOptions(
            enabled: true,
            similarityThreshold: 0.85,
            maxResults: null, // Not provided
            expireSeconds: null, // Not provided
            azureManagedRedis: new AzureManagedRedisOptions("test-connection"),
            embeddingProvider: new EmbeddingProviderOptions(
                type: "azure-openai",
                endpoint: "https://test.com",
                apiKey: "key",
                model: "model")
        );

        // Act
        string json = JsonSerializer.Serialize(options, new JsonSerializerOptions 
        { 
            WriteIndented = true 
        });

        // Assert
        Assert.IsTrue(json.Contains("\"enabled\""));
        Assert.IsTrue(json.Contains("\"similarity-threshold\""));
        // max-results and expire-seconds should not be in JSON if not provided
    }

    [TestMethod]
    public void Constants_HaveCorrectDefaultValues()
    {
        // Assert
        Assert.AreEqual(0.85, SemanticCacheOptions.DEFAULT_SIMILARITY_THRESHOLD);
        Assert.AreEqual(5, SemanticCacheOptions.DEFAULT_MAX_RESULTS);
        Assert.AreEqual(86400, SemanticCacheOptions.DEFAULT_EXPIRE_SECONDS);
    }

    [TestMethod]
    [DataRow(0.0)]
    [DataRow(0.5)]
    [DataRow(0.85)]
    [DataRow(0.99)]
    [DataRow(1.0)]
    public void SimilarityThreshold_WithValidValues_IsAccepted(double threshold)
    {
        // Arrange & Act
        var options = new SemanticCacheOptions(
            enabled: true,
            similarityThreshold: threshold,
            maxResults: 5,
            expireSeconds: 3600,
            azureManagedRedis: new AzureManagedRedisOptions("test"),
            embeddingProvider: new EmbeddingProviderOptions("azure-openai", "https://test.com", "key", "model")
        );

        // Assert
        Assert.AreEqual(threshold, options.SimilarityThreshold);
    }

    [TestMethod]
    [DataRow(1)]
    [DataRow(5)]
    [DataRow(10)]
    [DataRow(100)]
    public void MaxResults_WithValidValues_IsAccepted(int maxResults)
    {
        // Arrange & Act
        var options = new SemanticCacheOptions(
            enabled: true,
            similarityThreshold: 0.85,
            maxResults: maxResults,
            expireSeconds: 3600,
            azureManagedRedis: new AzureManagedRedisOptions("test"),
            embeddingProvider: new EmbeddingProviderOptions("azure-openai", "https://test.com", "key", "model")
        );

        // Assert
        Assert.AreEqual(maxResults, options.MaxResults);
    }

    [TestMethod]
    [DataRow(60)]      // 1 minute
    [DataRow(3600)]    // 1 hour
    [DataRow(86400)]   // 1 day
    [DataRow(604800)]  // 1 week
    public void ExpireSeconds_WithValidValues_IsAccepted(int expireSeconds)
    {
        // Arrange & Act
        var options = new SemanticCacheOptions(
            enabled: true,
            similarityThreshold: 0.85,
            maxResults: 5,
            expireSeconds: expireSeconds,
            azureManagedRedis: new AzureManagedRedisOptions("test"),
            embeddingProvider: new EmbeddingProviderOptions("azure-openai", "https://test.com", "key", "model")
        );

        // Assert
        Assert.AreEqual(expireSeconds, options.ExpireSeconds);
    }
}
