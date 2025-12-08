// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace Azure.DataApiBuilder.Service.SemanticCache
{
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
}
}
