// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using Azure.DataApiBuilder.Auth;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.Cosmos;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

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

        [TestMethod]
        public void CosmosClientEnvUserAgent()
        {

            MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>()
            {
                { @"../schema.gql", new MockFileData(TestBase.GRAPHQL_SCHEMA) }
            });

            RuntimeConfigProvider runtimeConfigProvider = TestHelper.GetRuntimeConfigProvider(CosmosTestHelper.ConfigPath);
            ISqlMetadataProvider cosmosSqlMetadataProvider = new CosmosSqlMetadataProvider(runtimeConfigProvider, fileSystem);
            Mock<ILogger<AuthorizationResolver>> authorizationResolverLogger = new();
            IAuthorizationResolver authorizationResolverCosmos = new AuthorizationResolver(runtimeConfigProvider, cosmosSqlMetadataProvider, authorizationResolverLogger.Object);
            string appName = "gql_dab_cosmos";
            Environment.SetEnvironmentVariable(CosmosClientProvider.DAB_APP_NAME_ENV, appName);
            WebApplicationFactory<Startup> application = new WebApplicationFactory<Startup>()
                .WithWebHostBuilder(builder =>
                {
                    _ = builder.ConfigureTestServices(services =>
                    {
                        services.AddSingleton<IFileSystem>(fileSystem);
                        services.AddSingleton(runtimeConfigProvider);
                        services.AddSingleton(authorizationResolverCosmos);
                    });
                });

            CosmosClient client = application.Services.GetService<CosmosClientProvider>().Client;
            // Validate results
            Assert.AreEqual(client.ClientOptions.ApplicationName, appName);
        }

    }
}
