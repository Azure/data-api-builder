// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
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

        public async Task<List<T>> ExecuteQueryAsync<T>(string query, Action<T?>? callback = null)
        {
            List<T> dataArray = new();

            FeedIterator queryIterator = _container.GetItemQueryStreamIterator(new QueryDefinition(query));
            while (queryIterator.HasMoreResults)
            {
                ResponseMessage item = await queryIterator.ReadNextAsync();

                if (item.IsSuccessStatusCode)
                {
                    using StreamReader sr = new(item.Content);
                    string content = await sr.ReadToEndAsync();

                    JsonDocument jsonDocument = JsonDocument.Parse(content);
                    JsonElement root = jsonDocument.RootElement.GetProperty("Documents");

                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (JsonElement element in root.EnumerateArray())
                        {
                            Process(callback, dataArray, element);
                        }
                    }
                    else
                    {
                        Process(callback, dataArray, root);
                    }
                }
            }

            return dataArray;
        }

        private static void Process<T>(Action<T?>? callback, List<T> dataArray, JsonElement element)
        {
            T? document = JsonSerializer.Deserialize<T>(element.GetRawText());
            if (document != null)
            {
                dataArray.Add(document);
            }

            if (callback != null)
            {
                callback(document);
            }
        }

        public async Task<string> GetPartitionKeyPath()
        {
            ContainerProperties containerProperties = await _container.ReadContainerAsync();

            return containerProperties.PartitionKeyPath;
        }
    }
}
