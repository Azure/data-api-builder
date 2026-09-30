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
    /// Data-driven test for constructor and EffectiveSizeChars scenarios.
    /// </summary>
    [DataTestMethod]
    [DataRow(true, null, null, EmbeddingsChunkingOptions.DEFAULT_SIZE_CHARS, EmbeddingsChunkingOptions.DEFAULT_OVERLAP_CHARS, EmbeddingsChunkingOptions.DEFAULT_SIZE_CHARS, DisplayName = "Default values")]
    [DataRow(true, 500, 100, 500, 100, 500, DisplayName = "Custom values override defaults")]
    [DataRow(true, 750, 50, 750, 50, 750, DisplayName = "EffectiveSizeChars returns configured value when valid")]
    [DataRow(true, 0, 50, 0, 50, 51, DisplayName = "EffectiveSizeChars returns minimum valid when value too small")]
    [DataRow(true, -100, 50, -100, 50, 51, DisplayName = "EffectiveSizeChars returns minimum valid when value negative")]
    [DataRow(false, 500, 100, 500, 100, 500, DisplayName = "Allows disabled chunking")]
    [DataRow(true, 1000, 0, 1000, 0, 1000, DisplayName = "Allows zero overlap")]
    [DataRow(true, 1000, -50, 1000, 0, 1000, DisplayName = "Negative overlap clamped to zero")]
    [DataRow(true, 100000, 1000, 100000, 1000, 100000, DisplayName = "Allows large chunk size")]
    [DataRow(true, 100, 200, 100, 200, 201, DisplayName = "Allows overlap larger than chunk size (edge case)")]
    public void EmbeddingsChunkingOptions_ConstructorAndEffectiveSizeChars(
        bool enabled,
        int? sizeChars,
        int? overlapChars,
        int expectedSizeChars,
        int expectedOverlapChars,
        int expectedEffectiveSize)
    {
        EmbeddingsChunkingOptions options = sizeChars is null && overlapChars is null
            ? new(enabled)
            : new(enabled, sizeChars ?? EmbeddingsChunkingOptions.DEFAULT_SIZE_CHARS, overlapChars ?? EmbeddingsChunkingOptions.DEFAULT_OVERLAP_CHARS);

        Assert.AreEqual(enabled, options.Enabled);
        Assert.AreEqual(expectedSizeChars, options.SizeChars);
        Assert.AreEqual(expectedOverlapChars, options.OverlapChars);
        Assert.AreEqual(expectedEffectiveSize, options.EffectiveSizeChars);
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
    /// Tests that negative overlap is clamped to zero.
    /// </summary>
    [TestMethod]
    public void Constructor_NegativeOverlapClampedToZero()
    {
        // Arrange & Act
        EmbeddingsChunkingOptions options = new(
            Enabled: true,
            SizeChars: 1000,
            OverlapChars: -50);

        // Assert: negative overlap must be clamped to 0
        Assert.AreEqual(0, options.OverlapChars);
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
