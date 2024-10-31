// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Configuration.HotReload;

[TestClass]
public class ConfigurationHotReloadTests
{
    private const string MSSQL_ENVIRONMENT = TestCategory.MSSQL;
    private static TestServer _testServer;
    private static HttpClient _testClient;
    private static RuntimeConfigProvider _configProvider;
    internal const string CONFIG_FILE_NAME = "hot-reload.dab-config.json";
    internal const string GQL_QUERY_NAME = "books";

    internal const string GQL_QUERY = @"{
                books(first: 100) {
                    items {
                        id
                        title
                        publisher_id
                    }
                }
            }";

    internal const string BOOK_DBO_CONTENTS =
        "[" +
        "{\"id\":1,\"title\":\"Awesome book\",\"publisher_id\":1234}," +
        "{\"id\":2,\"title\":\"Also Awesome book\",\"publisher_id\":1234}," +
        "{\"id\":3,\"title\":\"Great wall of china explained\",\"publisher_id\":2345}," +
        "{\"id\":4,\"title\":\"US history in a nutshell\",\"publisher_id\":2345}," +
        "{\"id\":5,\"title\":\"Chernobyl Diaries\",\"publisher_id\":2323}," +
        "{\"id\":6,\"title\":\"The Palace Door\",\"publisher_id\":2324}," +
        "{\"id\":7,\"title\":\"The Groovy Bar\",\"publisher_id\":2324}," +
        "{\"id\":8,\"title\":\"Time to Eat\",\"publisher_id\":2324}," +
        "{\"id\":9,\"title\":\"Policy-Test-01\",\"publisher_id\":1940}," +
        "{\"id\":10,\"title\":\"Policy-Test-02\",\"publisher_id\":1940}," +
        "{\"id\":11,\"title\":\"Policy-Test-04\",\"publisher_id\":1941}," +
        "{\"id\":12,\"title\":\"Time to Eat 2\",\"publisher_id\":1941}," +
        "{\"id\":13,\"title\":\"Before Sunrise\",\"publisher_id\":1234}," +
        "{\"id\":14,\"title\":\"Before Sunset\",\"publisher_id\":1234}," +
        "]";

    private static void GenerateConfigFile(
        DatabaseType databaseType = DatabaseType.MSSQL,
        string connectionString = "",
        string restPath = "/rest",
        string restEnabled = "true",
        string gQLPath = "/graphQL",
        string gQLEnabled = "true",
        string entityName = "Book",
        string sourceObject = "books",
        string gQLEntityEnabled = "true",
        string gQLEntitySingular = "book",
        string gQLEntityPlural = "books",
        string restEntityEnabled = "true",
        string entityBackingColumn = "title",
        string entityExposedName = "title",
        string configFileName = CONFIG_FILE_NAME)
    {
        File.WriteAllText(configFileName, @"
              {
                ""$schema"": """",
                    ""data-source"": {
                        ""database-type"": """ + databaseType + @""",
                        ""options"": {
                            ""set-session-context"": true
                        },
                        ""connection-string"": """ + connectionString + @"""
                    },
                    ""runtime"": {
                        ""rest"": {
                          ""enabled"": " + restEnabled + @",
                          ""path"": """ + restPath + @""",
                          ""request-body-strict"": true
                        },
                        ""graphql"": {
                          ""enabled"": " + gQLEnabled + @",
                          ""path"": """ + gQLPath + @""",
                          ""allow-introspection"": true
                        },
                        ""host"": {
                          ""cors"": {
                            ""origins"": [
                              ""http://localhost:5000""
                            ],
                            ""allow-credentials"": false
                          },
                          ""authentication"": {
                            ""provider"": ""StaticWebApps""
                          },
                          ""mode"": ""development""
                        }
                      },
                    ""entities"": {
                      """ + entityName + @""": {
                        ""source"": {
                          ""object"": """ + sourceObject + @""",
                          ""type"": ""table""
                        },
                        ""graphql"": {
                          ""enabled"": " + gQLEntityEnabled + @",
                          ""type"": {
                            ""singular"": """ + gQLEntitySingular + @""",
                            ""plural"": """ + gQLEntityPlural + @"""
                          }
                        },
                        ""rest"": {
                          ""enabled"": " + restEntityEnabled + @"
                        },
                        ""permissions"": [
                        {
                          ""role"": ""anonymous"",
                          ""actions"": [
                            {
                              ""action"": ""create""
                            },
                            {
                              ""action"": ""read""
                            },
                            {
                              ""action"": ""update""
                            },
                            {
                              ""action"": ""delete""
                            }
                          ]
                        },
                        {
                          ""role"": ""authenticated"",
                          ""actions"": [
                            {
                              ""action"": ""create""
                            },
                            {
                              ""action"": ""read""
                            },
                            {
                              ""action"": ""update""
                            },
                            {
                              ""action"": ""delete""
                            }
                          ]
                        }
                      ],
                        ""mappings"": {
                          """ + entityBackingColumn + @""": """ + entityExposedName + @"""
                        }
                      }
                  }
              }");
    }

    /// <summary>
    /// Initialize the test fixture by creating the initial configuration file and starting
    /// the test server with it. Validate that the test server returns OK status when handling
    /// valid requests.
    /// </summary>
    [ClassInitialize]
    public static async Task ClassInitializeAsync(TestContext context)
    {
        // Arrange
        GenerateConfigFile(connectionString: $"{ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.MSSQL).Replace("\\", "\\\\")}");
        _testServer = new(Program.CreateWebHostBuilder(new string[] { "--ConfigFileName", CONFIG_FILE_NAME }));
        _testClient = _testServer.CreateClient();
        _configProvider = _testServer.Services.GetService<RuntimeConfigProvider>();

        string query = GQL_QUERY;
        object payload =
            new { query };

        HttpRequestMessage request = new(HttpMethod.Post, "/graphQL")
        {
            Content = JsonContent.Create(payload)
        };

        HttpResponseMessage restResult = await _testClient.GetAsync("/rest/Book");
        string reloadRestContent = await restResult.Content.ReadAsStringAsync();
        Console.WriteLine(reloadRestContent);
        HttpResponseMessage gQLResult = await _testClient.SendAsync(request);

        Assert.AreEqual(HttpStatusCode.OK, restResult.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, gQLResult.StatusCode);
    }

    [ClassCleanup]
    public static void ClassCleanup()
    {
        if (File.Exists(CONFIG_FILE_NAME))
        {
            File.Delete(CONFIG_FILE_NAME);
        }

        _testServer.Dispose();
        _testClient.Dispose();
    }

    /// <summary>
    /// Hot reload the configuration by saving a new file with different rest and graphQL paths.
    /// Validate that the response is correct when making a request with the newly hot-reloaded paths.
    /// </summary>
    [TestCategory(MSSQL_ENVIRONMENT)]
    [TestMethod("Hot-reload runtime paths.")]
    public async Task HotReloadConfigRuntimePathsEndToEndTest()
    {
        // Arrange
        string restBookContents = $"{{\"value\":{BOOK_DBO_CONTENTS}}}";
        string restPath = "/restApi";
        string gQLPath = "/gQLApi";
        string query = GQL_QUERY;
        object payload =
            new { query };

        HttpRequestMessage request = new(HttpMethod.Post, "/graphQL")
        {
            Content = JsonContent.Create(payload)
        };

        GenerateConfigFile(
            connectionString: $"{ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.MSSQL).Replace("\\", "\\\\")}",
            restPath: restPath,
            gQLPath: gQLPath);
        System.Threading.Thread.Sleep(2000);

        // Act
        HttpResponseMessage badPathRestResult = await _testClient.GetAsync($"rest/Book");
        HttpResponseMessage badPathGQLResult = await _testClient.SendAsync(request);

        HttpResponseMessage result = await _testClient.GetAsync($"{restPath}/Book");
        string reloadRestContent = await result.Content.ReadAsStringAsync();
        JsonElement reloadGQLContents = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
            _testClient,
            _configProvider,
            GQL_QUERY_NAME,
            GQL_QUERY);

        // Assert
        // Old paths are not found.
        Assert.AreEqual(HttpStatusCode.BadRequest, badPathRestResult.StatusCode);
        Assert.AreEqual(HttpStatusCode.NotFound, badPathGQLResult.StatusCode);
        // Hot reloaded paths return correct response.
        Assert.IsTrue(SqlTestHelper.JsonStringsDeepEqual(restBookContents, reloadRestContent));
        SqlTestHelper.PerformTestEqualJsonStrings(BOOK_DBO_CONTENTS, reloadGQLContents.GetProperty("items").ToString());
    }

    /// <summary>
    /// Hot reload the configuration file by saving a new file with the rest enabled property
    /// set to false. Validate that the response from the server is NOT FOUND when making a request after
    /// the hot reload.
    /// </summary>
    [TestCategory(MSSQL_ENVIRONMENT)]
    [TestMethod("Hot-reload rest enabled.")]
    public async Task HotReloadConfigRuntimeRestEnabledEndToEndTest()
    {
        // Arrange
        string restEnabled = "false";

        GenerateConfigFile(
            connectionString: $"{ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.MSSQL).Replace("\\", "\\\\")}",
            restEnabled: restEnabled);
        System.Threading.Thread.Sleep(2000);

        // Act
        HttpResponseMessage restResult = await _testClient.GetAsync($"rest/Book");

        // Assert
        Assert.AreEqual(HttpStatusCode.NotFound, restResult.StatusCode);
    }

    /// <summary>
    /// Hot reload the configuration file by saving a new file with the graphQL enabled property
    /// set to false. Validate that the response from the server is NOT FOUND when making a request after
    /// the hot reload.
    /// </summary>
    [TestCategory(MSSQL_ENVIRONMENT)]
    [TestMethod("Hot-reload gql enabled.")]
    public async Task HotReloadConfigRuntimeGQLEnabledEndToEndTest()
    {
        // Arrange
        string gQLEnabled = "false";
        string query = GQL_QUERY;
        object payload =
            new { query };

        HttpRequestMessage request = new(HttpMethod.Post, "/graphQL")
        {
            Content = JsonContent.Create(payload)
        };
        GenerateConfigFile(
            connectionString: $"{ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.MSSQL).Replace("\\", "\\\\")}",
            gQLEnabled: gQLEnabled);
        System.Threading.Thread.Sleep(2000);

        // Act
        HttpResponseMessage gQLResult = await _testClient.SendAsync(request);

        // Assert
        Assert.AreEqual(HttpStatusCode.NotFound, gQLResult.StatusCode);
    }
}
