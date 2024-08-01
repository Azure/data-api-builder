// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO.Abstractions;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Newtonsoft.Json.Linq;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests;

public class TestBase
{
    internal const string DATABASE_NAME = "graphqldb";
    // Intentionally removed name attribute from Planet model to test scenario where the 'name' attribute
    // is not explicitly added in the schema
    internal const string GRAPHQL_SCHEMA = @"
type Character {
    id : ID,
    name : String,
    type: String,
    homePlanet: Int,
    primaryFunction: String,
    star: Star
}

type Planet @model(name:""PlanetAlias"") {
    id : ID!,
    name : String,
    character: Character,
    age : Float,
    dimension : String,
    earth: Earth,
    tags: [String!],
    stars: [Star],
    additionalAttributes: [AdditionalAttribute],
    moons: [Moon],
    suns: [Sun]
}

type Star {
    id : ID,
    name : String,
    tag: Tag
}

type Tag {
    id : ID,
    name : String
}

type Moon {
    id : ID,
    name : String,
    details : String,
    moonAdditionalAttributes: [MoonAdditionalAttribute]
}

type Earth {
    id : ID,
    name : String,
    type: String @authorize(roles: [""authenticated""])
}

type Sun {
    id : ID,
    name : String
}

type AdditionalAttribute {
    id : ID,
    name : String,
    type: String
}

type MoonAdditionalAttribute {
    id : ID,
    name : String,
    moreAttributes: [MoreAttribute!]
}

type MoreAttribute {
    id : ID,
    name : String,
    type: String @authorize(roles: [""authenticated""])
}

type InvalidAuthModel @model @authorize(policy: ""Crater"") {
    id : ID!,
    name : String
}

type PlanetAgain @model {
    id : ID,
    name : String,
    type: String @authorize(roles: [""authenticated""])
}
";

    private static string[] _planets = { "Earth", "Mars", "Jupiter", "Tatooine", "Endor", "Dagobah", "Hoth", "Bespin", "Spec%ial" };

    private HttpClient _client;
    internal WebApplicationFactory<Startup> _application;
    internal string _containerName = Guid.NewGuid().ToString();

    [TestInitialize]
    public void Init()
    {
        _application = SetupTestApplicationFactory();

        _client = _application.CreateClient();
    }

    protected WebApplicationFactory<Startup> SetupTestApplicationFactory()
    {
        // Read the base config from the file system
        TestHelper.SetupDatabaseEnvironment(TestCategory.COSMOSDBNOSQL);
        FileSystemRuntimeConfigLoader baseLoader = TestHelper.GetRuntimeConfigLoader();
        if (!baseLoader.TryLoadKnownConfig(out RuntimeConfig baseConfig))
        {
            throw new ApplicationException("Failed to load the default CosmosDB_NoSQL config and cannot continue with tests.");
        }

        Dictionary<string, object> updatedOptions = baseConfig.DataSource.Options;
        updatedOptions["container"] = JsonDocument.Parse($"\"{_containerName}\"").RootElement;

        RuntimeConfig updatedConfig = baseConfig
            with
        {
            DataSource = baseConfig.DataSource with { Options = updatedOptions },
            Entities = new(baseConfig.Entities.ToDictionary(e => e.Key, e => e.Value with { Source = e.Value.Source with { Object = _containerName } }))
        };

        // Setup a mock file system, and use that one with the loader/provider for the config
        MockFileSystem fileSystem = new(new Dictionary<string, MockFileData>()
        {
            { @"../schema.gql", new MockFileData(GRAPHQL_SCHEMA) },
            { FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(updatedConfig.ToJson()) }
        });
        FileSystemRuntimeConfigLoader loader = new(fileSystem);
        RuntimeConfigProvider provider = new(loader);

        ISqlMetadataProvider cosmosSqlMetadataProvider = new CosmosSqlMetadataProvider(provider, fileSystem);
        Mock<IMetadataProviderFactory> metadataProviderFactory = new();
        metadataProviderFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(cosmosSqlMetadataProvider);

        IAuthorizationResolver authorizationResolverCosmos = new AuthorizationResolver(provider, metadataProviderFactory.Object);

        return new WebApplicationFactory<Startup>()
            .WithWebHostBuilder(builder =>
            {
                _ = builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IFileSystem>(fileSystem);
                    services.AddSingleton(loader);
                    services.AddSingleton(provider);
                    services.AddSingleton(authorizationResolverCosmos);
                });
            });
    }

    [TestCleanup]
    public void CleanupAfterEachTest()
    {
        TestHelper.UnsetAllDABEnvironmentVariables();
    }

    /// <summary>
    /// Creates items on the specified container
    /// </summary>
    /// <param name="dbName">the database name</param>
    /// <param name="containerName">the container name</param>
    /// <param name="numItems">number of items to be created</param>
    internal List<string> CreateItems(string dbName, string containerName, int numItems, string partitionKeyPath = null, int? waitInMs = null)
    {
        List<string> idList = new();
        CosmosClientProvider cosmosClientProvider = _application.Services.GetService<CosmosClientProvider>();
        CosmosClient cosmosClient = cosmosClientProvider.Clients[cosmosClientProvider.RuntimeConfigProvider.GetConfig().DefaultDataSourceName];
        for (int i = 0; i < numItems; i++)
        {
            if (waitInMs is not null)
            {
                Task.Delay(waitInMs.Value).Wait();
            }

            string uid = Guid.NewGuid().ToString();
            idList.Add(uid);
            dynamic sourceItem = CosmosTestHelper.GetItem(uid, _planets[i % _planets.Length], i);

            PartitionKey partitionKey;
            if (partitionKeyPath == "/name")
            {
                partitionKey = new PartitionKey(sourceItem.name);
            }
            else
            {
                partitionKey = new PartitionKey(uid);
            }

            cosmosClient.GetContainer(dbName, containerName)
                .CreateItemAsync(sourceItem, partitionKey).Wait();
        }

        return idList;
    }

    /// <summary>
    /// Executes the GraphQL request and returns the results
    /// </summary>
    /// <param name="queryName"> Name of the GraphQL query/mutation</param>
    /// <param name="query"> The GraphQL query/mutation</param>
    /// <param name="variables">Variables to be included in the GraphQL request. If null, no variables property is included in the request, to pass an empty object provide an empty dictionary</param>
    /// <returns></returns>
    internal Task<JsonElement> ExecuteGraphQLRequestAsync(string queryName, string query, Dictionary<string, object> variables = null, string authToken = null, string clientRoleHeader = null)
    {
        RuntimeConfigProvider configProvider = _application.Services.GetService<RuntimeConfigProvider>();
        return GraphQLRequestExecutor.PostGraphQLRequestAsync(_client, configProvider, queryName, query, variables, authToken, clientRoleHeader);
    }

    internal async Task<JsonDocument> ExecuteCosmosRequestAsync(string query, int pageSize, string continuationToken, string containerName)
    {
        QueryRequestOptions options = new()
        {
            MaxItemCount = pageSize,
        };
        CosmosClientProvider cosmosClientProvider = _application.Services.GetService<CosmosClientProvider>();
        CosmosClient cosmosClient = cosmosClientProvider.Clients[cosmosClientProvider.RuntimeConfigProvider.GetConfig().DefaultDataSourceName];
        Container c = cosmosClient.GetContainer(DATABASE_NAME, containerName);
        QueryDefinition queryDef = new(query);
        FeedIterator<JObject> resultSetIterator = c.GetItemQueryIterator<JObject>(queryDef, continuationToken, options);
        FeedResponse<JObject> firstPage = await resultSetIterator.ReadNextAsync();
        JArray jsonArray = new();
        IEnumerator<JObject> enumerator = firstPage.GetEnumerator();
        while (enumerator.MoveNext())
        {
            JObject item = enumerator.Current;
            jsonArray.Add(item);
        }

        return JsonDocument.Parse(jsonArray.ToString().Trim());
    }
}
