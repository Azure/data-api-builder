// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    /// <summary>
    /// This class is responsible for interacting with CosmosDB.
    /// </summary>
    internal class CosmosExecutor
    {
        private Container _container;
        private ILogger _logger;

        public CosmosExecutor(Container container, ILogger logger)
        {
            this._container = container;
            this._logger = logger;
        }

        /// <summary>
        /// This function execute the passed query and returns the result in the given type.
        /// </summary>
        /// <typeparam name="T"></typeparam>
        /// <param name="query">Cosmos DB query</param>
        /// <param name="callback"> This callback can be used to manipulate or fetch only required information from the returned item.</param>
        /// <returns></returns>
        public async Task<List<T>> ExecuteQueryAsync<T>(string query, Action<T?>? callback = null)
        {
            _logger.LogDebug($"Executing Query: {query}");

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
                else
                {
                    throw new Exception($"Failed to execute query: {query} with status code {item.StatusCode}, Error Message : {item.ErrorMessage}");
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

        /// <summary>
        /// Returns the partition key path of the container.
        /// </summary>
        /// <returns></returns>
        public async Task<string> GetPartitionKeyPath()
        {
            ContainerProperties containerProperties = await _container.ReadContainerAsync();

            _logger.LogDebug($"Partition Key Path: {containerProperties.PartitionKeyPath}");

            return containerProperties.PartitionKeyPath;
        }
    }
}
