# Semantic Caching Implementation

This directory contains the complete semantic caching implementation for Data API Builder (DAB) using Azure OpenAI embeddings and Azure Managed Redis with vector search capabilities.

## Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    GraphQL/REST Request                      │
└───────────────────────────┬─────────────────────────────────┘
                            │
                            ▼
┌─────────────────────────────────────────────────────────────┐
│              SemanticCacheService (ISemanticCache)          │
│  - QueryAsync(): Search for similar cached responses        │
│  - StoreAsync(): Store new responses with embeddings        │
└──────────────┬────────────────────────┬─────────────────────┘
               │                        │
               ▼                        ▼
┌──────────────────────────┐  ┌────────────────────────────┐
│  AzureOpenAIEmbedding    │  │    RedisVectorStore        │
│       Service            │  │  - SearchSimilarAsync()    │
│                          │  │  - StoreAsync()            │
│  - GenerateEmbedding()   │  │  - EnsureIndexExists()     │
└──────────────────────────┘  └────────────────────────────┘
               │                        │
               ▼                        ▼
┌──────────────────────────┐  ┌────────────────────────────┐
│   Azure OpenAI Service   │  │  Azure Managed Redis       │
│   (Embeddings API)       │  │  (RediSearch Vector)       │
└──────────────────────────┘  └────────────────────────────┘
```

## Components

### 1. **ISemanticCache** (Interface)
- Defines the contract for semantic caching operations
- Located in: `Service/SemanticCache/ISemanticCache.cs`

### 2. **SemanticCacheService** (Implementation)
- Main orchestration service
- Coordinates embedding generation and vector storage/retrieval
- Graceful error handling with fallback to no cache

### 3. **AzureOpenAIEmbeddingService**
- Generates vector embeddings using Azure OpenAI
- Implements retry logic with exponential backoff
- Handles rate limiting (HTTP 429)
- Supports models: text-embedding-3-small, text-embedding-3-large

### 4. **RedisVectorStore**
- Manages Redis vector operations using RediSearch
- Implements KNN (K-Nearest Neighbors) search
- COSINE similarity metric for text embeddings
- Automatic index management

### 5. **SemanticCacheResult**
- DTO for cache query results
- Contains response JSON, similarity score, and optional query text

## Configuration

Add to your `dab-config.json`:

```json
{
  "runtime": {
    "semantic-cache": {
      "enabled": true,
      "similarity-threshold": 0.85,
      "max-results": 5,
      "expire-seconds": 86400,
      "azure-managed-redis": {
        "connection-string": "${REDIS_CONNECTION_STRING}",
        "vector-index": "dab-semantic-index",
        "key-prefix": "resp:"
      },
      "embedding-provider": {
        "type": "azure-openai",
        "endpoint": "${AZURE_OPENAI_ENDPOINT}",
        "api-key": "${AZURE_OPENAI_KEY}",
        "model": "text-embedding-3-small"
      }
    }
  }
}
```

### Configuration Parameters

| Parameter | Type | Default | Description |
|-----------|------|---------|-------------|
| `enabled` | bool | false | Enable/disable semantic caching |
| `similarity-threshold` | double | 0.85 | Minimum similarity (0.0-1.0) for cache hit |
| `max-results` | int | 5 | Max KNN results to retrieve |
| `expire-seconds` | int | 86400 | TTL for cached entries (1 day) |

### Azure Managed Redis Options

| Parameter | Required | Description |
|-----------|----------|-------------|
| `connection-string` | Yes | Redis connection string with authentication |
| `vector-index` | No | Index name (default: "dab-semantic-index") |
| `key-prefix` | No | Key prefix (default: "resp:") |

### Embedding Provider Options

| Parameter | Required | Description |
|-----------|----------|-------------|
| `type` | Yes | Provider type (currently only "azure-openai") |
| `endpoint` | Yes | Azure OpenAI endpoint URL |
| `api-key` | Yes | Azure OpenAI API key |
| `model` | Yes | Embedding model deployment name |

## Usage Example

### Basic Integration Pattern

```csharp
// Inject ISemanticCache in your service
public class YourQueryService
{
    private readonly ISemanticCache _semanticCache;
    private readonly RuntimeConfigProvider _configProvider;
    
    public async Task<string> ExecuteQueryAsync(string queryText)
    {
        var config = _configProvider.GetConfig();
        
        // Only use semantic cache if enabled
        if (!config.IsSemanticCachingEnabled)
        {
            return await ExecuteQueryNormally(queryText);
        }
        
        var semanticConfig = config.Runtime!.SemanticCache!;
        
        // 1. Generate embedding for the query
        // Note: You'd get this from IEmbeddingService
        float[] queryEmbedding = await GenerateEmbedding(queryText);
        
        // 2. Try to get cached response
        var cachedResult = await _semanticCache.QueryAsync(
            embedding: queryEmbedding,
            maxResults: semanticConfig.MaxResults ?? 5,
            similarityThreshold: semanticConfig.SimilarityThreshold ?? 0.85);
        
        if (cachedResult != null)
        {
            // Cache hit!
            return cachedResult.ResponseJson;
        }
        
        // 3. Cache miss - execute query normally
        string response = await ExecuteQueryNormally(queryText);
        
        // 4. Store in semantic cache (fire and forget)
        _ = Task.Run(async () =>
        {
            try
            {
                await _semanticCache.StoreAsync(
                    embedding: queryEmbedding,
                    responseJson: response,
                    ttl: TimeSpan.FromSeconds(semanticConfig.ExpireSeconds ?? 86400));
            }
            catch (Exception ex)
            {
                // Log but don't fail the request
                _logger.LogWarning(ex, "Failed to store in semantic cache");
            }
        });
        
        return response;
    }
}
```

## How It Works

### Query Flow (Cache Hit)

1. **Request comes in**: GraphQL query or REST request
2. **Generate embedding**: Convert query text to vector using Azure OpenAI
3. **Search Redis**: Find similar vectors using KNN search
4. **Check threshold**: Filter results by similarity score
5. **Return cached response**: If match found, return immediately

### Query Flow (Cache Miss)

1. **Request comes in**: GraphQL query or REST request
2. **Generate embedding**: Convert query text to vector
3. **Search Redis**: No similar vectors found above threshold
4. **Execute query**: Run against database normally
5. **Store result**: Save response + embedding to Redis
6. **Return response**: Return query result to client

### Similarity Calculation

The system uses **COSINE similarity**:
- Range: 0.0 (orthogonal) to 1.0 (identical)
- Formula: `similarity = 1.0 - (cosine_distance / 2.0)`
- Typical threshold: 0.80-0.90

**Example similarities:**
- 0.95-1.00: Nearly identical questions
- 0.85-0.95: Very similar questions
- 0.70-0.85: Somewhat similar questions
- <0.70: Different questions

## Performance Characteristics

### Latency

- **Embedding generation**: 50-200ms (Azure OpenAI)
- **Redis vector search**: 5-50ms (depends on corpus size)
- **Total cache check**: 55-250ms

### Memory Usage

Per cached entry (1536 dimensions):
- Vector: ~6 KB (4 bytes × 1536)
- Metadata: ~200 bytes
- Response: Variable (depends on JSON size)
- **Total**: ~6.5 KB + response size

### Scalability

- **Vectors stored**: Up to 100K-1M (depends on Redis memory)
- **Search performance**: O(n) for FLAT index, sub-linear for HNSW
- **Index size**: ~650 MB for 100K vectors (1536 dims)

## Error Handling

All components implement graceful degradation:

1. **Azure OpenAI failures**: Retry with exponential backoff (3 attempts)
2. **Redis failures**: Log error, continue without cache
3. **Invalid configuration**: Throw at startup (fail fast)
4. **Concurrent index creation**: Handle "already exists" error

## Monitoring & Logging

### Log Levels

- **Debug**: Query parameters, vector dimensions
- **Info**: Cache hits, storage success, index creation
- **Warning**: Rate limiting, retries, configuration issues
- **Error**: Service failures, network errors

### Key Metrics to Track

1. **Cache hit rate**: `cache_hits / total_queries`
2. **Average similarity score**: Quality of matches
3. **Embedding generation time**: Azure OpenAI latency
4. **Vector search time**: Redis query performance
5. **Storage time**: Write latency

## Redis Requirements

### Azure Managed Redis Configuration

- **Tier**: Enterprise (includes RediSearch module)
- **Redis version**: 6.2+
- **Modules**: RediSearch 2.x or higher
- **Memory**: Minimum 1 GB (depends on corpus size)
- **Network**: VNet integration recommended for security

### Index Configuration

```redis
FT.CREATE dab-semantic-index
  ON HASH PREFIX 1 resp:
  SCHEMA
    query TEXT
    embedding VECTOR FLAT 6 
      TYPE FLOAT32 
      DIM 1536 
      DISTANCE_METRIC COSINE
    response TEXT
    timestamp NUMERIC
    dimensions NUMERIC
```

## Testing

### Unit Tests

Test each component independently:
- Mock `IConnectionMultiplexer` for Redis tests
- Mock `IHttpClientFactory` for Azure OpenAI tests
- Use test doubles for `RuntimeConfigProvider`

### Integration Tests

1. **Embedding generation**: Test with real Azure OpenAI
2. **Vector storage/retrieval**: Test with Redis container
3. **End-to-end flow**: Test full semantic cache workflow

### Load Tests

Simulate production load:
- 1000 queries/second
- Varying query similarity distributions
- Monitor memory usage and latency

## Troubleshooting

### Common Issues

1. **"Index already exists" error**: Ignore, it's safe (concurrent creation)
2. **Rate limiting (429)**: Increase Azure OpenAI quota or adjust retry delays
3. **Dimension mismatch**: Ensure embedding model matches index dimension
4. **Low cache hit rate**: Lower similarity threshold or increase corpus size

### Debug Tips

Enable debug logging to see:
- Embedding dimensions
- Similarity scores
- Redis command details
- Cache hit/miss patterns

## Future Enhancements

- [ ] Support for HNSW index (faster search for large corpus)
- [ ] Batch embedding generation
- [ ] Query text storage with embeddings
- [ ] Cache invalidation strategies
- [ ] Multi-tenant key isolation
- [ ] Embedding model hot-swapping
- [ ] Prometheus metrics export

## References

- [Azure OpenAI Embeddings](https://learn.microsoft.com/azure/ai-services/openai/concepts/embeddings)
- [RediSearch Vector Similarity](https://redis.io/docs/stack/search/reference/vectors/)
- [Azure Managed Redis Enterprise](https://learn.microsoft.com/azure/azure-cache-for-redis/cache-overview)
