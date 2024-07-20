// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Azure.Cosmos;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    internal class CosmosExecutor
    {
        private Container _container;

        public CosmosExecutor(Container container)
        {
            this._container = container;
        }

        public async Task<List<T>> ExecuteQueryAsync<T>(string query, Action<T>? callback = null)
        {
            List<T> dataArray = new();

            FeedIterator<T> queryIterator = _container.GetItemQueryIterator<T>(new QueryDefinition(query));
            while (queryIterator.HasMoreResults)
            {
                foreach (T item in await queryIterator.ReadNextAsync())
                {
                    dataArray.Add(item);

                    if(callback != null)
                    {
                        callback(item);
                    }
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
