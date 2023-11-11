// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Core.Models;

namespace Azure.DataApiBuilder.Core.Services.Cache;

public class DabCacheKeyProvider
{
    private const char KEY_DELIMITER = ':';

    public static string CreateKey(DatabaseQueryMetadata queryMetadata)
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
