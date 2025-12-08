// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Azure.DataApiBuilder.Service.SemanticCache;

/// <summary>
/// Handles Redis vector store operations for semantic caching using RediSearch vector similarity.
/// </summary>
public class RedisVectorStore
{
    private readonly AzureManagedRedisOptions _options;
    private readonly IConnectionMultiplexer _redis;
    private readonly ILogger<RedisVectorStore> _logger;
    private readonly IDatabase _database;
    private bool _indexCreated = false;

    // Field names for Redis hash
    private const string FIELD_QUERY = "query";
    private const string FIELD_EMBEDDING = "embedding";
    private const string FIELD_RESPONSE = "response";
    private const string FIELD_TIMESTAMP = "timestamp";
    private const string FIELD_DIMENSIONS = "dimensions";

    public RedisVectorStore(
        AzureManagedRedisOptions options,
        IConnectionMultiplexer redis,
        ILogger<RedisVectorStore> logger)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrEmpty(_options.ConnectionString))
        {
            throw new ArgumentException("Redis connection string is required.", nameof(options));
        }

        _database = _redis.GetDatabase();
    }

    /// <summary>
    /// Searches for similar vectors in Redis using RediSearch vector similarity search.
    /// </summary>
    /// <param name="queryVector">The query embedding vector.</param>
    /// <param name="maxResults">Maximum number of results to return.</param>
    /// <param name="similarityThreshold">Minimum similarity threshold (0.0 to 1.0).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of similar cached entries with their similarity scores.</returns>
    public async Task<List<(string Key, double Score, string Response)>> SearchSimilarAsync(
        float[] queryVector,
        int maxResults,
        double similarityThreshold,
        CancellationToken cancellationToken = default)
    {
        if (queryVector == null || queryVector.Length == 0)
        {
            throw new ArgumentException("Query vector cannot be null or empty.", nameof(queryVector));
        }

        try
        {
            _logger.LogDebug(
                "Searching for similar vectors with max results: {MaxResults}, threshold: {Threshold}",
                maxResults,
                similarityThreshold);

            // Ensure index exists before searching
            await EnsureIndexExistsAsync(cancellationToken);

            // Convert float array to byte array for Redis
            byte[] vectorBytes = ConvertFloatArrayToBytes(queryVector);

            // Build FT.SEARCH query for vector similarity
            // KNN query format: *=>[KNN K @field_name $vector AS score]
            string indexName = GetIndexName();
            string keyPrefix = _options.KeyPrefix ?? "resp:";
            
            // Execute FT.SEARCH command
            // Note: RediSearch uses COSINE similarity by default (1.0 = identical, 0.0 = orthogonal)
            var result = await _database.ExecuteAsync(
                "FT.SEARCH",
                indexName,
                $"*=>[KNN {maxResults} @{FIELD_EMBEDDING} $vector AS score]",
                "PARAMS", "2", "vector", vectorBytes,
                "SORTBY", "score", "ASC",
                "DIALECT", "2",
                "RETURN", "3", FIELD_RESPONSE, "score", FIELD_QUERY);

            var results = new List<(string Key, double Score, string Response)>();

            if (result.Type == ResultType.Array)
            {
                var resultArray = (RedisResult[])result!;
                
                // First element is the count
                if (resultArray.Length > 0)
                {
                    int count = (int)resultArray[0];
                    _logger.LogDebug("Redis returned {Count} results", count);

                    // Results come in pairs: [key, [field1, value1, field2, value2, ...]]
                    for (int i = 1; i < resultArray.Length; i += 2)
                    {
                        if (i + 1 < resultArray.Length)
                        {
                            string key = (string)resultArray[i]!;
                            var fields = (RedisResult[])resultArray[i + 1]!;

                            double score = 0.0;
                            string? response = null;

                            // Parse fields
                            for (int j = 0; j < fields.Length; j += 2)
                            {
                                if (j + 1 < fields.Length)
                                {
                                    string fieldName = (string)fields[j]!;
                                    string fieldValue = (string)fields[j + 1]!;

                                    if (fieldName == "score")
                                    {
                                        score = double.Parse(fieldValue, CultureInfo.InvariantCulture);
                                    }
                                    else if (fieldName == FIELD_RESPONSE)
                                    {
                                        response = fieldValue;
                                    }
                                }
                            }

                            // Convert distance to similarity (cosine distance: 0 = identical, 2 = opposite)
                            // Similarity = 1 - (distance / 2)
                            double similarity = 1.0 - (score / 2.0);

                            _logger.LogDebug(
                                "Found result: Key={Key}, Distance={Distance}, Similarity={Similarity}",
                                key,
                                score,
                                similarity);

                            // Filter by similarity threshold
                            if (similarity >= similarityThreshold && response != null)
                            {
                                results.Add((key, similarity, response));
                            }
                        }
                    }
                }
            }

            _logger.LogInformation(
                "Found {Count} similar vectors above threshold {Threshold}",
                results.Count,
                similarityThreshold);

            return results;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error searching similar vectors");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error searching similar vectors in Redis");
            throw;
        }
    }

    /// <summary>
    /// Stores a query, its embedding vector, and response in Redis with TTL.
    /// </summary>
    /// <param name="query">The original query text.</param>
    /// <param name="embedding">The embedding vector.</param>
    /// <param name="response">The response to cache.</param>
    /// <param name="expireSeconds">Time-to-live in seconds.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task StoreAsync(
        string query,
        float[] embedding,
        string response,
        int expireSeconds,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            throw new ArgumentException("Query cannot be null or empty.", nameof(query));
        }

        if (embedding == null || embedding.Length == 0)
        {
            throw new ArgumentException("Embedding cannot be null or empty.", nameof(embedding));
        }

        if (string.IsNullOrWhiteSpace(response))
        {
            throw new ArgumentException("Response cannot be null or empty.", nameof(response));
        }

        try
        {
            _logger.LogDebug("Storing semantic cache entry for query of length {QueryLength}", query.Length);

            // Ensure index exists before storing
            await EnsureIndexExistsAsync(cancellationToken);

            // Generate unique key with prefix
            string keyPrefix = _options.KeyPrefix ?? "resp:";
            string key = $"{keyPrefix}{Guid.NewGuid()}";

            // Convert embedding to byte array
            byte[] embeddingBytes = ConvertFloatArrayToBytes(embedding);

            // Create hash entries
            var hashEntries = new HashEntry[]
            {
                new HashEntry(FIELD_QUERY, query),
                new HashEntry(FIELD_EMBEDDING, embeddingBytes),
                new HashEntry(FIELD_RESPONSE, response),
                new HashEntry(FIELD_TIMESTAMP, DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                new HashEntry(FIELD_DIMENSIONS, embedding.Length)
            };

            // Store in Redis with TTL
            await _database.HashSetAsync(key, hashEntries);
            await _database.KeyExpireAsync(key, TimeSpan.FromSeconds(expireSeconds));

            _logger.LogInformation(
                "Stored semantic cache entry with key {Key}, TTL {ExpireSeconds}s, dimensions {Dimensions}",
                key,
                expireSeconds,
                embedding.Length);
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error storing semantic cache entry");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error storing semantic cache entry in Redis");
            throw;
        }
    }

    /// <summary>
    /// Initializes or verifies the Redis vector index using RediSearch.
    /// </summary>
    public async Task EnsureIndexExistsAsync(CancellationToken cancellationToken = default)
    {
        if (_indexCreated)
        {
            return;
        }

        try
        {
            string indexName = GetIndexName();
            _logger.LogInformation("Ensuring Redis vector index exists: {IndexName}", indexName);

            // Check if index exists using FT.INFO
            try
            {
                var infoResult = await _database.ExecuteAsync("FT.INFO", indexName);
                _logger.LogInformation("Vector index {IndexName} already exists", indexName);
                _indexCreated = true;
                return;
            }
            catch (RedisServerException ex) when (ex.Message.Contains("Unknown index name"))
            {
                _logger.LogInformation("Vector index {IndexName} does not exist, creating...", indexName);
            }

            // Create the index with vector field
            // FT.CREATE index ON HASH PREFIX 1 prefix: SCHEMA
            //   query TEXT
            //   embedding VECTOR FLAT 6 TYPE FLOAT32 DIM dimensions DISTANCE_METRIC COSINE
            //   response TEXT
            //   timestamp NUMERIC
            string keyPrefix = _options.KeyPrefix ?? "resp:";

            // Note: We'll use a default dimension (1536 for text-embedding-3-small)
            // The actual dimension should match your embedding model
            int defaultDimensions = 1536; // Adjust based on your embedding model

            var createResult = await _database.ExecuteAsync(
                "FT.CREATE",
                indexName,
                "ON", "HASH",
                "PREFIX", "1", keyPrefix,
                "SCHEMA",
                FIELD_QUERY, "TEXT",
                FIELD_EMBEDDING, "VECTOR", "FLAT", "6",
                    "TYPE", "FLOAT32",
                    "DIM", defaultDimensions.ToString(),
                    "DISTANCE_METRIC", "COSINE",
                FIELD_RESPONSE, "TEXT",
                FIELD_TIMESTAMP, "NUMERIC",
                FIELD_DIMENSIONS, "NUMERIC");

            _logger.LogInformation(
                "Created vector index {IndexName} with dimension {Dimensions}, distance metric COSINE",
                indexName,
                defaultDimensions);

            _indexCreated = true;
        }
        catch (RedisServerException ex) when (ex.Message.Contains("Index already exists"))
        {
            _logger.LogInformation("Vector index already exists (concurrent creation)");
            _indexCreated = true;
        }
        catch (RedisException ex)
        {
            _logger.LogError(ex, "Redis error ensuring vector index exists");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error ensuring Redis vector index exists");
            throw;
        }
    }

    /// <summary>
    /// Gets the index name from options or uses a default.
    /// </summary>
    private string GetIndexName()
    {
        return _options.VectorIndex ?? "dab-semantic-index";
    }

    /// <summary>
    /// Converts a float array to a byte array for Redis storage.
    /// </summary>
    private static byte[] ConvertFloatArrayToBytes(float[] floats)
    {
        byte[] bytes = new byte[floats.Length * sizeof(float)];
        Buffer.BlockCopy(floats, 0, bytes, 0, bytes.Length);
        return bytes;
    }

    /// <summary>
    /// Converts a byte array to a float array (for future use if needed).
    /// </summary>
    private static float[] ConvertBytesToFloatArray(byte[] bytes)
    {
        float[] floats = new float[bytes.Length / sizeof(float)];
        Buffer.BlockCopy(bytes, 0, floats, 0, bytes.Length);
        return floats;
    }
}


