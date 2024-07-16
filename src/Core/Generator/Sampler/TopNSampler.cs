// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    internal class TopNSampler : ISchemaGeneratorSampler
    {
        private int _numberOfRecords;
        private QueryExecutor _queryExecutor;

        public TopNSampler(Container container, int? numberOfRecords)
        {
            this._numberOfRecords = numberOfRecords ?? 10;

            this._queryExecutor = new QueryExecutor(container);
        }

        public async Task<List<JObject>> GetSampleAsync()
        {
            string query = $"SELECT TOP {_numberOfRecords} * FROM c ORDER BY c._ts desc";

            return await _queryExecutor.ExecuteQueryAsync<JObject>(query);
        }
    }
}
