// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.Tests.Configuration.ConfigurationEndpoints;
using static Azure.DataApiBuilder.Service.Tests.Configuration.ConfigurationJsonBuilder;

namespace Azure.DataApiBuilder.Service.Tests.Configuration;

[TestClass]
public class LoadConfigViaEndpointTests
{
    [TestMethod("Testing that environment variables can be replaced at runtime not only when config is loaded."), TestCategory(TestCategory.COSMOSDBNOSQL)]
    [DataRow(CONFIGURATION_ENDPOINT_V2)]
    public async Task CanLoadConfigWithMissingEnvironmentVariables(string configurationEndpoint)
    {
        TestServer server = new(Program.CreateWebHostFromInMemoryUpdateableConfBuilder(Array.Empty<string>()));
        HttpClient httpClient = server.CreateClient();

        RuntimeConfig config = ReadCosmosConfigurationFromFile() with {
            Schema = "@env('schema')"
        };

        ConfigurationPostParametersV2 @params = new(
            Configuration: config.ToJson(),
            ConfigurationOverrides: "{}",
            Schema: @"
            type Entity {
                id: ID!
                name: String!
            }
            ",
            AccessToken: null
        );

        JsonContent content = JsonContent.Create(@params);

        HttpResponseMessage postResult =
            await httpClient.PostAsync(configurationEndpoint, content);
        Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);
    }
}
