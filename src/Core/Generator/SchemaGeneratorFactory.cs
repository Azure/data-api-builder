// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Generator.Sampler;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;

namespace Azure.DataApiBuilder.Core.Generator
{
    public static class SchemaGeneratorFactory
    {
        /// <summary>
        /// Factory which takes all the configuration, Connect to the cosmosDB account and get the sample data and then generate GQL schema using that.
        /// </summary>
        /// <param name="config"></param>
        /// <param name="mode"></param>
        /// <param name="sampleCount"></param>
        /// <param name="partitionKeyPath"></param>
        /// <param name="days"></param>
        /// <param name="groupCount"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static async Task<string> Create(RuntimeConfig config, string mode, int? sampleCount, string? partitionKeyPath, int? days, int? groupCount, ILogger logger)
        {
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
                logger.LogError($"Config file passed is not compatible with this feature. Please make sure datasource type is configured as  {DatabaseType.CosmosDB_NoSQL}");
                throw new ArgumentException($"Config file passed is not compatible with this feature. Please make sure datasource type is configured as {DatabaseType.CosmosDB_NoSQL}");
            }

            string? connectionString = config.DataSource?.ConnectionString;
            string? databaseName = config.DataSource?.Options?["database"]?.ToString();
            string? containerName = config.DataSource?.Options?["container"]?.ToString();

            if (connectionString == null || databaseName == null || containerName == null)
            {
                logger.LogError("Connection String, Database and container name must be provided in the config file");
                throw new ArgumentException("Connection String, Database and container name must be provided in the config file");
            }

            logger.LogInformation($"Connecting to Cosmos DB Database: {databaseName}, Container: {containerName}");
            Container container = ConnectToCosmosDB(connectionString, databaseName, containerName);
            SamplingModes samplingMode = (SamplingModes)Enum.Parse(typeof(SamplingModes), mode);

            ISchemaGeneratorSampler schemaGeneratorSampler = samplingMode switch
            {
                SamplingModes.TopNSampler => new TopNSampler(container, sampleCount, days, logger),
                SamplingModes.PartitionBasedSampler => new PartitionBasedSampler(container, partitionKeyPath, sampleCount, days, logger),
                SamplingModes.TimeBasedSampler => new TimeBasedSampler(container, groupCount, sampleCount, days, logger),
                _ => throw new ArgumentException($"Invalid sampling mode: {mode}, Valid Sampling Modes are: {SamplingModes.TopNSampler}, {SamplingModes.PartitionBasedSampler}, {SamplingModes.TimeBasedSampler}")
            };

            logger.LogInformation($"Sampling Started using {schemaGeneratorSampler.GetType().Name}");

            // Get Sample Data
            List<JsonDocument> dataArray = await schemaGeneratorSampler.GetSampleAsync();

            logger.LogInformation($"{dataArray.Count} records collected as Sample");
            if (dataArray.Count == 0)
            {
                logger.LogError("No data got sampled out. Please try different sampling Mode or Sampling configuration");
                throw new ArgumentException("No data got sampled out. Please try different sampling Mode or Sampling configuration");
            }

            logger.LogInformation($"Generating Schema Started");
            // Generate GQL Schema
            return SchemaGenerator.Generate(dataArray, container.Id, config);
        }

        private static Container ConnectToCosmosDB(string connectionString, string database, string container)
        {
            CosmosClient cosmosClient = new(connectionString);
            return cosmosClient.GetContainer(database, container);
        }
    }
}
