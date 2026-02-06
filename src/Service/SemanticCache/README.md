# Semantic Caching Implementation

This directory contains the complete semantic caching implementation for Data API Builder (DAB) using Azure OpenAI embeddings and Azure Managed Redis with vector search capabilities.

## 🎯 Scope

**Currently supported:** SQL databases only (SQL Server, PostgreSQL, MySQL)

Semantic caching is integrated at the `SqlQueryEngine` level and works for:
- ✅ GraphQL queries (SELECT operations)
- ✅ REST API queries
- ✅ Complex SQL queries with joins and filters

**Not currently supported:**
- ❌ Cosmos DB queries
- ❌ Mutation operations (INSERT, UPDATE, DELETE)
- ❌ Stored procedure calls

**Future enhancement:** Could be extended to Cosmos DB (SQL API) if there's demand.

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

### Using DAB CLI (Recommended)

You can configure semantic caching using the `dab configure` command:

```bash
# Enable semantic cache with minimal configuration
dab configure \
  --runtime.semantic-cache.enabled true \
  --runtime.semantic-cache.azure-managed-redis.connection-string "your-redis.redis.cache.windows.net:6380,password=yourpassword,ssl=True" \
  --runtime.semantic-cache.embedding-provider.type "azure-openai" \
  --runtime.semantic-cache.embedding-provider.endpoint "https://your-openai.openai.azure.com" \
  --runtime.semantic-cache.embedding-provider.api-key "your-api-key" \
  --runtime.semantic-cache.embedding-provider.model "text-embedding-ada-002"

# With all options
dab configure \
  --runtime.semantic-cache.enabled true \
  --runtime.semantic-cache.similarity-threshold 0.85 \
  --runtime.semantic-cache.max-results 5 \
  --runtime.semantic-cache.expire-seconds 86400 \
  --runtime.semantic-cache.azure-managed-redis.connection-string "your-redis.redis.cache.windows.net:6380,password=yourpassword,ssl=True" \
  --runtime.semantic-cache.azure-managed-redis.vector-index "dab-semantic-index" \
  --runtime.semantic-cache.azure-managed-redis.key-prefix "dab:sc:" \
  --runtime.semantic-cache.embedding-provider.type "azure-openai" \
  --runtime.semantic-cache.embedding-provider.endpoint "https://your-openai.openai.azure.com" \
  --runtime.semantic-cache.embedding-provider.api-key "your-api-key" \
  --runtime.semantic-cache.embedding-provider.model "text-embedding-ada-002"
```

**Available CLI Options:**

| Option | Type | Description |
|--------|------|-------------|
| `--runtime.semantic-cache.enabled` | bool | Enable/disable semantic caching |
| `--runtime.semantic-cache.similarity-threshold` | double | Minimum similarity (0.0-1.0) for cache hit. Default: 0.85 |
| `--runtime.semantic-cache.max-results` | int | Max KNN results to retrieve. Default: 5 |
| `--runtime.semantic-cache.expire-seconds` | int | TTL for cached entries in seconds. Default: 86400 |
| `--runtime.semantic-cache.azure-managed-redis.connection-string` | string | Redis connection string (required) |
| `--runtime.semantic-cache.azure-managed-redis.vector-index` | string | Vector index name. Default: "dab-semantic-index" |
| `--runtime.semantic-cache.azure-managed-redis.key-prefix` | string | Redis key prefix. Default: "dab:sc:" |
| `--runtime.semantic-cache.embedding-provider.type` | string | Provider type (currently only "azure-openai") |
| `--runtime.semantic-cache.embedding-provider.endpoint` | string | Azure OpenAI endpoint URL (required) |
| `--runtime.semantic-cache.embedding-provider.api-key` | string | Azure OpenAI API key (required) |
| `--runtime.semantic-cache.embedding-provider.model` | string | Embedding model name (required) |

### Manual Configuration (JSON)

Alternatively, you can manually add to your `dab-config.json`:

### Minimal Configuration (Required Settings Only)

```json
{
  "runtime": {
    "semantic-cache": {
      "enabled": true,
      "azure-managed-redis": {
        "connection-string": "your-redis.redis.cache.windows.net:6380,password=yourpassword,ssl=True"
      },
      "embedding-provider": {
        "type": "azure-openai",
        "endpoint": "https://your-openai.openai.azure.com",
        "api-key": "your-api-key",
        "model": "text-embedding-ada-002"
      }
    }
  }
}
```

### Full Configuration (All Options)

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
        "key-prefix": "dab:sc:"
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

### Environment Variables (Recommended for Production)

```bash
# .env file or Azure App Configuration
REDIS_CONNECTION_STRING="your-redis.redis.cache.windows.net:6380,password=xyz,ssl=True"
AZURE_OPENAI_ENDPOINT="https://your-openai.openai.azure.com"
AZURE_OPENAI_KEY="your-api-key-here"
```

Then in config:
```json
{
  "runtime": {
    "semantic-cache": {
      "enabled": true,
      "azure-managed-redis": {
        "connection-string": "@env('REDIS_CONNECTION_STRING')"
      },
      "embedding-provider": {
        "endpoint": "@env('AZURE_OPENAI_ENDPOINT')",
        "api-key": "@env('AZURE_OPENAI_KEY')",
        "model": "text-embedding-ada-002"
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

### Unit Tests ✅ (Completed)

Located in `Service.Tests/UnitTests/`:
- `SemanticCacheServiceTests.cs` - Tests SemanticCacheService orchestration
- `AzureOpenAIEmbeddingServiceTests.cs` - Tests embedding generation with mocks
- `SemanticCacheOptionsTests.cs` - Tests configuration validation

**Run unit tests:**
```powershell
cd src
dotnet test Service.Tests/Azure.DataApiBuilder.Service.Tests.csproj --filter "FullyQualifiedName~SemanticCache"
```

Test coverage includes:
- Mock `IConnectionMultiplexer` for Redis tests
- Mock `IHttpClientFactory` for Azure OpenAI tests
- Configuration validation scenarios
- Error handling and graceful degradation

### Integration Tests ✅ (Completed)

Located in `Service.Tests/IntegrationTests/SemanticCacheIntegrationTests.cs`

Tests cover:
1. **Service registration**: Validates DI container setup
2. **Cache hit/miss scenarios**: Tests query matching logic
3. **Store operations**: Validates storing new results
4. **Error handling**: Tests graceful degradation on failures
5. **Configuration validation**: Tests invalid configs
6. **Similarity thresholding**: Validates filtering logic

**Run integration tests:**
```powershell
cd src
dotnet test Service.Tests/Azure.DataApiBuilder.Service.Tests.csproj --filter "FullyQualifiedName~SemanticCacheIntegrationTests"
```

**Prerequisites for full integration tests:**
- Azure Managed Redis Enterprise with RediSearch module
- Azure OpenAI endpoint with embedding model deployed
- Set environment variables:
  - `REDIS_CONNECTION_STRING`
  - `AZURE_OPENAI_ENDPOINT`
  - `AZURE_OPENAI_KEY`

### Manual End-to-End Tests

#### Setup Test Environment

1. **Create Azure Resources**
```bash
# Redis Enterprise with RediSearch
az redis create \
  --resource-group dab-test-rg \
  --name dab-semantic-cache-test \
  --location eastus \
  --sku Enterprise_E10 \
  --modules RediSearch

# Get connection string
az redis list-keys --resource-group dab-test-rg --name dab-semantic-cache-test
```

2. **Configure DAB**
Create `dab-config.SemanticCache.json`:
```json
{
  "$schema": "https://github.com/Azure/data-api-builder/releases/download/v0.12.0/dab.draft.schema.json",
  "data-source": {
    "database-type": "mssql",
    "connection-string": "@env('SQL_CONNECTION_STRING')"
  },
  "runtime": {
    "cache": {
      "enabled": true,
      "ttl-seconds": 60
    },
    "semantic-cache": {
      "enabled": true,
      "similarity-threshold": 0.85,
      "max-results": 5,
      "expire-seconds": 3600,
      "azure-managed-redis": {
        "connection-string": "@env('REDIS_CONNECTION_STRING')"
      },
      "embedding-provider": {
        "type": "azure-openai",
        "endpoint": "@env('AZURE_OPENAI_ENDPOINT')",
        "api-key": "@env('AZURE_OPENAI_KEY')",
        "model": "text-embedding-ada-002"
      }
    },
    "rest": { "enabled": true, "path": "/api" },
    "graphql": { "enabled": true, "path": "/graphql" },
    "host": {
      "mode": "development",
      "authentication": { "provider": "StaticWebApps" }
    }
  },
  "entities": {
    "Book": {
      "source": "dbo.books",
      "permissions": [{ "role": "anonymous", "actions": ["read"] }],
      "cache": { "enabled": true, "ttl-seconds": 60 }
    }
  }
}
```

3. **Start DAB**
```powershell
cd src/Service
dotnet run -- start --ConfigFileName dab-config.SemanticCache.json
```

4. **Test Queries**

**Test 1: Cache Miss (First Query)**
```graphql
query {
  books(filter: { id: { gt: 5 } }) {
    items {
      id
      title
    }
  }
}
```
Expected: Database query executed, logs show "Semantic cache miss"

**Test 2: Semantic Cache Hit (Similar Query)**
```graphql
query {
  books(filter: { id: { gte: 6 } }) {
    items {
      id
      title
    }
  }
}
```
Expected: Logs show "Semantic cache hit! Similarity: 0.9X"

**Test 3: Check Logs**
```
[Information] Semantic cache miss for query: SELECT * FROM books WHERE id > 5
[Information] Generating embedding for query (length: 35 chars)
[Information] Stored query result in semantic cache with TTL 3600s
[Information] Semantic cache hit! Similarity: 0.92 for query: SELECT * FROM books WHERE id >= 6
```

5. **Verify in Redis**
```bash
redis-cli -h your-redis.redis.cache.windows.net -p 10000 -a your-password --tls

# Check index
FT.INFO dab-semantic-index

# Check stored entries
FT.SEARCH dab-semantic-index "*" LIMIT 0 5

# Check specific key
HGETALL dab:sc:some-guid
```

### Load Tests (Future Work)

**Recommended tools:**
- k6 for load testing (existing framework in `Service.Tests/ConcurrentTests/`)
- Apache Bench for simple HTTP load
- Azure Load Testing service

**Test scenarios:**
- 100-1000 queries/second
- Mix of similar/dissimilar queries (50/50 distribution)
- Measure cache hit rate over time
- Monitor Redis memory usage
- Track embedding generation latency

**Key metrics to track:**
1. Cache hit rate: Target >60% for production workloads
2. P95 latency: Should be <300ms including embedding generation
3. Redis memory usage: Should stay below 80% capacity
4. Embedding service rate limit hits: Should be <1%

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
