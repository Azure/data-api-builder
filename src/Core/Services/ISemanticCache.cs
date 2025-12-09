// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Services
{
    /// <summary>
    /// Interface for semantic caching service that uses vector embeddings
    /// and similarity search to cache query responses.
    /// </summary>
    public interface ISemanticCache
    {
        /// <summary>
        /// Query the semantic cache with an embedding vector.
        /// Returns a result if a cached response exists above the similarity threshold.
        /// </summary>
        /// <param name="embedding">Embedding vector of the request.</param>
        /// <param name="maxResults">Max number of nearest neighbors to consider.</param>
        /// <param name="similarityThreshold">Minimum cosine similarity to accept as a hit.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Cached result if found, null otherwise.</returns>
        Task<SemanticCacheResult?> QueryAsync(
            float[] embedding,
            int maxResults,
            double similarityThreshold,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Store a response in the semantic cache with its embedding.
        /// </summary>
        /// <param name="embedding">Embedding vector of the request.</param>
        /// <param name="responseJson">The JSON response to store.</param>
        /// <param name="ttl">Optional time-to-live for the cache entry.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        Task StoreAsync(
            float[] embedding,
            string responseJson,
            TimeSpan? ttl = null,
            CancellationToken cancellationToken = default);
    }

    /// <summary>
    /// Result from a semantic cache query containing the cached response and similarity score.
    /// </summary>
    public class SemanticCacheResult
    {
        /// <summary>
        /// The cached JSON response.
        /// </summary>
        public string Response { get; }

        /// <summary>
        /// The cosine similarity score between the query and cached entry (0.0 to 1.0).
        /// </summary>
        public double Similarity { get; }

        /// <summary>
        /// The original query text that was cached (optional).
        /// </summary>
        public string? OriginalQuery { get; }

        public SemanticCacheResult(string response, double similarity, string? originalQuery = null)
        {
            Response = response ?? throw new ArgumentNullException(nameof(response));
            Similarity = similarity;
            OriginalQuery = originalQuery;
        }
    }
}
