// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace Azure.DataApiBuilder.Core.Services.Cache;

/// <summary>
/// Service which wraps an internal cache implementation that enables
/// provided an in-memory cache for the DAB engine.
/// </summary>
public class DabCacheService
{
    // Dependencies
    private readonly IFusionCache _cache;
    private readonly ILogger? _logger;
    private readonly IHttpContextAccessor _httpContextAccessor;

    // Constants
    private const char KEY_DELIMITER = ':';

    // Log Messages
    private const string CACHE_KEY_EMPTY = "The cache key should not be empty.";
    private const string CACHE_KEY_CREATED = "The cache key was created by the cache service.";
    private const string CACHE_ENTRY_TOO_LARGE = "The cache entry is too large.";

    /// <summary>
    /// Create cache service which encapsulates actual caching implementation.
    /// </summary>
    /// <param name="cache">Cache implementation.</param>
    /// <param name="logger">Logger.</param>
    /// <param name="httpContextAccessor">Accessor which provides httpContext within factory method.</param>
    public DabCacheService(IFusionCache cache, ILogger<DabCacheService>? logger, IHttpContextAccessor httpContextAccessor)
    {
        _cache = cache;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    /// <summary>
    /// Attempts to fetch response from cache. If there is a cache miss, call the 'factory method' to get a response
    /// from the backing database.
    /// </summary>
    /// <typeparam name="TResult">Response payload</typeparam>
    /// <param name="queryExecutor">Factory method. Only executed after a cache miss.</param>
    /// <param name="queryMetadata">Metadata used to create a cache key or fetch a response from the database.</param>
    /// <param name="cacheEntryTtl">Number of seconds the cache entry should be valid before eviction.</param>
    /// <returns>JSON Response</returns>
    /// <exception cref="Exception">Throws when the cache-miss factory method execution fails.</exception>
    public async ValueTask<TResult?> GetOrSetAsync<TResult>(
        IQueryExecutor queryExecutor,
        DatabaseQueryMetadata queryMetadata,
        int cacheEntryTtl,
        EntityCacheLevel cacheEntryLevel)
    {
        string cacheKey = CreateCacheKey(queryMetadata);
        TResult? result = await _cache.GetOrSetAsync(
               key: cacheKey,
               async (FusionCacheFactoryExecutionContext<TResult?> ctx, CancellationToken ct) =>
               {
                   // Need to handle undesirable results like db errors or null.
                   TResult? result = await queryExecutor.ExecuteQueryAsync(
                       sqltext: queryMetadata.QueryText,
                       parameters: queryMetadata.QueryParameters,
                       dataReaderHandler: queryExecutor.GetJsonResultAsync<TResult>,
                       httpContext: _httpContextAccessor.HttpContext!,
                       args: null,
                       dataSourceName: queryMetadata.DataSource);

                   // TODO: check if still needed, probably not (since no SizeLimit has been set on the underlying MemoryCache)
                   ctx.Options.SetSize(EstimateCacheEntrySize(cacheKey: cacheKey, cacheValue: result?.ToString()));

                   ctx.Options.SetDuration(duration: TimeSpan.FromSeconds(cacheEntryTtl));

                   if (cacheEntryLevel == EntityCacheLevel.L1)
                   {
                       ctx.Options.SetSkipDistributedCache(true, true);
                   }

                   return result;
               });

        return result;
    }

    /// <summary>
    /// Try to get cacheValue from the cache with the derived cache key.
    /// </summary>
    /// <typeparam name="T">The type of value in the cache</typeparam>
    /// <param name="queryMetadata">Metadata used to create a cache key or fetch a response from the database.</param>
    /// <returns>JSON Response</returns>
    public MaybeValue<T>? TryGet<T>(DatabaseQueryMetadata queryMetadata, EntityCacheLevel cacheEntryLevel)
    {
        string cacheKey = CreateCacheKey(queryMetadata);
        FusionCacheEntryOptions options = new();

        if (cacheEntryLevel == EntityCacheLevel.L1)
        {
            options.SetSkipDistributedCache(true, true);
        }

        return _cache.TryGet<T>(key: cacheKey);
    }

    /// <summary>
    /// Store cacheValue into the cache with the derived cache key.
    /// </summary>
    /// <typeparam name="JsonElement">The type of value in the cache</typeparam>
    /// <param name="queryMetadata">Metadata used to create a cache key or fetch a response from the database.</param>
    /// <param name="cacheEntryTtl">Number of seconds the cache entry should be valid before eviction.</param>
    /// <param name="cacheValue"">The value to store in the cache.</param>
    public void Set<JsonElement>(
        DatabaseQueryMetadata queryMetadata,
        int cacheEntryTtl,
        JsonElement? cacheValue,
        EntityCacheLevel cacheEntryLevel)
    {
        string cacheKey = CreateCacheKey(queryMetadata);
        _cache.Set(
            key: cacheKey,
            value: cacheValue,
            (FusionCacheEntryOptions options) =>
            {
                options.SetSize(EstimateCacheEntrySize(cacheKey: cacheKey, cacheValue: cacheValue?.ToString()));
                options.SetDuration(duration: TimeSpan.FromSeconds(cacheEntryTtl));

                if (cacheEntryLevel == EntityCacheLevel.L1)
                {
                    options.SetSkipDistributedCache(true, true);
                }

            });
    }

    /// <summary>
    /// Attempts to fetch response from cache. If there is a cache miss, invoke executeQueryAsync Func to get a response
    /// </summary>
    /// <typeparam name="TResult">Response payload Type</typeparam>
    /// <param name="executeQueryAsync">Func with a result of type TResult. Only executed after a cache miss.</param>
    /// <param name="queryMetadata">Metadata used to create a cache key or fetch a response from the database.</param>
    /// <param name="cacheEntryTtl">Number of seconds the cache entry should be valid before eviction.</param>
    /// <returns>JSON Response</returns>
    /// <exception cref="Exception">Throws when the cache-miss factory method execution fails.</exception>
    public async ValueTask<TResult?> GetOrSetAsync<TResult>(
        Func<Task<TResult>> executeQueryAsync,
        DatabaseQueryMetadata queryMetadata,
        int cacheEntryTtl,
        EntityCacheLevel cacheEntryLevel)
    {
        string cacheKey = CreateCacheKey(queryMetadata);
        TResult? result = await _cache.GetOrSetAsync(
               key: cacheKey,
               async (FusionCacheFactoryExecutionContext<TResult> ctx, CancellationToken ct) =>
               {
                   TResult result = await executeQueryAsync();

                   // TODO: check if still needed, probably not (since no SizeLimit has been set on the underlying MemoryCache)
                   ctx.Options.SetSize(EstimateCacheEntrySize(cacheKey: cacheKey, cacheValue: JsonSerializer.Serialize(result?.ToString())));

                   ctx.Options.SetDuration(duration: TimeSpan.FromSeconds(cacheEntryTtl));

                   if (cacheEntryLevel == EntityCacheLevel.L1)
                   {
                       ctx.Options.SetSkipDistributedCache(true, true);
                   }

                   return result;

               });

        return result;
    }

    /// <summary>
    /// Creates a cache key using the request metadata resolved from a built SqlQueryStructure
    /// Format: DataSourceName:QueryText:JSON_QueryParameters
    /// Example: 7a07f92a-1aa2-4e2a-81d6-b9af0a25bbb6:select * from MyTable where id = @param1 = :{"@param1":{"Value":"42","DbType":null}}
    /// Format CosmosDB: DataSourceName:ContinuationToken:QueryText:JSON_QueryParameters
    /// IsPaginated and ContinuationToken are optional and will be present only for paginated queries
    /// Example without Pagination : 11bbfedb-df1d-47ad-ac2c-8dfeecc5f1a1:SELECT c.id, c.name FROM c:{}
    /// Example with Pagination: 11bbfedb-df1d-47ad-ac2c-8dfeecc5f1a1:W3sidG9rZW4iOiItUklEOn56dG9NQUxQaGhpWUZBQUFBQUFBQUFBPT0jUlQ6MSNUUkM6NSNJU1Y6MiNJRU86NjU1NjcjUUNGOjgiLCJyYW5nZSI6eyJtaW4iOiIiLCJtYXgiOiJGRiJ9fV0=:SELECT c.id, c.name FROM c:{}
    /// </summary>
    /// <returns>Cache key string</returns>
    private string CreateCacheKey(DatabaseQueryMetadata queryMetadata)
    {
        // TODO: to avoid cache keys being too large, we should consider the use of hashing.
        // We can hash the query parameters, and maybe even the query text.
        // I would exclude the datasource, for easier investigations.
        // The hash algorithm should be deterministic and fast, not cryptographically secure.
        StringBuilder cacheKeyBuilder = new();
        cacheKeyBuilder.Append(queryMetadata.DataSource);
        cacheKeyBuilder.Append(KEY_DELIMITER);
        cacheKeyBuilder.Append(queryMetadata.QueryText);
        cacheKeyBuilder.Append(KEY_DELIMITER);
        cacheKeyBuilder.Append(JsonSerializer.Serialize(queryMetadata.QueryParameters));

        if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
        {
            _logger.LogTrace(message: CACHE_KEY_CREATED);
        }

        return cacheKeyBuilder.ToString();
    }

    /// <summary>
    /// Estimates the size of the cache entry in bytes.
    /// The cache entry is the concatenation of the cache key and cache value.
    /// </summary>
    /// <param name="cacheKey">Cache key string.</param>
    /// <param name="cacheValue">Cache value as a serialized JSON payload.</param>
    /// <returns>Size in bytes.</returns>
    /// <seealso cref="https://learn.microsoft.com/dotnet/csharp/language-reference/statements/checked-and-unchecked"/>
    /// <exception cref="ArgumentException">Thrown when the cacheKey value is empty or whitespace.</exception>
    /// <exception cref="OverflowException">Thrown when the cache entry size is too big.</exception>
    private long EstimateCacheEntrySize(string cacheKey, string? cacheValue)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            throw new ArgumentException(message: CACHE_KEY_EMPTY);
        }

        try
        {
            checked
            {
                long size = 0L;
                long cacheValueSize = string.IsNullOrWhiteSpace(cacheValue) ? 0L : cacheValue.Length;
                size += cacheKey.Length * sizeof(char);
                size += cacheValueSize * sizeof(char);
                return size;
            }
        }
        catch (OverflowException)
        {
            if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
            {
                _logger.LogTrace(message: CACHE_ENTRY_TOO_LARGE);
            }

            throw;
        }
    }
}
