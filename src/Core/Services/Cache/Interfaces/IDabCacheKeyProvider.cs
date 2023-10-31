// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Core.Models;

namespace Azure.DataApiBuilder.Core.Services.Cache.Interfaces;

public interface IDabCacheKeyProvider
{
    public string CreateKey(DatabaseQueryMetadata queryMetadata);
}
