// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;
using System.Text.Json;

namespace Azure.DataApiBuilder.Core.Models
{
    internal class DatabaseQueryMetadata
    {
        private const char KEY_DELIMITER = ':';
        private readonly string _queryText;
        private readonly string _dataSource;
        private readonly Dictionary<string, DbConnectionParam> _queryParameters;

        public DatabaseQueryMetadata(string queryText, string dataSource, Dictionary<string, DbConnectionParam> queryParameters)
        {
            _queryText = queryText;
            _dataSource = dataSource;
            _queryParameters = queryParameters;
        }

        public string CreateCacheKey()
        {
            StringBuilder cacheKeyBuilder = new();
            cacheKeyBuilder.Append(_dataSource);
            cacheKeyBuilder.Append(KEY_DELIMITER);
            cacheKeyBuilder.Append(_queryText);
            cacheKeyBuilder.Append(KEY_DELIMITER);
            cacheKeyBuilder.Append(JsonSerializer.Serialize(_queryParameters));

            return cacheKeyBuilder.ToString();
        }
    }
}
