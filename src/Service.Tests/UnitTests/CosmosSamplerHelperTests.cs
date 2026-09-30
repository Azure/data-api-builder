// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Core.Generator.Sampler;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class CosmosSamplerHelperTests
    {
        [TestMethod]
        public async Task ExecuteQueryAsync_ProcessesArrayResponseAndInvokesCallback()
        {
            using ResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new MemoryStream(Encoding.UTF8.GetBytes("{\"Documents\":[{\"Id\":1},{\"Id\":2}]}"))
            };
            CosmosExecutor executor = CreateExecutor(response);
            List<SampleItem?> callbacks = new();

            List<SampleItem> results = await executor.ExecuteQueryAsync<SampleItem>(
                "SELECT * FROM c",
                item => callbacks.Add(item));

            CollectionAssert.AreEqual(new[] { 1, 2 }, results.ConvertAll(item => item.Id));
            Assert.AreEqual(2, callbacks.Count);
        }

        [TestMethod]
        public async Task ExecuteQueryAsync_UnsuccessfulResponseThrows()
        {
            using ResponseMessage response = new(HttpStatusCode.BadRequest);
            CosmosExecutor executor = CreateExecutor(response);

            await Assert.ThrowsExceptionAsync<Exception>(() =>
                executor.ExecuteQueryAsync<SampleItem>("SELECT * FROM c"));
        }

        [TestMethod]
        public async Task TimePartitionedSampler_GappedTimestampsReturnsOnlyPopulatedGroups()
        {
            int[] timestamps = { 100, 101, 103, 104, 106, 107, 109, 110 };
            Mock<Container> container = new();
            container.Setup(x => x.GetItemQueryStreamIterator(
                    It.IsAny<QueryDefinition>(),
                    It.IsAny<string>(),
                    It.IsAny<QueryRequestOptions>()))
                .Returns((QueryDefinition query, string _, QueryRequestOptions _) =>
                    CreateIterator(CreateSamplerResponse(query.QueryText, timestamps)));
            Mock<TimePartitionedSampler> sampler = new(
                container.Object,
                null,
                null,
                null,
                Mock.Of<ILogger>());
            sampler.Setup(x => x.GetTimeStampThreshold()).Returns(100);

            List<JsonDocument> result = await sampler.Object.GetSampleAsync();

            Assert.AreEqual(8, result.Count);
            result.ForEach(document => document.Dispose());
        }

        private static CosmosExecutor CreateExecutor(ResponseMessage response)
        {
            Mock<FeedIterator> iterator = new();
            iterator.SetupSequence(x => x.HasMoreResults).Returns(true).Returns(false);
            iterator.Setup(x => x.ReadNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(response);
            Mock<Container> container = new();
            container.Setup(x => x.GetItemQueryStreamIterator(
                    It.IsAny<QueryDefinition>(),
                    It.IsAny<string>(),
                    It.IsAny<QueryRequestOptions>()))
                .Returns(iterator.Object);
            return new CosmosExecutor(container.Object, Mock.Of<ILogger>());
        }

        private static FeedIterator CreateIterator(string content)
        {
            ResponseMessage response = new(HttpStatusCode.OK)
            {
                Content = new MemoryStream(Encoding.UTF8.GetBytes(content))
            };
            Mock<FeedIterator> iterator = new();
            iterator.SetupSequence(x => x.HasMoreResults).Returns(true).Returns(false);
            iterator.Setup(x => x.ReadNextAsync(It.IsAny<CancellationToken>())).ReturnsAsync(response);
            return iterator.Object;
        }

        private static string CreateSamplerResponse(string query, IReadOnlyCollection<int> timestamps)
        {
            if (query == "SELECT VALUE MAX(c._ts) FROM c")
            {
                return JsonSerializer.Serialize(new { Documents = new[] { timestamps.Max() } });
            }

            System.Text.RegularExpressions.Match range = Regex.Match(query, @"c\._ts >= (?<start>\d+) AND c\._ts <= (?<end>\d+)");
            Assert.IsTrue(range.Success, $"Unexpected sampler query: {query}");
            int start = int.Parse(range.Groups["start"].Value);
            int end = int.Parse(range.Groups["end"].Value);
            IEnumerable<object> documents = timestamps
                .Where(timestamp => timestamp >= start && timestamp <= end)
                .Select(timestamp => new { timestamp });

            return JsonSerializer.Serialize(new { Documents = documents });
        }

        private sealed class SampleItem
        {
            public int Id { get; set; }
        }
    }
}
