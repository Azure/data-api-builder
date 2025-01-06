// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Generator.Sampler
{
    /// <summary>
    /// The CosmosExecutor class provides methods for interacting with a Cosmos DB container,
    /// including executing queries and retrieving metadata such as partition key paths.
    /// It acts as a utility for data retrieval operations, handling query execution and result processing.
    /// </summary>
    internal class CosmosExecutor
    {
        private Container _container;
        private ILogger _logger;

        /// <summary>
        /// Initializes a new instance of the <see cref="CosmosExecutor"/> class.
        /// </summary>
        /// <param name="container">The Azure Cosmos DB container instance to interact with.</param>
        /// <param name="logger">The logger instance used for logging information and debugging messages.</param>
        public CosmosExecutor(Container container, ILogger logger)
        {
            this._container = container;
            this._logger = logger;
        }

        /// <summary>
        /// Executes the specified query on the Azure Cosmos DB container and returns the results as a list of the specified type.
        /// </summary>
        /// <typeparam name="T">The type to which the query results should be deserialized.</typeparam>
        /// <param name="query">The SQL-like query string to execute against the Azure Cosmos DB container.</param>
        /// <param name="callback">Optional. A callback function that can be used to manipulate or process each retrieved item.</param>
        /// <returns>A task representing the asynchronous operation, containing a list of results of type <typeparamref name="T"/>.</returns>
        /// <exception cref="Exception">Thrown when the query execution fails with an error message and status code.</exception>
        public async Task<List<T>> ExecuteQueryAsync<T>(string query, Action<T?>? callback = null)
        {
            _logger.LogDebug("Executing Query: {0}", query);

            List<T> dataArray = new();

            FeedIterator queryIterator = _container.GetItemQueryStreamIterator(new QueryDefinition(query));
            while (queryIterator.HasMoreResults)
            {
                ResponseMessage item = await queryIterator.ReadNextAsync();

                if (item.IsSuccessStatusCode)
                {
                    using StreamReader sr = new(item.Content);
                    string content = await sr.ReadToEndAsync();

                    using JsonDocument jsonDocument = JsonDocument.Parse(content);

                    JsonElement root = jsonDocument.RootElement;
                    if (root.ValueKind != JsonValueKind.Array && root.TryGetProperty("Documents", out JsonElement documentRootProperty))
                    {
                        root = documentRootProperty;
                    }

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
                    throw new Exception(string.Format("Failed to execute query: {0} with status code {1}, Error Message : {2}", query, item.StatusCode, item.ErrorMessage));
                }
            }

            return dataArray;
        }

        /// <summary>
        /// Deserializes a JSON element to the specified type and adds it to the data array.
        /// Optionally, invokes a callback function on each deserialized item.
        /// </summary>
        /// <typeparam name="T">The type to which the JSON element should be deserialized.</typeparam>
        /// <param name="callback">Optional. A callback function to process each deserialized item.</param>
        /// <param name="dataArray">The list to which the deserialized item is added.</param>
        /// <param name="element">The JSON element representing a data item.</param>
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
        /// Retrieves the partition key path of the Azure Cosmos DB container.
        /// </summary>
        /// <returns>A task representing the asynchronous operation, containing the partition key path as a string.</returns>
        public async Task<string> GetPartitionKeyPath()
        {
            ContainerProperties containerProperties = await _container.ReadContainerAsync();

            _logger.LogDebug("Partition Key Path: {0}", containerProperties.PartitionKeyPath);

            return containerProperties.PartitionKeyPath;
        }
    }
}
