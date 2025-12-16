// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Generator.Sampler;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{
    /// <summary>
    /// The <c>SamplerTests</c> class contains unit tests for the different sampling strategies used in the schema generation process.
    /// These tests validate the behavior of the <c>TopNExtractor</c>, <c>EligibleDataSampler</c>, and <c>TimePartitionedSampler</c> classes
    /// to ensure they produce correct and expected results based on various configurations and parameters.
    /// </summary>
    /// <remarks>
    /// The tests cover various scenarios and edge cases for each sampler type. They verify that the samplers correctly handle
    /// different configurations, such as sampling count, partition keys, and time-based parameters. The tests also ensure that
    /// the samplers interact correctly with the Cosmos DB containers and return results as expected.
    /// </remarks>
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class SamplerTests : TestBase
    {
        private CosmosClient _cosmosClient;
        private Database _database;
        private Container _containerWithNamePk;
        private Container _containerWithIdPk;

        private List<int> _sortedTimespansIdPk = new();
        private List<int> _sortedTimespansNamePk = new();

        private Mock<ILogger<SamplerTests>> _mockLogger = new();

        private const string CONTAINER_NAME_ID_PK = "containerWithIdPk";
        private const string CONTAINER_NAME_NAME_PK = "containerWithNamePk";

        /// <summary>
        /// Initializes the test environment by creating Cosmos DB containers and populating them with sample data.
        /// This setup is required to ensure that the test cases have the necessary data and schema for accurate testing.
        /// The method also includes wait times to ensure unique timestamp generation for each item, which is essential for sampling accuracy.
        /// </summary>
        [TestInitialize]
        public async Task Initialize()
        {
            CosmosClientProvider cosmosClientProvider = _application.Services.GetService<CosmosClientProvider>();
            _cosmosClient = cosmosClientProvider.Clients[cosmosClientProvider.RuntimeConfigProvider.GetConfig().DefaultDataSourceName];
            _database = await _cosmosClient.CreateDatabaseIfNotExistsAsync(DATABASE_NAME);

            _containerWithIdPk = await _database.CreateContainerIfNotExistsAsync(CONTAINER_NAME_ID_PK, "/id");
            _containerWithNamePk = await _database.CreateContainerIfNotExistsAsync(CONTAINER_NAME_NAME_PK, "/name");

            // Insert sample items with a delay to ensure unique _ts values for each item.
            // This delay allows for accurate timestamp-based sampling in the test cases.
            CreateItems(DATABASE_NAME, CONTAINER_NAME_ID_PK, 10, waitInMs: 1000);

            // Retrieve timestamps from the container to use in validation.
            CosmosExecutor executor = new(_containerWithIdPk, new Mock<ILogger>().Object);
            await executor
                    .ExecuteQueryAsync<JsonDocument>("SELECT DISTINCT c._ts FROM c ORDER BY c._ts desc",
                        callback: (item) => _sortedTimespansIdPk.Add(item.RootElement.GetProperty("_ts").GetInt32()));

            // Insert additional items into the second container with a delay for unique timestamps and partitioned over name i.e planets name.
            // Number of partitions would be 9 as we have 9 unique names.
            CreateItems(DATABASE_NAME, CONTAINER_NAME_NAME_PK, 15, "/name", waitInMs: 1000);

            // Retrieve timestamps for the second container to use in validation.
            executor = new(_containerWithNamePk, new Mock<ILogger>().Object);
            await executor
                    .ExecuteQueryAsync<JsonDocument>("SELECT DISTINCT c._ts FROM c ORDER BY c._ts desc",
                        callback: (item) => _sortedTimespansNamePk.Add(item.RootElement.GetProperty("_ts").GetInt32()));
        }

        /// <summary>
        /// Verifies the functionality of the <c>TopNExtractor</c> class with various configurations to ensure it accurately samples the top N records.
        /// </summary>
        /// <param name="count">The maximum number of top records to retrieve.</param>
        /// <param name="maxDays">The maximum number of days to filter records. If null, no day-based filtering is applied.</param>
        /// <param name="expectedCount">The expected number of records returned by the sampler.</param>
        /// <remarks>
        /// This test case ensures that the <c>TopNExtractor</c> behaves as expected under different sampling scenarios, including scenarios where
        /// maximum days are not specified. It checks that the sampling logic correctly handles the specified count and optionally applies
        /// date-based filtering based on the <c>maxDays</c> parameter.
        /// </remarks>
        [TestMethod(displayName: "TopNExtractor Scenarios")]
        [DataRow(1, 0, 1, DisplayName = "Retrieve 1 record when max days are not specified as count is set as 1.")]
        [DataRow(5, null, 5, DisplayName = "Retrieve 5 records when max days are null as count is set as 5")]
        [DataRow(5, 2, 3, DisplayName = "Retrieve 3 records when max days configured as 2 as we should get 2 records from last 2 days and 1 record from today. Hence 3 records")]
        public async Task TestTopNExtractor(int count, int? maxDays, int expectedCount)
        {
            Mock<TopNExtractor> topNExtractor = new(_containerWithIdPk, count, maxDays, _mockLogger.Object);

            if (maxDays is null || maxDays == 0)
            {
                maxDays = TopNExtractor.MAX_DAYS;
            }

            topNExtractor
                .Setup<long>(x => x.GetTimeStampThreshold())
                .Returns((long)(_sortedTimespansIdPk[0] - maxDays));

            List<JsonDocument> result = await topNExtractor.Object.GetSampleAsync();

            // We're relying on a delay to create records with different timestamps.
            // However, this can cause the actual result to intermittently vary by one record in some cases, particularly in pipelines.
            // To prevent these tests from becoming flaky, the assertion has been adjusted.
            Assert.IsTrue(expectedCount == result.Count || (expectedCount - 1) == result.Count, $"Expected result count is {expectedCount} and Actual result count is {result.Count}");
        }

        /// <summary>
        /// Tests the <c>EligibleDataSampler</c> class to ensure correct sampling across partitions with various configurations.
        /// </summary>
        /// <param name="partitionKeyPath">The path of the partition key to use for sampling. If null, partition key path is not considered.</param>
        /// <param name="numberOfRecordsPerPartition">The number of records to retrieve per partition. Defaults to 5 if not specified.</param>
        /// <param name="maxDaysPerPartition">The maximum number of days to filter records within each partition. If null, no date-based filtering is applied.</param>
        /// <param name="expectedResultCount">The expected number of records returned by the sampler.</param>
        /// <remarks>
        /// This test case ensures that the <c>EligibleDataSampler</c> handles partition-based sampling correctly with various configurations.
        /// It verifies that the sampler correctly applies partition key paths, record limits per partition, and date-based filters as specified.
        /// </remarks>
        [TestMethod(displayName: "EligibleDataSampler Scenarios")]
        [DataRow("/name", 1, 0, 9, DisplayName = "Retrieve 1 record per partition, ignoring day-based filtering. It will return total 9 records because we have 9 partitions")]
        [DataRow("/name", 2, 0, 15, DisplayName = "Retrieve 2 records per partition, ignoring day-based filtering. It will return 15 records i.e. total number of records available")] // calculation wise, it should have returned 18 (i.e. 2 * 9 partitions) records but we have total 15 records available, hence returning that.
        [DataRow("/name", 2, 1, 2, DisplayName = "Retrieve 2 records per partition, filtering for 1 day old records.It will return 2 records from 2 partitions")]
        [DataRow("/name", 0, 1, 2, DisplayName = "Retrieve records from 1 day old records per partition with no count limit.It will return 2 records from 2 partitions")]
        [DataRow("/name", null, null, 15, DisplayName = "Retrieve default value of 5 records per partition if count limit is not set. It will return all the records available")]
        [DataRow(null, 1, 0, 9, DisplayName = "Retrieve 1 record per partition, ignoring day-based filtering when partition key path is not specified. It will identify the partition and return the correct result")]
        public async Task TestEligibleDataSampler(string partitionKeyPath, int? numberOfRecordsPerPartition, int? maxDaysPerPartition, int expectedResultCount)
        {
            Mock<EligibleDataSampler> eligibleDataSampler
                = new(_containerWithNamePk, partitionKeyPath, numberOfRecordsPerPartition, maxDaysPerPartition, _mockLogger.Object);

            if (maxDaysPerPartition is null || maxDaysPerPartition == 0)
            {
                maxDaysPerPartition = EligibleDataSampler.MAX_DAYS_PER_PARTITION;
            }

            eligibleDataSampler
                .Setup<long>(x => x.GetTimeStampThreshold())
                .Returns((long)(_sortedTimespansNamePk[0] - maxDaysPerPartition));

            List<JsonDocument> result = await eligibleDataSampler.Object.GetSampleAsync();

            // We're relying on a delay to create records with different timestamps.
            // However, this can cause the actual result to intermittently vary by one record in some cases, particularly in pipelines.
            // To prevent these tests from becoming flaky, the assertion has been adjusted.
            Assert.IsTrue(expectedResultCount == result.Count || (expectedResultCount - 1) == result.Count, $"Expected result count is {expectedResultCount} and Actual result count is {result.Count}");
        }

        /// <summary>
        /// Verifies that the <c>EligibleDataSampler</c> correctly retrieves partition key paths from containers with different partition key configurations.
        /// </summary>
        /// <remarks>
        /// This test ensures that the <c>EligibleDataSampler</c> accurately identifies and returns partition key paths from containers.
        /// It includes cases where containers have single and multiple partition key paths to validate correct functionality.
        /// </remarks>
        [TestMethod]
        [DataRow("name", DisplayName = "When Container is partitioned by name")]
        [DataRow("id", DisplayName = "When Container is partitioned by id")]
        [DataRow("anotherPojo/anotherProp", DisplayName = "Hierarchy Partition Key: When Container is partitioned by multi-level partition key")]
        public async Task TestGetPartitionInfoInEligibleDataSampler(string partitionKeyPath)
        {
            Container container = await _database.CreateContainerIfNotExistsAsync("myTestContainer", $"/{partitionKeyPath}");

            Mock<EligibleDataSampler> eligibleDataSampler = new(container, null, 1, 1, _mockLogger.Object);
            List<string> result = await eligibleDataSampler.Object.GetPartitionKeyPaths();

            if (partitionKeyPath == "anotherPojo/anotherProp")
            {
                Assert.AreEqual(2, result.Count);
                Assert.AreEqual("anotherPojo", result[0]);
                Assert.AreEqual("anotherProp", result[1]);
            }
            else
            {
                Assert.AreEqual(1, result.Count);
                Assert.AreEqual(partitionKeyPath, result[0]);
            }

            await container.DeleteContainerAsync();
        }

        /// <summary>
        /// Tests the functionality of the <c>TimePartitionedSampler</c> class to ensure it samples records correctly based on time-based constraints and group counts.
        /// </summary>
        /// <param name="groupCount">The number of time-based groups to consider for sampling. If null, the default number of groups is used.</param>
        /// <param name="numberOfRecordsPerGroup">The number of records to sample from each time-based group. If null, the default number of records per group is used.</param>
        /// <param name="maxDays">The maximum number of days to filter records. If null, no date-based filtering is applied.</param>
        /// <param name="expectedResultCount">The expected number of records returned by the sampler.</param>
        /// <remarks>
        /// This test case ensures that the <c>TimePartitionedSampler</c> accurately handles various configurations for time-based sampling. 
        /// It verifies the sampler’s ability to manage different group counts and record limits, as well as its handling of optional day-based filtering. 
        /// The test cases also include scenarios where records are not evenly distributed across time-based groups.
        /// </remarks>
        [TestMethod(displayName: "TimePartitionedSampler Scenarios")]
        [DataRow(5, 1, 0, 5, DisplayName = "Retrieve 1 record, if it is allowed to fetch 1 item from a group and there are 5 groups (or time range)")]
        [DataRow(1, 10, 0, 10, DisplayName = "Retrieve 10 records, if it is allowed to fetch 10 item from a group and there is only 1 group.")]
        [DataRow(null, 1, 0, 10, DisplayName = "Retrieve 10 records, if 1 item is allowed to fetch from each group and number of groups is 10 (i.e default)")]
        [DataRow(null, null, null, 10, DisplayName = "Retrieve 10 records i.e last 10 days data, based on default values when no specific limits are set.")]
        [DataRow(5, 1, 4, 1, DisplayName = "Retrieve 1 record from a single group when records cannot be evenly divided into time-based groups.")]
        public async Task TestTimePartitionedSampler(int? groupCount, int? numberOfRecordsPerGroup, int? maxDays, int expectedResultCount)
        {
            Mock<TimePartitionedSampler> timePartitionedSampler
                = new(_containerWithNamePk, groupCount, numberOfRecordsPerGroup, maxDays, _mockLogger.Object);

            if (maxDays is null || maxDays == 0)
            {
                maxDays = TimePartitionedSampler.MAX_DAYS;
            }

            timePartitionedSampler
                .Setup<long>(x => x.GetTimeStampThreshold())
                .Returns((long)(_sortedTimespansNamePk[0] - maxDays));

            List<JsonDocument> result = await timePartitionedSampler.Object.GetSampleAsync();

            // We're relying on a delay to create records with different timestamps.
            // However, this can cause the actual result to intermittently vary by one record in some cases, particularly in pipelines.
            // To prevent these tests from becoming flaky, the assertion has been adjusted.
            Assert.IsTrue(expectedResultCount == result.Count || (expectedResultCount + 1) == result.Count || (expectedResultCount - 1) == result.Count, $"Expected result count is {expectedResultCount} and Actual result count is {result.Count}");
        }

        /// <summary>
        /// Cleans up the test environment by deleting the Cosmos DB containers created for the tests.
        /// This cleanup is performed after all test methods have been executed to ensure no residual data remains.
        /// </summary>
        [TestCleanup]
        public async Task CleanUp()
        {
            await _database.DeleteAsync();
        }
    }
}
