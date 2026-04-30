// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable disable

using System;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for RuntimeCacheOptions with L2 cache configuration.
/// Tests integration between RuntimeCacheOptions and RuntimeCacheLevel2Options.
/// </summary>
[TestClass]
public class RuntimeCacheOptionsL2Tests
{
    private const string LocalRedisConnectionString = "localhost:6379";
    private const string AzureRedisConnectionString = "contoso.redis.cache.windows.net:6380";
    private const string OpenAIApiUrl = "https://api.openai.com";
    private const string AzureOpenAIApiUrl = "https://contoso.openai.azure.com";
    private const string TestApiKey = "test-key";
    private const string TestModel = "text-embedding-3-small";

    [TestMethod]
    public void RuntimeCacheOptions_InferredLevel_L1Only()
    {
        // Arrange
        RuntimeCacheOptions options = new(Enabled: true, TtlSeconds: 5);

        // Assert
        Assert.AreEqual(EntityCacheLevel.L1, options.InferredLevel, 
            "Should infer L1 when Level2 is null");
    }

    [TestMethod]
    public void RuntimeCacheOptions_InferredLevel_L1L2_WhenLevel2Enabled()
    {
        // Arrange
        RuntimeCacheLevel2Options level2 = new(Enabled: true, ConnectionString: LocalRedisConnectionString);
        RuntimeCacheOptions options = new(Enabled: true, TtlSeconds: 5)
        {
            Level2 = level2
        };

        // Assert
        Assert.AreEqual(EntityCacheLevel.L1L2, options.InferredLevel, 
            "Should infer L1L2 when Level2 is enabled");
    }

    [TestMethod]
    public void RuntimeCacheOptions_InferredLevel_L1_WhenLevel2Disabled()
    {
        // Arrange
        RuntimeCacheLevel2Options level2 = new(Enabled: false, ConnectionString: LocalRedisConnectionString);
        RuntimeCacheOptions options = new(Enabled: true, TtlSeconds: 5)
        {
            Level2 = level2
        };

        // Assert
        Assert.AreEqual(EntityCacheLevel.L1, options.InferredLevel, 
            "Should infer L1 when Level2 is disabled");
    }

    [TestMethod]
    public void RuntimeCacheOptions_WithLevel2_RoundTripSerialization()
    {
        // Arrange
        RuntimeCacheLevel2Options level2 = new(Enabled: true, ConnectionString: LocalRedisConnectionString);
        RuntimeCacheOptions original = new(Enabled: true, TtlSeconds: 10)
        {
            Level2 = level2
        };
        JsonSerializerOptions serializerOptions = RuntimeConfigLoader.GetSerializationOptions();

        // Act
        string json = JsonSerializer.Serialize(original, serializerOptions);
        RuntimeCacheOptions deserialized = JsonSerializer.Deserialize<RuntimeCacheOptions>(json, serializerOptions);

        // Assert
        Assert.IsNotNull(deserialized, "Deserialized options should not be null");
        Assert.AreEqual(original.Enabled, deserialized.Enabled, "Enabled should match");
        Assert.AreEqual(original.TtlSeconds, deserialized.TtlSeconds, "TtlSeconds should match");
        Assert.IsNotNull(deserialized.Level2, "Level2 should not be null");
        Assert.AreEqual(original.Level2.Enabled, deserialized.Level2.Enabled, "Level2 Enabled should match");
        Assert.AreEqual(original.Level2.ConnectionString, deserialized.Level2.ConnectionString, 
            "Level2 ConnectionString should match");
    }

    [TestMethod]
    public void RuntimeCacheOptions_DefaultTtl()
    {
        // Arrange & Act
        RuntimeCacheOptions options = new();

        // Assert
        Assert.AreEqual(RuntimeCacheOptions.DEFAULT_TTL_SECONDS, options.TtlSeconds, 
            "Should use default TTL of 5 seconds");
    }

    [TestMethod]
    public void RuntimeCacheOptions_UserProvidedTtl()
    {
        // Arrange & Act
        RuntimeCacheOptions options = new(TtlSeconds: 30);

        // Assert
        Assert.AreEqual(30, options.TtlSeconds, "Should use user-provided TTL");
        Assert.IsTrue(options.UserProvidedTtlOptions, "UserProvidedTtlOptions should be true");
    }

    [TestMethod]
    public void EmbeddingsOptions_WithCacheConfiguration()
    {
        // Arrange
        EmbeddingsCacheLevel2Options level2 = new(Enabled: true, ConnectionString: AzureRedisConnectionString);
        EmbeddingsCacheOptions cache = new(Enabled: true, TtlHours: 48, Level2: level2);

        // Act
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.AzureOpenAI,
            BaseUrl: AzureOpenAIApiUrl,
            ApiKey: TestApiKey,
            Model: TestModel,
            Cache: cache);

        // Assert
        Assert.IsTrue(options.IsCachingEnabled, "Caching should be enabled");
        Assert.IsTrue(options.IsLevel2CacheEnabled, "L2 cache should be enabled");
        Assert.IsNotNull(options.Cache, "Cache should not be null");
        Assert.AreEqual(48, options.Cache.TtlHours, "Cache TTL should be 48 hours");
        Assert.IsNotNull(options.Cache.Level2, "Cache Level2 should not be null");
        Assert.AreEqual(AzureRedisConnectionString, options.Cache.Level2.ConnectionString);
    }

    [TestMethod]
    public void EmbeddingsOptions_IsCachingEnabled_DefaultTrue()
    {
        // Arrange & Act
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: OpenAIApiUrl,
            ApiKey: TestApiKey);

        // Assert
        Assert.IsTrue(options.IsCachingEnabled, 
            "IsCachingEnabled should be true by default when Cache is null");
    }

    [TestMethod]
    public void EmbeddingsOptions_IsCachingEnabled_ExplicitlyDisabled()
    {
        // Arrange
        EmbeddingsCacheOptions cache = new(Enabled: false);

        // Act
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: OpenAIApiUrl,
            ApiKey: TestApiKey,
            Cache: cache);

        // Assert
        Assert.IsFalse(options.IsCachingEnabled, "IsCachingEnabled should be false when explicitly disabled");
    }

    [TestMethod]
    public void EmbeddingsOptions_IsLevel2CacheEnabled_WhenCacheNull()
    {
        // Arrange & Act
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: OpenAIApiUrl,
            ApiKey: TestApiKey);

        // Assert
        Assert.IsFalse(options.IsLevel2CacheEnabled, 
            "IsLevel2CacheEnabled should be false when Cache is null");
    }

    [TestMethod]
    public void EmbeddingsOptions_IsLevel2CacheEnabled_WhenLevel2Null()
    {
        // Arrange
        EmbeddingsCacheOptions cache = new(Enabled: true);

        // Act
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: OpenAIApiUrl,
            ApiKey: TestApiKey,
            Cache: cache);

        // Assert
        Assert.IsFalse(options.IsLevel2CacheEnabled, 
            "IsLevel2CacheEnabled should be false when Level2 is null");
    }

    [TestMethod]
    public void EmbeddingsOptions_IsLevel2CacheEnabled_WhenLevel2Disabled()
    {
        // Arrange
        EmbeddingsCacheLevel2Options level2 = new(Enabled: false);
        EmbeddingsCacheOptions cache = new(Enabled: true, Level2: level2);

        // Act
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: OpenAIApiUrl,
            ApiKey: TestApiKey,
            Cache: cache);

        // Assert
        Assert.IsFalse(options.IsLevel2CacheEnabled, 
            "IsLevel2CacheEnabled should be false when Level2 is disabled");
    }

    [TestMethod]
    public void EmbeddingsOptions_IsLevel2CacheEnabled_WhenCacheDisabled()
    {
        // Arrange
        EmbeddingsCacheLevel2Options level2 = new(Enabled: true, ConnectionString: LocalRedisConnectionString);
        EmbeddingsCacheOptions cache = new(Enabled: false, Level2: level2);

        // Act
        EmbeddingsOptions options = new(
            Provider: EmbeddingProviderType.OpenAI,
            BaseUrl: OpenAIApiUrl,
            ApiKey: TestApiKey,
            Cache: cache);

        // Assert
        Assert.IsFalse(options.IsLevel2CacheEnabled, 
            "IsLevel2CacheEnabled should be false when cache itself is disabled");
    }
}
