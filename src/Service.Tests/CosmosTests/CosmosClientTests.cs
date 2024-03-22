// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Product;
using Microsoft.AspNetCore.Mvc.Testing;
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
            CosmosClientProvider cosmosClientProvider = _application.Services.GetService<CosmosClientProvider>();
            CosmosClient client = cosmosClientProvider.Clients[cosmosClientProvider.RuntimeConfigProvider.GetConfig().DefaultDataSourceName];
            // Validate results
            Assert.AreEqual(client.ClientOptions.ApplicationName, ProductInfo.DAB_USER_AGENT);
        }

        [TestMethod]
        public void CosmosClientEnvUserAgent()
        {
            string appName = "gql_dab_cosmos";
            Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, appName);

            // We need to create a new application factory to pick up the environment variable
            WebApplicationFactory<Startup> application = SetupTestApplicationFactory();

            CosmosClientProvider cosmosClientProvider = application.Services.GetService<CosmosClientProvider>();
            CosmosClient client = cosmosClientProvider.Clients[cosmosClientProvider.RuntimeConfigProvider.GetConfig().DefaultDataSourceName];
            // Validate results
            Assert.AreEqual(client.ClientOptions.ApplicationName, appName);
        }

        [TestCleanup]
        public void Cleanup()
        {
            Environment.SetEnvironmentVariable(ProductInfo.DAB_APP_NAME_ENV, null);
        }
    }
}
