// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Controllers;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.Tests.Configuration.ConfigurationEndpoints;
using static Azure.DataApiBuilder.Service.Tests.Configuration.TestConfigFileReader;

namespace Azure.DataApiBuilder.Service.Tests.Configuration;

[TestClass]
public class LoadConfigViaEndpointTests
{
    [TestMethod("Testing that missing environment variables won't cause runtime failure."), TestCategory(TestCategory.COSMOSDBNOSQL)]
    [DataRow(CONFIGURATION_ENDPOINT)]
    [DataRow(CONFIGURATION_ENDPOINT_V2)]
    public async Task CanLoadConfigWithMissingEnvironmentVariables(string configurationEndpoint)
    {
        TestServer server = new(Program.CreateWebHostFromInMemoryUpdatableConfBuilder(Array.Empty<string>()));
        HttpClient httpClient = server.CreateClient();

        (RuntimeConfig config, JsonContent content) = GetParameterContent(configurationEndpoint);

        HttpResponseMessage postResult =
            await httpClient.PostAsync(configurationEndpoint, content);
        Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

        RuntimeConfigProvider configProvider = server.Services.GetService(typeof(RuntimeConfigProvider)) as RuntimeConfigProvider;
        RuntimeConfig loadedConfig = configProvider.GetConfig();

        Assert.AreEqual(config.Schema, loadedConfig.Schema);
    }

    [TestMethod("Testing that environment variables can be replaced at runtime not only when config is loaded."), TestCategory(TestCategory.COSMOSDBNOSQL)]
    [DataRow(CONFIGURATION_ENDPOINT)]
    [DataRow(CONFIGURATION_ENDPOINT_V2)]
    [Ignore("We don't want to environment variable substitution in late configuration, but test is left in for if this changes.")]
    public async Task CanLoadConfigWithEnvironmentVariables(string configurationEndpoint)
    {
        Environment.SetEnvironmentVariable("schema", "schema.graphql");
        TestServer server = new(Program.CreateWebHostFromInMemoryUpdatableConfBuilder(Array.Empty<string>()));
        HttpClient httpClient = server.CreateClient();

        (RuntimeConfig config, JsonContent content) = GetParameterContent(configurationEndpoint);

        HttpResponseMessage postResult =
            await httpClient.PostAsync(configurationEndpoint, content);
        Assert.AreEqual(HttpStatusCode.OK, postResult.StatusCode);

        RuntimeConfigProvider configProvider = server.Services.GetService(typeof(RuntimeConfigProvider)) as RuntimeConfigProvider;
        RuntimeConfig loadedConfig = configProvider.GetConfig();

        Assert.AreNotEqual(config.Schema, loadedConfig.Schema);
        Assert.AreEqual(Environment.GetEnvironmentVariable("schema"), loadedConfig.Schema);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Environment.SetEnvironmentVariable("schema", null);
    }

    private static (RuntimeConfig, JsonContent) GetParameterContent(string endpoint)
    {
        RuntimeConfig config = ReadCosmosConfigurationFromFile() with { Schema = "@env('schema')" };

        if (endpoint == CONFIGURATION_ENDPOINT)
        {
            ConfigurationPostParameters @params = new(
                Configuration: config.ToJson(),
                Schema: @"
                type Entity {
                    id: ID!
                    name: String!
                }
                ",
                ConnectionString: "AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==",
                AccessToken: null
            );

            return (config, JsonContent.Create(@params));
        }
        else if (endpoint == CONFIGURATION_ENDPOINT_V2)
        {
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

            return (config, JsonContent.Create(@params));
        }

        throw new ArgumentException($"Unknown endpoint: {endpoint}");
    }
}
