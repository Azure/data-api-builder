// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    internal class TopNSampler : ISchemaGeneratorSampler
    {
        private int _numberOfRecords;

        public TopNSampler(int numberOfRecords)
        {
            this._numberOfRecords = numberOfRecords;
        }

        public async Task<JArray> GetSampleAsync(Container container)
        {
            JArray dataArray = new();
            string query = $"SELECT TOP {_numberOfRecords} * FROM c";

            var queryIterator = container.GetItemQueryIterator<JObject>(new QueryDefinition(query));
            while (queryIterator.HasMoreResults)
            {
                foreach (var item in await queryIterator.ReadNextAsync())
                {
                    dataArray.Add(item);
                }
            }

            return dataArray;
        }
    }
}
