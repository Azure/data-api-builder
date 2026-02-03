// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Services;

/// <summary>
/// Service interface for text embedding/vectorization.
/// Supports both single text and batch embedding operations.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>
    /// Generates an embedding vector for a single text input.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The embedding vector as an array of floats.</returns>
    Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken = default);

    /// <summary>
    /// Generates embedding vectors for multiple text inputs in a batch.
    /// </summary>
    /// <param name="texts">The texts to embed.</param>
    /// <param name="cancellationToken">Cancellation token for the operation.</param>
    /// <returns>The embedding vectors as an array of float arrays, matching input order.</returns>
    Task<float[][]> EmbedBatchAsync(string[] texts, CancellationToken cancellationToken = default);
}
