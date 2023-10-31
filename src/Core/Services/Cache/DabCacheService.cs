// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services.Cache.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion;

namespace Azure.DataApiBuilder.Core.Services.Cache;

public class DabCacheService : IDabCacheService
{
    private IDabCacheKeyProvider _cacheKeyProvider;
    private IFusionCache _cache;
    private ILogger _logger;
    private IHttpContextAccessor _httpContextAccessor;

    public DabCacheService(IDabCacheKeyProvider cacheKeyProvider, IFusionCache cache, ILogger<DabCacheService> logger, IHttpContextAccessor httpContextAccessor)
    {
        _cacheKeyProvider = cacheKeyProvider;
        _cache = cache;
        _logger = logger;
        _httpContextAccessor = httpContextAccessor;
    }

    public async ValueTask<JsonElement?> GetOrSetAsync<JsonElement>(IQueryExecutor queryExecutor, DatabaseQueryMetadata queryMetadata)
    {
        string cacheKey = _cacheKeyProvider.CreateKey(queryMetadata);
        JsonElement? result = await _cache.GetOrSetAsync(
               key: cacheKey,
               async (FusionCacheFactoryExecutionContext<JsonElement> ctx, CancellationToken ct ) =>
               {
                   // Need to handle undesirable results like db errors or null.
                   JsonElement? result = await queryExecutor.ExecuteQueryAsync(
                       sqltext: queryMetadata.QueryText,
                       parameters: queryMetadata.QueryParameters,
                       dataReaderHandler: queryExecutor.GetJsonResultAsync<JsonElement>,
                       httpContext: _httpContextAccessor.HttpContext!,
                       args: null,
                       dataSourceName: queryMetadata.DataSource);

                   ctx.Options.SetSize(CacheEntryHelpers.EstimateCachedResponseSize(cacheKey: cacheKey, cacheValue: result?.ToString()));

                   return result;
               });

        return result;
    }
}
