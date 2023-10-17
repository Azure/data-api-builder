// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
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

        JsonContent content = GetJsonContentForCosmosConfigRequest(configurationEndpoint);

        HttpResponseMessage postResult =
            await httpClient.PostAsync(configurationEndpoint, content);
        Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);
    }
}
