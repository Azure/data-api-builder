// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using Azure.DataApiBuilder.Config.ObjectModel.Embeddings;
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
        string text = new string('A', 250); // 250 characters
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
    /// Tests that ChunkText creates overlapping chunks.
    /// </summary>
    [TestMethod]
    public void ChunkText_CreatesOverlappingChunks()
    {
        // Arrange
        string text = "0123456789ABCDEFGHIJ"; // 20 characters
        int chunkSize = 10;
        int overlap = 3;

        // Act
        List<string> chunks = ChunkText(text, chunkSize, overlap);

        // Assert
        Assert.IsTrue(chunks.Count >= 2, "Should have multiple chunks");

        // First chunk: chars 0-9
        Assert.AreEqual("0123456789", chunks[0]);

        // Second chunk should start at position 7 (10 - 3 overlap)
        // and include chars 7-16
        if (chunks.Count >= 2)
        {
            Assert.IsTrue(chunks[1].StartsWith("789"), "Second chunk should start with overlap from first chunk");
        }
    }

    /// <summary>
    /// Tests that ChunkText with zero overlap creates adjacent chunks.
    /// </summary>
    [TestMethod]
    public void ChunkText_WithZeroOverlap_CreatesAdjacentChunks()
    {
        // Arrange
        string text = "AAAABBBBCCCCDDDD"; // 16 characters
        int chunkSize = 4;
        int overlap = 0;

        // Act
        List<string> chunks = ChunkText(text, chunkSize, overlap);

        // Assert
        Assert.AreEqual(4, chunks.Count);
        Assert.AreEqual("AAAA", chunks[0]);
        Assert.AreEqual("BBBB", chunks[1]);
        Assert.AreEqual("CCCC", chunks[2]);
        Assert.AreEqual("DDDD", chunks[3]);
    }

    /// <summary>
    /// Tests that ChunkText handles overlap equal to chunk size.
    /// </summary>
    [TestMethod]
    public void ChunkText_HandlesOverlapEqualToChunkSize()
    {
        // Arrange
        string text = "0123456789ABCDEF"; // 16 characters
        int chunkSize = 5;
        int overlap = 5;

        // Act
        List<string> chunks = ChunkText(text, chunkSize, overlap);

        // Assert - each chunk should start at the same position as previous (overlap = size)
        // This should still terminate and not create infinite chunks
        Assert.IsTrue(chunks.Count > 0);
        Assert.IsTrue(chunks.Count < 100, "Should not create excessive chunks");
    }

    /// <summary>
    /// Tests that ChunkText handles overlap larger than chunk size.
    /// </summary>
    [TestMethod]
    public void ChunkText_HandlesOverlapLargerThanChunkSize()
    {
        // Arrange
        string text = "0123456789ABCDEF"; // 16 characters
        int chunkSize = 5;
        int overlap = 10;

        // Act
        List<string> chunks = ChunkText(text, chunkSize, overlap);

        // Assert - should handle gracefully without infinite loop
        Assert.IsTrue(chunks.Count > 0);
        Assert.IsTrue(chunks.Count < 100, "Should not create excessive chunks");
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
        Assert.IsTrue(reconstructedStart.Contains("Hello") || reconstructedStart.Contains("世"),
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
        string text = new string('X', 10000);
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
    /// Helper method that invokes the ChunkText logic from EmbeddingController.
    /// This uses reflection or a test-friendly approach to access the private method.
    /// <summary>
    /// Helper method that invokes the production ChunkText logic from EmbeddingController via reflection.
    /// This ensures tests validate the actual production implementation rather than a duplicate.
    /// </summary>
    private static List<string> ChunkText(string text, int chunkSize, int overlap)
    {
        // Load the EmbeddingController type
        Type? embeddingControllerType = null;
        foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            embeddingControllerType = assembly.GetType("Azure.DataApiBuilder.Service.Controllers.EmbeddingController");
            if (embeddingControllerType is not null)
            {
                break;
            }
        }

        Assert.IsNotNull(
            embeddingControllerType,
            "Could not locate Azure.DataApiBuilder.Service.Controllers.EmbeddingController in loaded assemblies.");

        // Find the ChunkText method
        System.Reflection.MethodInfo? chunkTextMethod = embeddingControllerType!.GetMethod(
            "ChunkText",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static,
            binder: null,
            types: new[] { typeof(string), typeof(EmbeddingsChunkingOptions) },
            modifiers: null);

        Assert.IsNotNull(
            chunkTextMethod,
            "Could not locate ChunkText(string, EmbeddingsChunkingOptions) on EmbeddingController.");

        // Create EmbeddingsChunkingOptions
        EmbeddingsChunkingOptions options = new(
            Enabled: true,
            SizeChars: chunkSize,
            OverlapChars: overlap);

        // Get controller instance if method is not static
        object? controllerInstance = null;
        if (!chunkTextMethod!.IsStatic)
        {
            // ChunkText should be marked as static in the production code.
            // If it's not static, we need to create an instance.
            // Using Activator with null parameters since we only need ChunkText which doesn't use instance fields.
            try
            {
#pragma warning disable SYSLIB0050 // Type or member is obsolete
                controllerInstance = System.Runtime.Serialization.FormatterServices.GetUninitializedObject(embeddingControllerType);
#pragma warning restore SYSLIB0050
            }
            catch (Exception ex)
            {
                Assert.Fail($"Failed to create EmbeddingController instance for testing: {ex.Message}");
            }
        }

        // Invoke ChunkText method
        object? result = chunkTextMethod.Invoke(controllerInstance, new object?[] { text, options });

        Assert.IsNotNull(result, "EmbeddingController.ChunkText returned null.");
        Assert.IsInstanceOfType(result, typeof(string[]), "EmbeddingController.ChunkText did not return string[].");

        return new List<string>((string[])result);
    }
}
