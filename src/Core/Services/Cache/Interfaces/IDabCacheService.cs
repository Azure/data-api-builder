// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;

namespace Azure.DataApiBuilder.Core.Services.Cache.Interfaces;

public interface IDabCacheService
{
    public ValueTask<TValue?> GetOrSetAsync<TValue>(IQueryExecutor queryExecutor, DatabaseQueryMetadata queryMetadata);
}
