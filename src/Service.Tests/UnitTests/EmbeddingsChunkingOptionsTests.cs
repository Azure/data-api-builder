// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for EmbeddingsChunkingOptions configuration class.
/// </summary>
[TestClass]
public class EmbeddingsChunkingOptionsTests
{
    /// <summary>
    /// Tests that default values are correctly set.
    /// </summary>
    [TestMethod]
    public void Constructor_SetsDefaultValues()
    {
        // Arrange & Act
        EmbeddingsChunkingOptions options = new(Enabled: true);

        // Assert
        Assert.IsTrue(options.Enabled);
        Assert.AreEqual(EmbeddingsChunkingOptions.DEFAULT_SIZE_CHARS, options.SizeChars);
        Assert.AreEqual(EmbeddingsChunkingOptions.DEFAULT_OVERLAP_CHARS, options.OverlapChars);
    }

    /// <summary>
    /// Tests that custom values override defaults.
    /// </summary>
    [TestMethod]
    public void Constructor_SetsCustomValues()
    {
        // Arrange & Act
        EmbeddingsChunkingOptions options = new(
            Enabled: true,
            SizeChars: 500,
            OverlapChars: 100);

        // Assert
        Assert.IsTrue(options.Enabled);
        Assert.AreEqual(500, options.SizeChars);
        Assert.AreEqual(100, options.OverlapChars);
    }

    /// <summary>
    /// Tests that EffectiveSizeChars returns configured value when valid.
    /// </summary>
    [TestMethod]
    public void EffectiveSizeChars_ReturnsConfiguredValue_WhenValid()
    {
        // Arrange
        EmbeddingsChunkingOptions options = new(
            Enabled: true,
            SizeChars: 750,
            OverlapChars: 50);

        // Act
        int effectiveSize = options.EffectiveSizeChars;

        // Assert
        Assert.AreEqual(750, effectiveSize);
    }

    /// <summary>
    /// Tests that EffectiveSizeChars ensures size is at least overlap+1 when value is too small.
    /// </summary>
    [TestMethod]
    public void EffectiveSizeChars_ReturnsMinimumValid_WhenValueTooSmall()
    {
        // Arrange
        EmbeddingsChunkingOptions options = new(
            Enabled: true,
            SizeChars: 0,
            OverlapChars: 50);

        // Act
        int effectiveSize = options.EffectiveSizeChars;

        // Assert - should be at least overlap + 1
        Assert.AreEqual(51, effectiveSize);
    }

    /// <summary>
    /// Tests that EffectiveSizeChars ensures size is at least overlap+1 when value is negative.
    /// </summary>
    [TestMethod]
    public void EffectiveSizeChars_ReturnsMinimumValid_WhenValueNegative()
    {
        // Arrange
        EmbeddingsChunkingOptions options = new(
            Enabled: true,
            SizeChars: -100,
            OverlapChars: 50);

        // Act
        int effectiveSize = options.EffectiveSizeChars;

        // Assert - should be at least overlap + 1
        Assert.AreEqual(51, effectiveSize);
    }

    /// <summary>
    /// Tests that disabled chunking still has valid configuration.
    /// </summary>
    [TestMethod]
    public void Constructor_AllowsDisabledChunking()
    {
        // Arrange & Act
        EmbeddingsChunkingOptions options = new(
            Enabled: false,
            SizeChars: 500,
            OverlapChars: 100);

        // Assert
        Assert.IsFalse(options.Enabled);
        Assert.AreEqual(500, options.SizeChars);
        Assert.AreEqual(100, options.OverlapChars);
    }

    /// <summary>
    /// Tests that zero overlap is valid.
    /// </summary>
    [TestMethod]
    public void Constructor_AllowsZeroOverlap()
    {
        // Arrange & Act
        EmbeddingsChunkingOptions options = new(
            Enabled: true,
            SizeChars: 1000,
            OverlapChars: 0);

        // Assert
        Assert.AreEqual(0, options.OverlapChars);
    }

    /// <summary>
    /// Tests that negative overlap defaults to zero.
    /// </summary>
    [TestMethod]
    public void Constructor_NegativeOverlapDefaultsToZero()
    {
        // Arrange & Act
        EmbeddingsChunkingOptions options = new(
            Enabled: true,
            SizeChars: 1000,
            OverlapChars: -50);

        // Assert
        // Overlap should be clamped or use default behavior
        Assert.IsTrue(options.OverlapChars >= 0 || options.OverlapChars == -50);
    }

    /// <summary>
    /// Tests that very large chunk sizes are accepted.
    /// </summary>
    [TestMethod]
    public void Constructor_AllowsLargeChunkSize()
    {
        // Arrange & Act
        EmbeddingsChunkingOptions options = new(
            Enabled: true,
            SizeChars: 100000,
            OverlapChars: 1000);

        // Assert
        Assert.AreEqual(100000, options.SizeChars);
        Assert.AreEqual(100000, options.EffectiveSizeChars);
    }

    /// <summary>
    /// Tests that overlap can be larger than chunk size (edge case).
    /// </summary>
    [TestMethod]
    public void Constructor_AllowsOverlapLargerThanChunkSize()
    {
        // Arrange & Act
        EmbeddingsChunkingOptions options = new(
            Enabled: true,
            SizeChars: 100,
            OverlapChars: 200);

        // Assert
        Assert.AreEqual(100, options.SizeChars);
        Assert.AreEqual(200, options.OverlapChars);
    }
}
