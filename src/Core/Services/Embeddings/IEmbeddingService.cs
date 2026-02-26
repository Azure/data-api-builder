// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Services.Embeddings;

/// <summary>
/// Result of a TryEmbed operation.
/// </summary>
/// <param name="Success">Whether the embedding was generated successfully.</param>
/// <param name="Embedding">The embedding vector, or null if unsuccessful.</param>
/// <param name="ErrorMessage">Error message if unsuccessful, or null if successful.</param>
public record EmbeddingResult(bool Success, float[]? Embedding, string? ErrorMessage = null);

/// <summary>
/// Result of a TryEmbedBatch operation.
/// </summary>
/// <param name="Success">Whether the embeddings were generated successfully.</param>
/// <param name="Embeddings">The embedding vectors, or null if unsuccessful.</param>
/// <param name="ErrorMessage">Error message if unsuccessful, or null if successful.</param>
public record EmbeddingBatchResult(bool Success, float[][]? Embeddings, string? ErrorMessage = null);

/// <summary>
/// Service interface for text embedding/vectorization.
/// Supports both single text and batch embedding operations.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Gets whether the embedding service is enabled.
    /// </summary>
    bool IsEnabled { get; }

    /// <summary>
    /// Attempts to generate an embedding vector for a single text input.
    /// Returns a result indicating success or failure without throwing exceptions.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Result containing the embedding if successful, or error information if not.</returns>
    Task<EmbeddingResult> TryEmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to generate embedding vectors for multiple text inputs in a batch.
    /// Returns a result indicating success or failure without throwing exceptions.
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>Result containing the embeddings if successful, or error information if not.</returns>
    Task<EmbeddingBatchResult> TryEmbedBatchAsync(string[] texts, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates an embedding vector for a single text input.
    /// Throws if the service is disabled or an error occurs.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The embedding vector as an array of floats.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the service is disabled.</exception>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embedding vectors for multiple text inputs in a batch.
    /// Throws if the service is disabled or an error occurs.
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The embedding vectors as an array of float arrays, matching input order.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the service is disabled.</exception>
    Task<float[][]> EmbedBatchAsync(string[] texts, CancellationToken cancellationToken = default);
}
