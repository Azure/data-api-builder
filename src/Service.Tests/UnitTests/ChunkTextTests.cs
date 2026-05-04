// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Service.Helpers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests;

/// <summary>
/// Unit tests for the ChunkText functionality in EmbeddingController.
/// </summary>
[TestClass]
public class ChunkTextTests
{

    /// <summary>
    /// Tests that ChunkText returns single chunk for text smaller than chunk size.
    /// </summary>
    [TestMethod]
    public void ChunkText_ReturnsSingleChunk_ForSmallText()
    {
        // Arrange
        string text = "Short text";
        int chunkSize = 100;
        int overlap = 10;

        // Act
        List<string> chunks = ChunkText(text, chunkSize, overlap);

        // Assert
        Assert.AreEqual(1, chunks.Count);
        Assert.AreEqual(text, chunks[0]);
    }

    /// <summary>
    /// Tests that ChunkText splits text into multiple chunks.
    /// </summary>
    [TestMethod]
    public void ChunkText_SplitsIntoMultipleChunks()
    {
        // Arrange
        string text = new('A', 250); // 250 characters
        int chunkSize = 100;
        int overlap = 0;

        // Act
        List<string> chunks = ChunkText(text, chunkSize, overlap);

        // Assert
        Assert.AreEqual(3, chunks.Count);
        Assert.AreEqual(100, chunks[0].Length);
        Assert.AreEqual(100, chunks[1].Length);
        Assert.AreEqual(50, chunks[2].Length);
    }

    /// <summary>
    /// Data-driven test for various overlap scenarios in ChunkText.
    /// </summary>
    [DataTestMethod]
    [DataRow("0123456789ABCDEFGHIJ", 10, 3, "0123456789", "789", DisplayName = "Creates overlapping chunks")]
    [DataRow("AAAABBBBCCCCDDDD", 4, 0, "AAAA", "BBBB", DisplayName = "Zero overlap creates adjacent chunks")]
    [DataRow("0123456789ABCDEF", 5, 5, null, null, DisplayName = "Overlap equal to chunk size")]
    [DataRow("0123456789ABCDEF", 5, 10, null, null, DisplayName = "Overlap larger than chunk size")]
    public void ChunkText_OverlapScenarios(string text, int chunkSize, int overlap, string expectedFirst, string expectedSecond)
    {
        List<string> chunks = ChunkText(text, chunkSize, overlap);

        // For the first two scenarios, check specific chunk content
        if (expectedFirst != null && expectedSecond != null)
        {
            Assert.IsTrue(chunks.Count >= 2, "Should have multiple chunks");
            Assert.AreEqual(expectedFirst, chunks[0]);
            Assert.IsTrue(chunks[1].StartsWith(expectedSecond), $"Second chunk should start with expected overlap: {expectedSecond}");
        }
        else
        {
            // For edge cases (overlap >= chunk size), just check chunk count is reasonable
            Assert.IsTrue(chunks.Count > 0);
            Assert.IsTrue(chunks.Count < 100, "Should not create excessive chunks");
        }
    }

    /// <summary>
    /// Tests that ChunkText handles empty string.
    /// </summary>
    [TestMethod]
    public void ChunkText_HandlesEmptyString()
    {
        // Arrange
        string text = "";
        int chunkSize = 100;
        int overlap = 10;

        // Act
        List<string> chunks = ChunkText(text, chunkSize, overlap);

        // Assert
        Assert.AreEqual(0, chunks.Count, "Empty text should produce no chunks");
    }

    /// <summary>
    /// Tests that ChunkText handles single character.
    /// </summary>
    [TestMethod]
    public void ChunkText_HandlesSingleCharacter()
    {
        // Arrange
        string text = "A";
        int chunkSize = 100;
        int overlap = 10;

        // Act
        List<string> chunks = ChunkText(text, chunkSize, overlap);

        // Assert
        Assert.AreEqual(1, chunks.Count);
        Assert.AreEqual("A", chunks[0]);
    }

    /// <summary>
    /// Tests that ChunkText with chunk size of 1 creates individual character chunks.
    /// </summary>
    [TestMethod]
    public void ChunkText_WithChunkSizeOne_CreatesCharacterChunks()
    {
        // Arrange
        string text = "ABCDE";
        int chunkSize = 1;
        int overlap = 0;

        // Act
        List<string> chunks = ChunkText(text, chunkSize, overlap);

        // Assert
        Assert.AreEqual(5, chunks.Count);
        Assert.AreEqual("A", chunks[0]);
        Assert.AreEqual("B", chunks[1]);
        Assert.AreEqual("C", chunks[2]);
        Assert.AreEqual("D", chunks[3]);
        Assert.AreEqual("E", chunks[4]);
    }

    /// <summary>
    /// Tests that ChunkText preserves whitespace and special characters.
    /// </summary>
    [TestMethod]
    public void ChunkText_PreservesWhitespaceAndSpecialCharacters()
    {
        // Arrange
        string text = "Hello World!\nNew Line\tTab";
        int chunkSize = 15;
        int overlap = 0;

        // Act
        List<string> chunks = ChunkText(text, chunkSize, overlap);

        // Assert
        string reconstructed = string.Concat(chunks);
        Assert.AreEqual(text, reconstructed, "Reconstructed text should match original");
    }

    /// <summary>
    /// Tests that ChunkText handles Unicode characters correctly.
    /// </summary>
    [TestMethod]
    public void ChunkText_HandlesUnicodeCharacters()
    {
        // Arrange
        string text = "Hello 世界 🌍 Émoji";
        int chunkSize = 10;
        int overlap = 2;

        // Act
        List<string> chunks = ChunkText(text, chunkSize, overlap);

        // Assert
        Assert.IsTrue(chunks.Count > 0);
        string reconstructedStart = chunks[0];
        Assert.IsTrue(reconstructedStart.Contains("Hello") || reconstructedStart.Contains("世") || reconstructedStart.Contains("🌍"),
            "Should preserve Unicode characters");
    }

    /// <summary>
    /// Tests that overlapping chunks share common text.
    /// </summary>
    [TestMethod]
    public void ChunkText_OverlappingChunksShareCommonText()
    {
        // Arrange
        string text = "ABCDEFGHIJKLMNOPQRSTUVWXYZ";
        int chunkSize = 10;
        int overlap = 3;

        // Act
        List<string> chunks = ChunkText(text, chunkSize, overlap);

        // Assert
        for (int i = 0; i < chunks.Count - 1; i++)
        {
            string currentChunk = chunks[i];
            string nextChunk = chunks[i + 1];

            // Last 'overlap' characters of current chunk should match first 'overlap' of next chunk
            string currentEnd = currentChunk.Substring(Math.Max(0, currentChunk.Length - overlap));
            string nextStart = nextChunk.Substring(0, Math.Min(overlap, nextChunk.Length));

            Assert.AreEqual(currentEnd, nextStart,
                $"Chunks {i} and {i + 1} should have overlapping content");
        }
    }

    /// <summary>
    /// Tests that text can be reconstructed from non-overlapping chunks.
    /// </summary>
    [TestMethod]
    public void ChunkText_NonOverlappingChunks_CanReconstructText()
    {
        // Arrange
        string text = "The quick brown fox jumps over the lazy dog";
        int chunkSize = 10;
        int overlap = 0;

        // Act
        List<string> chunks = ChunkText(text, chunkSize, overlap);

        // Assert
        string reconstructed = string.Concat(chunks);
        Assert.AreEqual(text, reconstructed);
    }

    /// <summary>
    /// Tests ChunkText with very large text.
    /// </summary>
    [TestMethod]
    public void ChunkText_HandlesLargeText()
    {
        // Arrange
        string text = new('X', 10000);
        int chunkSize = 1000;
        int overlap = 100;

        // Act
        List<string> chunks = ChunkText(text, chunkSize, overlap);

        // Assert
        Assert.IsTrue(chunks.Count >= 10, "Large text should be split into multiple chunks");
        Assert.AreEqual(1000, chunks[0].Length);
        Assert.IsTrue(chunks[chunks.Count - 1].Length <= 1000);
    }

    /// <summary>
    /// Helper method that delegates to the production <see cref="TextChunker.ChunkText(string, int, int)"/>
    /// implementation so tests exercise real controller logic rather than a local re-implementation.
    /// </summary>
    private static List<string> ChunkText(string text, int chunkSize, int overlap)
    {
        return TextChunker.ChunkText(text, chunkSize, overlap).ToList();
    }
}
