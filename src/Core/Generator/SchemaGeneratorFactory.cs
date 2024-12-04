// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Generator.Sampler;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Generator
{
    /// <summary>
    /// The <c>SchemaGeneratorFactory</c> class provides functionality to connect to an Azure Cosmos DB account, sample data from a specified container, 
    /// and generate a GraphQL (GQL) schema based on the sampled data. It uses various sampling strategies to collect representative data 
    /// and create a schema that reflects the structure of that data.
    /// </summary>
    /// <remarks>
    /// This class is designed to simplify the process of generating GQL schemas for data stored in Azure Cosmos DB. It abstracts away the details
    /// of connecting to Azure Cosmos DB, sampling data using different strategies, and converting that data into a GQL schema. It is particularly 
    /// useful in scenarios where the schema needs to be dynamically created based on actual data rather than being predefined.
    /// </remarks>
    public static class SchemaGeneratorFactory
    {
        /// <summary>
        /// Creates a GraphQL schema by connecting to Azure Cosmos DB, sampling data based on the provided configuration, 
        /// and generating the schema from the sampled data.
        /// </summary>
        /// <param name="config">The runtime configuration containing details about the data source and connection information for Azure Cosmos DB.</param>
        /// <param name="mode">The sampling mode to use when collecting sample data. This should be one of the defined sampling modes (e.g., TopNExtractor, EligibleDataSampler, TimePartitionedSampler).</param>
        /// <param name="sampleCount">The number of samples to collect. This must be greater than zero if specified.</param>
        /// <param name="partitionKeyPath">The path of the partition key for partition-based sampling. This parameter is optional and used only for partition-based sampling.</param>
        /// <param name="days">The number of days to use for time-based sampling. This parameter is optional and should be greater than zero if specified.</param>
        /// <param name="groupCount">The number of groups to use for time-based sampling. This parameter is optional and should be greater than zero if specified.</param>
        /// <param name="logger">An instance of <see cref="ILogger"/> used to log information and errors throughout the process.</param>
        /// <returns>A <see cref="Task{TResult}"/> representing the asynchronous operation. The result is a string containing the generated GraphQL schema.</returns>
        /// <exception cref="ArgumentException">Thrown when the configuration parameters are invalid or incomplete, or if the data source is not properly configured.</exception>
        public static async Task<string> Create(RuntimeConfig config, string mode, int? sampleCount, string? partitionKeyPath, int? days, int? groupCount, ILogger logger)
        {
            // Validate the configuration parameters.
            if ((days.HasValue && days < 1) || (groupCount.HasValue && groupCount < 1) || (sampleCount.HasValue && sampleCount < 1))
            {
                logger.LogError("Invalid Configuration Found");
                throw new ArgumentException("Invalid Configuration Found");
            }

            if (config.DataSource == null)
            {
                logger.LogError("Runtime Config file doesn't have Data Source configured");
                throw new ArgumentException("DataSource not found");
            }

            if (config.DataSource.DatabaseType != DatabaseType.CosmosDB_NoSQL)
            {
                logger.LogError("Config file passed is not compatible with this feature. Please make sure datasource type is configured as  {0}", DatabaseType.CosmosDB_NoSQL);
                throw new ArgumentException($"Config file passed is not compatible with this feature. Please make sure datasource type is configured as {DatabaseType.CosmosDB_NoSQL}");
            }

            HashSet<string> dbAndContainerToProcess = new();
            string? connectionString = config.DataSource?.ConnectionString;
            string? globalDatabaseName = config.DataSource?.Options?["database"]?.ToString();
            string? globalContainerName = config.DataSource?.Options?["container"]?.ToString();

            if (!string.IsNullOrEmpty(globalDatabaseName) && !string.IsNullOrEmpty(globalContainerName))
            {
                dbAndContainerToProcess.Add($"{globalDatabaseName}.{globalContainerName}");
            }

            foreach (KeyValuePair<string, Entity> entity in config.Entities)
            {
                dbAndContainerToProcess.Add(entity.Value.Source.Object);
            }

            if (connectionString == null)
            {
                logger.LogError("Connection String name must be provided in the config file");
                throw new ArgumentException("Connection String must be provided in the config file");
            }

            List<JsonDocument> dataArray = new();
            StringBuilder schema = new();
            foreach (string dbAndContainer in dbAndContainerToProcess)
            {
                if (string.IsNullOrEmpty(dbAndContainer))
                {
                    continue;
                }

                string[] dbContainer = dbAndContainer.Split('.');
                if (dbContainer.Length != 2)
                {
                    logger.LogError("Invalid Database and Container Name provided in the config file");
                    throw new ArgumentException("Invalid Database and Container Name provided in the config file");
                }

                logger.LogInformation("Connecting to Cosmos DB Database: {0}, Container: {1}", dbContainer[0], dbContainer[1]);

                // Connect to the Azure Cosmos DB container.
                Container container = ConnectToCosmosDB(connectionString, dbContainer[0], dbContainer[1]);
                SamplingModes samplingMode = (SamplingModes)Enum.Parse(typeof(SamplingModes), mode);

                // Determine the appropriate sampler based on the sampling mode.
                ISchemaGeneratorSampler schemaGeneratorSampler = samplingMode switch
                {
                    SamplingModes.TopNExtractor => new TopNExtractor(container, sampleCount, days, logger),
                    SamplingModes.EligibleDataSampler => new EligibleDataSampler(container, partitionKeyPath, sampleCount, days, logger),
                    SamplingModes.TimePartitionedSampler => new TimePartitionedSampler(container, groupCount, sampleCount, days, logger),
                    _ => throw new ArgumentException($"Invalid sampling mode: {mode}, Valid Sampling Modes are: {SamplingModes.TopNExtractor}, {SamplingModes.EligibleDataSampler}, {SamplingModes.TimePartitionedSampler}")
                };

                logger.LogInformation("Sampling Started using {0}", schemaGeneratorSampler.GetType().Name);

                // Get sample data from the selected sampler.
                dataArray.AddRange(await schemaGeneratorSampler.GetSampleAsync());

                logger.LogInformation("{0} records collected as Sample", dataArray.Count);
                if (dataArray.Count == 0)
                {
                    logger.LogError("No data was sampled. Please try a different sampling mode or provide different sampling options.");
                    throw new ArgumentException("No data was sampled. Please try a different sampling mode or provide different sampling options.");
                }

                logger.LogInformation("GraphQL schema generation started.");

                string generatedSchema = SchemaGenerator.Generate(dataArray, container.Id, config);
                schema.AppendLine(generatedSchema);
            }

            // Generate and return the GraphQL schema based on the sampled data.
            return schema.ToString();
        }

        /// <summary>
        /// Establishes a connection to an Azure Cosmos DB container using the provided connection string, database name, and container name.
        /// </summary>
        /// <param name="connectionString">The connection string for the Azure Cosmos DB account.</param>
        /// <param name="database">The name of the database to connect to.</param>
        /// <param name="container">The name of the container to connect to.</param>
        /// <returns>A <see cref="Container"/> object representing the connected Azure Cosmos DB container.</returns>
        private static Container ConnectToCosmosDB(string connectionString, string database, string container)
        {
            CosmosClient cosmosClient = new(connectionString);
            return cosmosClient.GetContainer(database, container);
        }
    }
}
