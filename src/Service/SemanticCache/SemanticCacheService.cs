// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
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
            // Note: Using empty string for query since we only have embedding at this point
            // The query text would need to be passed from the calling context if needed
            await _vectorStore.StoreAsync(
                query: string.Empty,
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

