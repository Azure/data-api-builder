// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Cosmos;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    internal class QueryExecutor
    {
        private Container _container;

        public QueryExecutor(Container container)
        {
            this._container = container;
        }

        public async Task<List<T>> ExecuteQueryAsync<T>(string query)
        {
            List<T> dataArray = new();

            FeedIterator<T> queryIterator = _container.GetItemQueryIterator<T>(new QueryDefinition(query));
            while (queryIterator.HasMoreResults)
            {
                foreach (T item in await queryIterator.ReadNextAsync())
                {
                    dataArray.Add(item);
                }
            }

            return dataArray;
        }

        public async Task<string> GetPartitionKeyPath()
        {
            ContainerProperties containerProperties = await _container.ReadContainerAsync();

            return containerProperties.PartitionKeyPath;
        }
    }
}
