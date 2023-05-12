// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Azure.DataApiBuilder.Service.Resolvers;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class CosmosClientTests : TestBase
    {
        [TestMethod]
        public void CosmosClientDefaultUserAgent()
        {
            CosmosClient client = _application.Services.GetService<CosmosClientProvider>().Client;
            // Validate results
            Assert.AreEqual(client.ClientOptions.ApplicationName,
                CosmosClientProvider.DEFAULT_APP_NAME);
        }

    }
}
