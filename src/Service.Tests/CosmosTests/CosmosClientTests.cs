// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
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
            Assert.AreEqual(client.ClientOptions.ApplicationName, CosmosClientProvider.DEFAULT_APP_NAME);
        }

        [TestMethod]
        public void CosmosClientEnvUserAgent()
        {
            string appName = "gql_dab_cosmos";
            Environment.SetEnvironmentVariable(CosmosClientProvider.DAB_APP_NAME_ENV, appName);

            CosmosClient client = _application.Services.GetService<CosmosClientProvider>().Client;
            // Validate results
            Assert.AreEqual(client.ClientOptions.ApplicationName, appName);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable(CosmosClientProvider.DAB_APP_NAME_ENV, null);
        }
    }
}
