// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
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

        private sealed class SampleItem
        {
            public int Id { get; set; }
        }
    }
}
