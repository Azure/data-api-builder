// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for EmbeddingsCacheOptions class.
/// Tests configuration, validation, and behavior of embeddings cache options.
/// </summary>
[TestClass]
public class EmbeddingsCacheOptionsTests
{
    [TestMethod]
    public void EmbeddingsCacheOptions_DefaultValues()
    {
        // Arrange & Act
        EmbeddingsCacheOptions options = new();

        // Assert
        Assert.IsTrue(options.Enabled ?? true, "Enabled should default to true");
        Assert.AreEqual(EmbeddingsCacheOptions.DEFAULT_TTL_HOURS, options.TtlHours,
            "TtlHours should default to 24");
        Assert.IsNull(options.Level2, "Level2 should default to null");
        Assert.IsFalse(options.UserProvidedTtlHours, "UserProvidedTtlHours should be false");
    }

    [TestMethod]
    public void EmbeddingsCacheOptions_WithCustomTtl()
    {
        // Arrange
        int customTtl = 48;

        // Act
        EmbeddingsCacheOptions options = new(TtlHours: customTtl);

        // Assert
        Assert.AreEqual(customTtl, options.TtlHours, "TtlHours should match custom value");
        Assert.IsTrue(options.UserProvidedTtlHours, "UserProvidedTtlHours should be true");
        Assert.AreEqual(customTtl, options.EffectiveTtlHours, "EffectiveTtlHours should match custom value");
    }

    [TestMethod]
    public void EmbeddingsCacheOptions_EffectiveTtlHours_UsesDefault()
    {
        // Arrange & Act
        EmbeddingsCacheOptions options = new();

        // Assert
        Assert.AreEqual(EmbeddingsCacheOptions.DEFAULT_TTL_HOURS, options.EffectiveTtlHours,
            "EffectiveTtlHours should return default when not provided");
    }

    [TestMethod]
    public void EmbeddingsCacheOptions_EffectiveTtlHours_UsesCustom()
    {
        // Arrange
        int customTtl = 72;

        // Act
        EmbeddingsCacheOptions options = new(TtlHours: customTtl);

        // Assert
        Assert.AreEqual(customTtl, options.EffectiveTtlHours,
            "EffectiveTtlHours should return custom value when provided");
    }

    [TestMethod]
    public void EmbeddingsCacheOptions_WithLevel2Disabled()
    {
        // Arrange
        EmbeddingsCacheLevel2Options level2Options = new(Enabled: false);

        // Act
        EmbeddingsCacheOptions options = new(Level2: level2Options);

        // Assert
        Assert.IsFalse(options.IsLevel2Enabled, "IsLevel2Enabled should be false");
        Assert.IsNotNull(options.Level2, "Level2 should not be null");
    }

    [TestMethod]
    public void EmbeddingsCacheOptions_WithLevel2Enabled()
    {
        // Arrange
        EmbeddingsCacheLevel2Options level2Options = new(
            Enabled: true,
            ConnectionString: "localhost:6379");

        // Act
        EmbeddingsCacheOptions options = new(
            Enabled: true,
            Level2: level2Options);

        // Assert
        Assert.IsTrue(options.IsLevel2Enabled, "IsLevel2Enabled should be true");
        Assert.IsNotNull(options.Level2, "Level2 should not be null");
        Assert.AreEqual("localhost:6379", options.Level2.ConnectionString);
    }

    [TestMethod]
    public void EmbeddingsCacheOptions_IsLevel2Enabled_WhenLevel2IsNull()
    {
        // Arrange & Act
        EmbeddingsCacheOptions options = new();

        // Assert
        Assert.IsFalse(options.IsLevel2Enabled, "IsLevel2Enabled should be false when Level2 is null");
    }

    [TestMethod]
    public void EmbeddingsCacheOptions_DisabledCache()
    {
        // Arrange & Act
        EmbeddingsCacheOptions options = new(Enabled: false);

        // Assert
        Assert.IsFalse(options.Enabled ?? true, "Enabled should be false");
    }

    [TestMethod]
    public void EmbeddingsCacheOptions_RecordEquality()
    {
        // Arrange
        EmbeddingsCacheLevel2Options level2 = new(
            Enabled: true,
            ConnectionString: "localhost:6379");

        EmbeddingsCacheOptions options1 = new(
            Enabled: true,
            TtlHours: 24,
            Level2: level2);

        EmbeddingsCacheOptions options2 = new(
            Enabled: true,
            TtlHours: 24,
            Level2: level2);

        EmbeddingsCacheOptions options3 = new(
            Enabled: true,
            TtlHours: 48,
            Level2: level2);

        // Act & Assert
        Assert.AreEqual(options1, options2, "Options with same values should be equal");
        Assert.AreNotEqual(options1, options3, "Options with different TTL should not be equal");
    }

    [TestMethod]
    public void EmbeddingsCacheOptions_DefaultTtlConstant()
    {
        // Assert
        Assert.AreEqual(24, EmbeddingsCacheOptions.DEFAULT_TTL_HOURS,
            "DEFAULT_TTL_HOURS constant should be 24");
    }

    [TestMethod]
    public void EmbeddingsCacheOptions_WithAllProperties()
    {
        // Arrange
        bool enabled = true;
        int ttlHours = 36;
        EmbeddingsCacheLevel2Options level2 = new(
            Enabled: true,
            ConnectionString: "contoso.redis.cache.windows.net:6380,password=key,ssl=True");

        // Act
        EmbeddingsCacheOptions options = new(
            Enabled: enabled,
            TtlHours: ttlHours,
            Level2: level2);

        // Assert
        Assert.AreEqual(enabled, options.Enabled);
        Assert.AreEqual(ttlHours, options.TtlHours);
        Assert.AreEqual(ttlHours, options.EffectiveTtlHours);
        Assert.IsTrue(options.UserProvidedTtlHours);
        Assert.IsTrue(options.IsLevel2Enabled);
        Assert.IsNotNull(options.Level2);
    }

    [TestMethod]
    public void EmbeddingsCacheOptions_UserProvidedTtlHours_FalseWhenNotProvided()
    {
        // Arrange & Act
        EmbeddingsCacheOptions options = new(Enabled: true);

        // Assert
        Assert.IsFalse(options.UserProvidedTtlHours,
            "UserProvidedTtlHours should be false when TTL not provided");
    }

    [TestMethod]
    public void EmbeddingsCacheOptions_UserProvidedTtlHours_TrueWhenProvided()
    {
        // Arrange & Act
        EmbeddingsCacheOptions options = new(Enabled: true, TtlHours: 12);

        // Assert
        Assert.IsTrue(options.UserProvidedTtlHours,
            "UserProvidedTtlHours should be true when TTL provided");
    }
}
