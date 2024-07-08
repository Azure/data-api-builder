// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Generator.Sampler;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class SamplerTests : TestBase
    {
        private Container _containerWithNamePk;
        private Container _containerWithIdPk;

        private const string CONTAINER_NAME_ID_PK = "containerWithIdPk";
        private const string CONTAINER_NAME_NAME_PK = "containerWithNamePk";

        [TestInitialize]
        public async Task Initialize()
        {
            CosmosClientProvider cosmosClientProvider = _application.Services.GetService<CosmosClientProvider>();
            CosmosClient cosmosClient = cosmosClientProvider.Clients[cosmosClientProvider.RuntimeConfigProvider.GetConfig().DefaultDataSourceName];
            Database database = await cosmosClient.CreateDatabaseIfNotExistsAsync(DATABASE_NAME);

            _containerWithIdPk = await database.CreateContainerIfNotExistsAsync(CONTAINER_NAME_ID_PK, "/id");
            _containerWithNamePk = await database.CreateContainerIfNotExistsAsync(CONTAINER_NAME_NAME_PK, "/name");
        }

        [TestMethod(displayName: "TopNSampler Scenarios")] 
        [DataRow(1, DisplayName = "TopNSampler: Get Top 1 record")]
        [DataRow(5, DisplayName = "TopNSampler: Get Top 5 record")]
        public async Task TestTopNSampler(int count)
        {
            CreateItems(DATABASE_NAME, CONTAINER_NAME_ID_PK, 10);

            // Arrange
            ISchemaGeneratorSampler topNSampler = new TopNSampler(count);

            // Act
            var result = await topNSampler.GetSampleAsync(_containerWithIdPk);

            // Assert
            Assert.AreEqual(count, result.Count);
        }

        [TestMethod (displayName: "PartitionBasedSampler Scenarios")]
        [DataRow(1, 0, 9, DisplayName = "PartitionBasedSampler: Get 1 record per partition, irrespective of days")]
        [DataRow(2, 0, 18, DisplayName = "PartitionBasedSampler: Get 2 record per partition, irrespective of days")]
        [DataRow(2, 1, 18, DisplayName = "PartitionBasedSampler: Get 2 record per partition, fetch only 1 day old record")]
        [DataRow(0, 1, 200, DisplayName = "PartitionBasedSampler: Get 1 day old record per partition, without any count limit")]
        [DataRow(null, null, 45, DisplayName = "PartitionBasedSampler: Fetch default value i.e. 5 records per partition if limit is not set")]
        public async Task TestPartitionBasedSampler(int? numberOfRecordsPerPartition, int? maxDaysPerPartition, int expectedResultCount)
        {
            CreateItems(DATABASE_NAME, CONTAINER_NAME_NAME_PK, 200, "/name");

            // Arrange
            ISchemaGeneratorSampler partitionBasedSampler
                = new PartitionBasedSampler(partitionKeyPath: "/id",
                        numberOfRecordsPerPartition: numberOfRecordsPerPartition,
                        maxDaysPerPartition: maxDaysPerPartition);
            // Act
            var result = await partitionBasedSampler.GetSampleAsync(_containerWithNamePk);

            Assert.AreEqual(expectedResultCount, result.Count);

            Console.WriteLine(result);
        }

        /// <summary>
        /// Runs once after all tests in this class are executed
        /// </summary>
        [TestCleanup]
        public async Task CleanUp()
        {
            await _containerWithIdPk.DeleteContainerAsync();
            await _containerWithNamePk.DeleteContainerAsync();
        }
    }
}
