// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Azure.DataApiBuilder.Core.Services.Cache;

public static class CacheEntryHelpers
{
    internal static long EstimateCachedResponseSize(string cacheKey, string? cacheValue)
    {
        if (string.IsNullOrWhiteSpace(cacheKey) || string.IsNullOrWhiteSpace(cacheValue))
        {
            return 0L;
        }

        checked
        {
            long size = 0L;
            size += cacheKey.Length * sizeof(char);
            size += cacheValue.Length * sizeof(char);
            return size;
        }
    }
}
