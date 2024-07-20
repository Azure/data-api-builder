// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Generator.Sampler;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class SamplerTests : TestBase
    {
        private Database _database;
        private Container _containerWithNamePk;
        private Container _containerWithIdPk;

        private List<int> _sortedTimespansIdPk = new();
        private List<int> _sortedTimespansNamePk = new();

        private const string CONTAINER_NAME_ID_PK = "containerWithIdPk";
        private const string CONTAINER_NAME_NAME_PK = "containerWithNamePk";

        [TestInitialize]
        public async Task Initialize()
        {
            CosmosClientProvider cosmosClientProvider = _application.Services.GetService<CosmosClientProvider>();
            CosmosClient cosmosClient = cosmosClientProvider.Clients[cosmosClientProvider.RuntimeConfigProvider.GetConfig().DefaultDataSourceName];
            _database = await cosmosClient.CreateDatabaseIfNotExistsAsync(DATABASE_NAME);

            _containerWithIdPk = await _database.CreateContainerIfNotExistsAsync(CONTAINER_NAME_ID_PK, "/id");
            _containerWithNamePk = await _database.CreateContainerIfNotExistsAsync(CONTAINER_NAME_NAME_PK, "/name");

            // Wait time is required to generate _ts value different for each item which we are using to do sampling. It might slow down the test execution.
            CreateItems(DATABASE_NAME, CONTAINER_NAME_ID_PK, 10, waitInMs: 1000);

            // Get to know about the timestamps generated so that it can be used in the test cases.
            CosmosExecutor executor = new(_containerWithIdPk);
            await executor.ExecuteQueryAsync<JObject>("SELECT DISTINCT c._ts FROM c ORDER BY c._ts desc",
                (item) => _sortedTimespansIdPk.Add(item.Value<int>("_ts")));

            // Wait time is required to generate _ts value different for each item which we are using to do sampling. It might slow down the test execution.
            CreateItems(DATABASE_NAME, CONTAINER_NAME_NAME_PK, 15, "/name", waitInMs: 1000);

            // Get to know about the timestamps generated so that it can be used in the test cases.
            executor = new(_containerWithNamePk);
            await executor.ExecuteQueryAsync<JObject>("SELECT DISTINCT c._ts FROM c ORDER BY c._ts desc",
                (item) => _sortedTimespansNamePk.Add(item.Value<int>("_ts")));
        }

        [TestMethod(displayName: "TopNSampler Scenarios")]
        [DataRow(1, 0, 1, DisplayName = "TopNSampler: Get Top 1 record, when max days are not configured.")]
        [DataRow(5, null, 5, DisplayName = "TopNSampler: Get Top 5 record, when max days are not configured.")]
        [DataRow(5, 2, 3, DisplayName = "TopNSampler: Get Top 3 record, when max days are configured as 2.")]
        public async Task TestTopNSampler(int count, int? maxDays, int expectedCount)
        {
            Mock<TopNSampler> topNSampler = new(_containerWithIdPk, count, maxDays);
            if (maxDays is not null)
            {
                topNSampler
                    .Setup<long>(x => x.GetTimeStampThreshold())
                    .Returns((long)(_sortedTimespansIdPk[0] - maxDays));
            }

            List<JObject> result = await topNSampler.Object.GetSampleAsync();
            Assert.AreEqual(expectedCount, result.Count);
        }

        [TestMethod(displayName: "PartitionBasedSampler Scenarios")]
        [DataRow("/name", 1, 0, 9, DisplayName = "PartitionBasedSampler: Get 1 record per partition, irrespective of days")]
        [DataRow("/name", 2, 0, 15, DisplayName = "PartitionBasedSampler: Get 2 record per partition, irrespective of days")]
        [DataRow("/name", 2, 1, 1, DisplayName = "PartitionBasedSampler: Get 2 record per partition, fetch only 1 day old record")]
        [DataRow("/name", 0, 1, 2, DisplayName = "PartitionBasedSampler: Get 1 day old record per partition, without any count limit")]
        [DataRow("/name", null, null, 15, DisplayName = "PartitionBasedSampler: Fetch default value i.e. 5 records per partition if limit is not set")]
        [DataRow(null, 1, 0, 9, DisplayName = "PartitionBasedSampler: Get 1 record per partition, irrespective of days, even partition information is not there.")]
        public async Task TestPartitionBasedSampler(string partitionKeyPath, int? numberOfRecordsPerPartition, int? maxDaysPerPartition, int expectedResultCount)
        {
            Mock<PartitionBasedSampler> partitionBasedSampler
                = new(_containerWithNamePk, partitionKeyPath, numberOfRecordsPerPartition, maxDaysPerPartition);
            if (maxDaysPerPartition is not null)
            {
                partitionBasedSampler
                    .Setup<long>(x => x.GetTimeStampThreshold())
                    .Returns((long)(_sortedTimespansNamePk[0] - maxDaysPerPartition));
            }

            List<JObject> result = await partitionBasedSampler.Object.GetSampleAsync();

            Assert.AreEqual(expectedResultCount, result.Count);
        }

        [TestMethod]
        public async Task TestGetPartitionInfoInPartitionBasedSampler()
        {

            Mock<PartitionBasedSampler> partitionBasedSampler = new(_containerWithNamePk, null, 1, 1);
            List<string> result = await partitionBasedSampler.Object.GetPartitionKeyPaths();
            Assert.AreEqual("name", result[0]);

            partitionBasedSampler = new(_containerWithIdPk, null, 1, 1);
            result = await partitionBasedSampler.Object.GetPartitionKeyPaths();
            Assert.AreEqual("id", result[0]);

            // Check if partition key path is getting fetched correctly if customer has multiple partitions
            Container _containerWithHPk = await _database.CreateContainerIfNotExistsAsync("newcontainerwithhpk", "/anotherPojo/anotherProp");
            partitionBasedSampler = new(_containerWithHPk, null, 1, 1);
            result = await partitionBasedSampler.Object.GetPartitionKeyPaths();
            Assert.AreEqual("anotherPojo", result[0]);
            Assert.AreEqual("anotherProp", result[1]);

            await _containerWithHPk.DeleteContainerAsync();
        }

        [TestMethod(displayName: "TimeBasedSampler Scenarios")]
        [DataRow(5, 1, 0, 5, DisplayName = "TimeBasedSampler: Get 1 record, if it is allowed to fetch 1 item from a group and there are 5 groups (or time range)")]
        [DataRow(1, 10, 0, 10, DisplayName = "TimeBasedSampler: Get 10 records, if it is allowed to fetch 10 item from a group and there is only 1 group.")]
        [DataRow(null, 1, 0, 10, DisplayName = "TimeBasedSampler: Get 2 records, if 1 item is allowed to fetch from each group and number of groups is 10 (i.e default)")]
        [DataRow(null, null, null, 10, DisplayName = "TimeBasedSampler: Calculate number of item according to the default values")]
        [DataRow(5, 1, 4, 1, DisplayName = "TimeBasedSampler: Data is not dividable into groups, based on time, hence, putting all the records in single group and returning 1 record ")]
        public async Task TestTimeBasedSampler(int? groupCount, int? numberOfRecordsPerGroup, int? maxDays, int expectedResultCount)
        {
            Mock<TimeBasedSampler> timeBasedSampler
                = new(_containerWithNamePk, groupCount, numberOfRecordsPerGroup, maxDays);
            if (maxDays is not null)
            {
                timeBasedSampler
                    .Setup<long>(x => x.GetTimeStampThreshold())
                    .Returns((long)(_sortedTimespansNamePk[0] - maxDays));
            }

            List<JObject> result = await timeBasedSampler.Object.GetSampleAsync();

            Assert.AreEqual(expectedResultCount, result.Count);
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
