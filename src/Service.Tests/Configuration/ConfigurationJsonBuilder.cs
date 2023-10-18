// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Service.Controllers;
using Microsoft.IdentityModel.Tokens;
using static Azure.DataApiBuilder.Config.FileSystemRuntimeConfigLoader;
using static Azure.DataApiBuilder.Service.Tests.Configuration.ConfigurationEndpoints;

namespace Azure.DataApiBuilder.Service.Tests.Configuration;

/// <summary>
/// Provides methods to build the json content for the configuration endpoint.
/// </summary>
internal static class ConfigurationJsonBuilder
{
    public const string COSMOS_ENVIRONMENT = TestCategory.COSMOSDBNOSQL;
    public const string COSMOS_DATABASE_NAME = "config_db";

    public static JsonContent GetJsonContentForCosmosConfigRequest(string endpoint, string config = null, bool useAccessToken = false)
    {
        if (CONFIGURATION_ENDPOINT == endpoint)
        {
            ConfigurationPostParameters configParams = GetCosmosConfigurationParameters();
            if (config is not null)
            {
                configParams = configParams with { Configuration = config };
            }

            if (useAccessToken)
            {
                configParams = configParams with
                {
                    ConnectionString = "AccountEndpoint=https://localhost:8081/;",
                    AccessToken = GenerateMockJwtToken()
                };
            }

            return JsonContent.Create(configParams);
        }
        else if (CONFIGURATION_ENDPOINT_V2 == endpoint)
        {
            ConfigurationPostParametersV2 configParams = GetCosmosConfigurationParametersV2();
            if (config != null)
            {
                configParams = configParams with { Configuration = config };
            }

            if (useAccessToken)
            {
                // With an invalid access token, when a new instance of CosmosClient is created with that token, it
                // won't throw an exception.  But when a graphql request is coming in, that's when it throws a 401
                // exception. To prevent this, CosmosClientProvider parses the token and retrieves the "exp" property
                // from the token, if it's not valid, then we will throw an exception from our code before it
                // initiating a client. Uses a valid fake JWT access token for testing purposes.
                RuntimeConfig overrides = new(
                    Schema: null,
                    DataSource: new DataSource(DatabaseType.CosmosDB_NoSQL, "AccountEndpoint=https://localhost:8081/;", new()),
                    Runtime: null,
                    Entities: new(new Dictionary<string, Entity>()));

                configParams = configParams with
                {
                    ConfigurationOverrides = overrides.ToJson(),
                    AccessToken = GenerateMockJwtToken()
                };
            }

            return JsonContent.Create(configParams);
        }
        else
        {
            throw new ArgumentException($"Unexpected configuration endpoint. {endpoint}");
        }
    }

    private static string GenerateMockJwtToken()
    {
        string mySecret = "PlaceholderPlaceholder";
        SymmetricSecurityKey mySecurityKey = new(Encoding.ASCII.GetBytes(mySecret));

        JwtSecurityTokenHandler tokenHandler = new();
        SecurityTokenDescriptor tokenDescriptor = new()
        {
            Subject = new ClaimsIdentity(new Claim[] { }),
            Expires = DateTime.UtcNow.AddMinutes(5),
            Issuer = "http://mysite.com",
            Audience = "http://myaudience.com",
            SigningCredentials = new SigningCredentials(mySecurityKey, SecurityAlgorithms.HmacSha256Signature)
        };

        SecurityToken token = tokenHandler.CreateToken(tokenDescriptor);
        return tokenHandler.WriteToken(token);
    }

    public static ConfigurationPostParameters GetCosmosConfigurationParameters()
    {
        RuntimeConfig configuration = ReadCosmosConfigurationFromFile();
        return new(
            configuration.ToJson(),
            File.ReadAllText("schema.gql"),
            $"AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;Database={COSMOS_DATABASE_NAME}",
            AccessToken: null);
    }

    private static ConfigurationPostParametersV2 GetCosmosConfigurationParametersV2()
    {
        RuntimeConfig configuration = ReadCosmosConfigurationFromFile();
        RuntimeConfig overrides = new(
            Schema: null,
            DataSource: new DataSource(DatabaseType.CosmosDB_NoSQL, $"AccountEndpoint=https://localhost:8081/;AccountKey=C2y6yDjf5/R+ob0N8A7Cgv30VRDJIWEHLM+4QDU5DE2nQ9nDuVTqobD4b8mGGyPMbIZnqyMsEcaGQy67XIw/Jw==;Database={COSMOS_DATABASE_NAME}", new()),
            Runtime: null,
            Entities: new(new Dictionary<string, Entity>()));

        return new(
            configuration.ToJson(),
            overrides.ToJson(),
            File.ReadAllText("schema.gql"),
            AccessToken: null);
    }

    public static RuntimeConfig ReadCosmosConfigurationFromFile()
    {
        string cosmosFile = $"{CONFIGFILE_NAME}.{COSMOS_ENVIRONMENT}{CONFIG_EXTENSION}";

        string configurationFileContents = File.ReadAllText(cosmosFile);
        if (!RuntimeConfigLoader.TryParseConfig(configurationFileContents, out RuntimeConfig config))
        {
            throw new Exception("Failed to parse configuration file.");
        }

        // The Schema file isn't provided in the configuration file when going through the configuration endpoint so we're removing it.
        config.DataSource.Options.Remove("Schema");
        return config;
    }
}
