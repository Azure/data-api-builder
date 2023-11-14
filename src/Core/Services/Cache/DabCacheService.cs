// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using System.Text.Json;
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
    /// <typeparam name="JsonElement">Response payload</typeparam>
    /// <param name="queryExecutor">Factory method. Only executed after a cache miss.</param>
    /// <param name="queryMetadata">Metadata used to create a cache key or fetch a response from the database.</param>
    /// <param name="cacheEntryTtl">Number of seconds the cache entry should be valid before eviction.</param>
    /// <returns>JSON Response</returns>
    /// <exception cref="Exception">Throws when the cache-miss factory method execution fails.</exception>
    public async ValueTask<JsonElement?> GetOrSetAsync<JsonElement>(IQueryExecutor queryExecutor, DatabaseQueryMetadata queryMetadata, int cacheEntryTtl)
    {
        string cacheKey = CreateCacheKey(queryMetadata);
        JsonElement? result = await _cache.GetOrSetAsync(
               key: cacheKey,
               async (FusionCacheFactoryExecutionContext<JsonElement> ctx, CancellationToken ct) =>
               {
                   // Need to handle undesirable results like db errors or null.
                   JsonElement? result = await queryExecutor.ExecuteQueryAsync(
                       sqltext: queryMetadata.QueryText,
                       parameters: queryMetadata.QueryParameters,
                       dataReaderHandler: queryExecutor.GetJsonResultAsync<JsonElement>,
                       httpContext: _httpContextAccessor.HttpContext!,
                       args: null,
                       dataSourceName: queryMetadata.DataSource);

                   ctx.Options.SetSize(EstimateCacheEntrySize(cacheKey: cacheKey, cacheValue: result?.ToString()));
                   ctx.Options.SetDuration(duration: TimeSpan.FromSeconds(cacheEntryTtl));

                   return result;
               });

        return result;
    }

    /// <summary>
    /// Creates a cache key using the request metadata resolved from a built SqlQueryStructure
    /// </summary>
    /// <returns>Cache key string</returns>
    private string CreateCacheKey(DatabaseQueryMetadata queryMetadata)
    {
        StringBuilder cacheKeyBuilder = new();
        cacheKeyBuilder.Append(queryMetadata.DataSource);
        cacheKeyBuilder.Append(KEY_DELIMITER);
        cacheKeyBuilder.Append(queryMetadata.QueryText);
        cacheKeyBuilder.Append(KEY_DELIMITER);
        cacheKeyBuilder.Append(JsonSerializer.Serialize(queryMetadata.QueryParameters));
        string cacheKey = cacheKeyBuilder.ToString();

        if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
        {
            _logger.LogTrace(message: "{cacheKey}", cacheKey);
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
    /// <exception cref="ArgumentException">Thrown when the cacheKey value is empty or whitespace.</exception>
    /// <exception cref="OverflowException">Thrown when the cache entry size is too big.</exception>
    private long EstimateCacheEntrySize(string cacheKey, string? cacheValue)
    {
        if (string.IsNullOrWhiteSpace(cacheKey))
        {
            throw new ArgumentException(message: "Cache key should not be empty.");
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
                _logger.LogTrace(message: "Cache entry is too big.");
            }

            throw;
        }
    }
}
