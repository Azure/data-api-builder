// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Generator.Sampler;
using Microsoft.Azure.Cosmos;

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
        public static async Task<string> Create(RuntimeConfig config, string mode, int? sampleCount, string? partitionKeyPath, int? days, int? groupCount)
        {
            if (config.DataSource == null)
            {
                throw new ArgumentException("Data Source not found");
            }

            if (config.DataSource.DatabaseType != DatabaseType.CosmosDB_NoSQL)
            {
                throw new ArgumentException("Config file passed is not compatible with this feature. Please make sure datasource type is configured as 'Cosmos DB'");
            }

            string? connectionString = config.DataSource?.ConnectionString;
            string? databaseName = config.DataSource?.Options?["database"]?.ToString();
            string? containerName = config.DataSource?.Options?["container"]?.ToString();

            if (connectionString == null || databaseName == null || containerName == null)
            {
                throw new ArgumentException("Connection String, Database and container name must be provided in the config file");
            }

            Container container = ConnectToCosmosDB(connectionString, databaseName, containerName);
            SamplingMode samplingMode = (SamplingMode)Enum.Parse(typeof(SamplingMode), mode);

            ISchemaGeneratorSampler schemaGeneratorSampler = samplingMode switch
            {
                SamplingMode.TopNSampler => new TopNSampler(container, sampleCount, days),
                SamplingMode.PartitionBasedSampler => new PartitionBasedSampler(container, partitionKeyPath, sampleCount, days),
                SamplingMode.TimeBasedSampler => new TimeBasedSampler(container, groupCount, sampleCount, days),
                _ => throw new ArgumentException($"Invalid sampling mode: {mode}")
            };

            // Get Sample Data
            List<JsonDocument> dataArray = await schemaGeneratorSampler.GetSampleAsync();

            if (dataArray.Count == 0)
            {
                throw new ArgumentException("No data got sampled out. Please try different sampling Mode or Sampling configuration");
            }
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
