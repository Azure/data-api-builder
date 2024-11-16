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
    private static StringWriter _writer;
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

    internal static string _bookDBOContents;

    private static void GenerateConfigFile(
        DatabaseType databaseType = DatabaseType.MSSQL,
        string sessionContext = "true",
        string connectionString = "",
        string restPath = "rest",
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
                            ""set-session-context"": " + sessionContext + @"
                        },
                        ""connection-string"": """ + connectionString + @"""
                    },
                    ""runtime"": {
                        ""rest"": {
                          ""enabled"": " + restEnabled + @",
                          ""path"": ""/" + restPath + @""",
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
        HttpResponseMessage gQLResult = await _testClient.SendAsync(request);

        // Assert rest and graphQL requests return status OK.
        Assert.AreEqual(HttpStatusCode.OK, restResult.StatusCode);
        Assert.AreEqual(HttpStatusCode.OK, gQLResult.StatusCode);

        // Save the contents from request to validate results after hot-reloads.
        string restContent = await restResult.Content.ReadAsStringAsync();
        using JsonDocument doc = JsonDocument.Parse(restContent);
        _bookDBOContents = doc.RootElement.GetProperty("value").ToString();
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
        string restBookContents = $"{{\"value\":{_bookDBOContents}}}";
        string restPath = "restApi";
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
        SqlTestHelper.PerformTestEqualJsonStrings(_bookDBOContents, reloadGQLContents.GetProperty("items").ToString());
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

    /// <summary>
    /// Hot reload the configuration file by saving a new database type and connection string.
    /// Validate that the response from the server is correct when making a new request after
    /// the change in database type.
    /// </summary>
    [TestCategory(MSSQL_ENVIRONMENT)]
    [TestMethod]
    public async Task HotReloadConfigDataSourceEndToEndTest()
    {
        // Arrange
        RuntimeConfig previousRuntimeConfig = _configProvider.GetConfig();
        MsSqlOptions previousSessionContext = previousRuntimeConfig.DataSource.GetTypedOptions<MsSqlOptions>();

        // String has additions that are not in original connection string
        string expectedConnectionString = $"{ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.MSSQL).Replace("\\", "\\\\")}" + "Trusted_Connection=True;";

        // Act
        GenerateConfigFile(
            sessionContext: "false",
            connectionString: expectedConnectionString);
        System.Threading.Thread.Sleep(3000);

        RuntimeConfig updatedRuntimeConfig = _configProvider.GetConfig();
        MsSqlOptions actualSessionContext = updatedRuntimeConfig.DataSource.GetTypedOptions<MsSqlOptions>();
        JsonElement reloadGQLContents = await GraphQLRequestExecutor.PostGraphQLRequestAsync(
            _testClient,
            _configProvider,
            GQL_QUERY_NAME,
            GQL_QUERY);

        // Assert
        Assert.AreNotEqual(previousSessionContext, actualSessionContext);
        Assert.AreEqual(false, actualSessionContext.SetSessionContext);
        SqlTestHelper.PerformTestEqualJsonStrings(_bookDBOContents, reloadGQLContents.GetProperty("items").ToString());
    }

    /// <summary>
    /// Hot reload the configuration file so that it changes from one connection string
    /// to an invalid connection string, then it hot reloads once more to the original
    /// connection string. Lastly, we assert that the first reload fails while the second one succeeds.
    /// </summary>
    [TestCategory(MSSQL_ENVIRONMENT)]
    [TestMethod]
    public async Task HotReloadConfigConnectionStringEndToEndTest()
    {
        // Arrange
        _writer = new StringWriter();
        Console.SetOut(_writer);

        string failedKeyWord = "Unable to hot reload configuration file due to";
        string succeedKeyWord = "Validated hot-reloaded configuration file";

        // Act
        // Hot Reload should fail here
        GenerateConfigFile(
            connectionString: "");
        System.Threading.Thread.Sleep(5000);

        // Log that shows that hot-reload was not able to validate properly
        string failedConfigLog = $"{_writer.ToString()}";

        // Hot Reload should succeed here
        GenerateConfigFile(
            connectionString: $"{ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.MSSQL).Replace("\\", "\\\\")}");
        System.Threading.Thread.Sleep(8000);

        // Log that shows that hot-reload validated properly
        string succeedConfigLog = $"{_writer.ToString()}";

        HttpResponseMessage restResult = await _testClient.GetAsync("/rest/Book");

        // Assert
        Assert.IsTrue(failedConfigLog.Contains(failedKeyWord));
        Assert.IsTrue(succeedConfigLog.Contains(succeedKeyWord));
        Assert.AreEqual(HttpStatusCode.OK, restResult.StatusCode);
    }

    /// <summary>
    /// /// (Warning: This test only currently works in the pipeline due to constrains of not
    /// being able to change from one database type to another, under normal circumstances
    /// hot reload allows changes from one database type to another)
    /// Hot reload the configuration file so that it changes from one database type to another.
    /// Then it hot reloads once more to the original database type. We assert that the
    /// first reload fails while the second one succeeds.
    /// </summary>
    [TestCategory(MSSQL_ENVIRONMENT)]
    [TestMethod]
    public async Task HotReloadConfigDatabaseTypeEndToEndTest()
    {
        // Arrange
        _writer = new StringWriter();
        Console.SetOut(_writer);

        string failedKeyWord = "Unable to hot reload configuration file due to";
        string succeedKeyWord = "Validated hot-reloaded configuration file";

        // Act
        // Hot Reload should fail here
        GenerateConfigFile(
            databaseType: DatabaseType.PostgreSQL,
            connectionString: $"{ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.POSTGRESQL).Replace("\\", "\\\\")}");
        System.Threading.Thread.Sleep(5000);

        // Log that shows that hot-reload was not able to validate properly
        string failedConfigLog = $"{_writer.ToString()}";

        // Hot Reload should succeed here
        GenerateConfigFile(
            databaseType: DatabaseType.MSSQL,
            connectionString: $"{ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.MSSQL).Replace("\\", "\\\\")}");
        System.Threading.Thread.Sleep(5000);

        // Log that shows that hot-reload validated properly
        string succeedConfigLog = $"{_writer.ToString()}";

        HttpResponseMessage restResult = await _testClient.GetAsync("/rest/Book");

        // Assert
        Assert.IsTrue(failedConfigLog.Contains(failedKeyWord));
        Assert.IsTrue(succeedConfigLog.Contains(succeedKeyWord));
        Assert.AreEqual(HttpStatusCode.OK, restResult.StatusCode);
    }
}
