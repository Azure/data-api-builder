// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Services.Cache.Interfaces;

namespace Azure.DataApiBuilder.Core.Services.Cache;

public class DabCacheKeyProvider : IDabCacheKeyProvider
{
    private const char KEY_DELIMITER = ':';

    public DabCacheKeyProvider() { }

    public string CreateKey(DatabaseQueryMetadata queryMetadata)
    {
        StringBuilder cacheKeyBuilder = new();
        cacheKeyBuilder.Append(queryMetadata.DataSource);
        cacheKeyBuilder.Append(KEY_DELIMITER);
        cacheKeyBuilder.Append(queryMetadata.QueryText);
        cacheKeyBuilder.Append(KEY_DELIMITER);
        cacheKeyBuilder.Append(JsonSerializer.Serialize(queryMetadata.QueryParameters));

        return cacheKeyBuilder.ToString();
    }
}
