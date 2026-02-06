// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Service.SemanticCache;

/// <summary>
/// Implementation of semantic caching service that uses vector embeddings
/// and Azure Managed Redis for similarity-based query caching.
/// </summary>
public class SemanticCacheService : ISemanticCache
{
    private readonly RuntimeConfigProvider _runtimeConfigProvider;
    private readonly RedisVectorStore _vectorStore;
    private readonly ILogger<SemanticCacheService> _logger;

    public SemanticCacheService(
        RuntimeConfigProvider runtimeConfigProvider,
        IEmbeddingService embeddingService,
        RedisVectorStore vectorStore,
        ILogger<SemanticCacheService> logger)
    {
        _runtimeConfigProvider = runtimeConfigProvider ?? throw new ArgumentNullException(nameof(runtimeConfigProvider));
        _vectorStore = vectorStore ?? throw new ArgumentNullException(nameof(vectorStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private static string CreateEmbeddingKey(float[] embedding)
    {
        // Use a deterministic short hash so RedisVectorStore gets a non-empty `query` value.
        // This is not used for similarity search (embedding is), but RedisVectorStore requires a non-empty query string.
        byte[] bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);

        byte[] hash = SHA256.HashData(bytes);
        // 16 hex chars is enough for uniqueness in practice while keeping payload small.
        return "embedding:" + Convert.ToHexString(hash).Substring(0, 16);
    }

    /// <inheritdoc/>
    public async Task<SemanticCacheResult?> QueryAsync(
        float[] embedding,
        int maxResults,
        double similarityThreshold,
        CancellationToken cancellationToken = default)
    {
        if (embedding == null || embedding.Length == 0)
        {
            throw new ArgumentException("Embedding cannot be null or empty.", nameof(embedding));
        }

        try
        {
            _logger.LogDebug(
                "Searching semantic cache with {EmbeddingLength} dimensions, maxResults: {MaxResults}, threshold: {Threshold}",
                embedding.Length,
                maxResults,
                similarityThreshold);

            // Search for similar vectors in Redis
            var results = await _vectorStore.SearchSimilarAsync(
                embedding,
                maxResults,
                similarityThreshold,
                cancellationToken);

            if (results.Any())
            {
                // Return the best match (highest similarity)
                var bestMatch = results.First();
                
                _logger.LogInformation(
                    "Semantic cache hit! Key: {Key}, Similarity: {Score:F4}",
                    bestMatch.Key,
                    bestMatch.Score);

                return new SemanticCacheResult(
                    bestMatch.Response,
                    bestMatch.Score,
                    originalQuery: null); // Query text not stored in search results
            }

            _logger.LogDebug("No semantic cache hit found");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error querying semantic cache");
            // Don't throw - gracefully degrade to no cache
            return null;
        }
    }

    /// <inheritdoc/>
    public async Task StoreAsync(
        float[] embedding,
        string responseJson,
        TimeSpan? ttl = null,
        CancellationToken cancellationToken = default)
    {
        if (embedding == null || embedding.Length == 0)
        {
            throw new ArgumentException("Embedding cannot be null or empty.", nameof(embedding));
        }

        if (string.IsNullOrWhiteSpace(responseJson))
        {
            throw new ArgumentException("Response JSON cannot be null or empty.", nameof(responseJson));
        }

        try
        {
            _logger.LogDebug(
                "Storing response in semantic cache with {EmbeddingLength} dimensions",
                embedding.Length);

            // Get configuration for TTL
            var config = _runtimeConfigProvider.GetConfig();
            var semanticCacheConfig = config.Runtime?.SemanticCache;
            
            // Use provided TTL, or fall back to config, or use default
            int expireSeconds;
            if (ttl.HasValue)
            {
                expireSeconds = (int)ttl.Value.TotalSeconds;
            }
            else if (semanticCacheConfig?.ExpireSeconds.HasValue == true)
            {
                expireSeconds = semanticCacheConfig.ExpireSeconds.Value;
            }
            else
            {
                expireSeconds = SemanticCacheOptions.DEFAULT_EXPIRE_SECONDS;
            }

            // Store in Redis vector store
            // Note: Caller only provides embedding+response. Provide a deterministic non-empty query id.
            await _vectorStore.StoreAsync(
                query: CreateEmbeddingKey(embedding),
                embedding: embedding,
                response: responseJson,
                expireSeconds: expireSeconds,
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Successfully stored response in semantic cache with TTL {ExpireSeconds}s",
                expireSeconds);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing in semantic cache");
            // Don't throw - gracefully degrade if caching fails
        }
    }
}

