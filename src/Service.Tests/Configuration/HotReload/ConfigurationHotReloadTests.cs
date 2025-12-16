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
using Microsoft.Extensions.Logging;
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
    private const string CONFIG_FILE_NAME = "hot-reload.dab-config.json";
    private const string GQL_QUERY_NAME = "books";

    private const string GQL_QUERY = @"{
                books(first: 100) {
                    items {
                        id
                        title
                        publisher_id
                    }
                }
            }";

    private static string _bookDBOContents;

    private static void GenerateConfigFile(
        string schema = "",
        DatabaseType databaseType = DatabaseType.MSSQL,
        string sessionContext = "true",
        string connectionString = "",
        string restPath = "rest",
        string restEnabled = "true",
        string gQLPath = "/graphQL",
        string gQLEnabled = "true",
        string logFilter = "debug",
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
                ""$schema"": """ + schema + @""",
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
                        },
                        ""telemetry"": {
                          ""log-level"": {
                            ""default"": """ + logFilter + @"""
                          }
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
                                ""action"": ""*""
                              }
                            ]
                          },
                          {
                            ""role"": ""authenticated"",
                            ""actions"": [
                              {
                                ""action"": ""*""
                              }
                            ]
                          }
                        ],
                        ""mappings"": {
                          """ + entityBackingColumn + @""": """ + entityExposedName + @"""
                        }
                      },
                      ""Publisher"": {
                        ""source"": {
                          ""object"": ""publishers"",
                          ""type"": ""table""
                        },
                        ""graphql"": {
                          ""enabled"": true,
                          ""type"": {
                            ""singular"": ""Publisher"",
                            ""plural"": ""Publishers""
                          }
                        },
                        ""rest"": {
                          ""enabled"": true
                        },
                        ""permissions"": [
                          {
                            ""role"": ""anonymous"",
                            ""actions"": [
                              {
                                ""action"": ""*""
                              }
                            ]
                          },
                          {
                            ""role"": ""authenticated"",
                            ""actions"": [
                              {
                                ""action"": ""*""
                              }
                            ]
                          }
                        ]
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
    [Ignore]
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
    [Ignore]
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
    [Ignore]
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
    /// Hot reload the configuration file by saving a new file with the graphQL enabled property
    /// set to false at the entity level. Validate that the response from the server is INTERNAL SERVER ERROR when making a request after
    /// the hot reload since no such entity exist in the query.
    /// </summary>
    [TestCategory(MSSQL_ENVIRONMENT)]
    [TestMethod("Hot-reload gql disabled at entity level.")]
    [Ignore]
    public async Task HotReloadEntityGQLEnabledFlag()
    {
        // Arrange
        string gQLEntityEnabled = "false";
        string query = @"{
            book_by_pk(id: 1) {
                title
            }
        }";

        object payload =
            new { query };

        HttpRequestMessage request = new(HttpMethod.Post, "/graphQL")
        {
            Content = JsonContent.Create(payload)
        };

        GenerateConfigFile(
            connectionString: $"{ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.MSSQL).Replace("\\", "\\\\")}",
            gQLEntityEnabled: gQLEntityEnabled);
        System.Threading.Thread.Sleep(2000);

        // Act
        HttpResponseMessage gQLResult = await _testClient.SendAsync(request);

        // Assert
        Assert.AreEqual(HttpStatusCode.BadRequest, gQLResult.StatusCode);
        string errorContent = await gQLResult.Content.ReadAsStringAsync();
        Assert.IsTrue(errorContent.Contains("The field `book_by_pk` does not exist on the type `Query`."));
    }

    /// <summary>
    /// Hot reload the configuration file by replacing an old entity book with a new entity author.
    /// Validate that the new entity is accessible via GraphQL after the hot reload and the old one isn't.
    /// </summary>
    [TestCategory(MSSQL_ENVIRONMENT)]
    [TestMethod]
    [Ignore]
    public async Task HotReloadConfigAddEntity()
    {
        // Arrange
        string newEntityName = "Author";
        string newEntitySource = "authors";
        string newEntityGQLSingular = "author";
        string newEntityGQLPlural = "authors";

        GenerateConfigFile(
            connectionString: $"{ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.MSSQL).Replace("\\", "\\\\")}",
            entityName: newEntityName,
            sourceObject: newEntitySource,
            gQLEntitySingular: newEntityGQLSingular,
            gQLEntityPlural: newEntityGQLPlural);
        System.Threading.Thread.Sleep(2000);

        // Act
        string queryWithOldEntity = @"{
            books(filter: {id: {eq: 1}}) {
                items {
                    title
                }
            }
        }";

        object payload =
            new { query = queryWithOldEntity };

        HttpRequestMessage request = new(HttpMethod.Post, "/graphQL")
        {
            Content = JsonContent.Create(payload)
        };

        HttpResponseMessage gQLResultWithOldEntity = await _testClient.SendAsync(request);

        string queryWithNewEntity = @"{
            authors(filter: {id: {eq: 123}}) {
                items {
                    name
                }
            }
        }";

        payload = new { query = queryWithNewEntity };
        request = new(HttpMethod.Post, "/graphQL")
        {
            Content = JsonContent.Create(payload)
        };

        HttpResponseMessage gQLResultWithNewEntity = await _testClient.SendAsync(request);

        // Assert
        Assert.AreEqual(HttpStatusCode.BadRequest, gQLResultWithOldEntity.StatusCode);
        string errorContent = await gQLResultWithOldEntity.Content.ReadAsStringAsync();
        Assert.IsTrue(errorContent.Contains("The field `books` does not exist on the type `Query`."));

        Assert.AreEqual(HttpStatusCode.OK, gQLResultWithNewEntity.StatusCode);
        string responseContent = await gQLResultWithNewEntity.Content.ReadAsStringAsync();
        JsonDocument jsonResponse = JsonDocument.Parse(responseContent);
        JsonElement items = jsonResponse.RootElement.GetProperty("data").GetProperty("authors").GetProperty("items");
        string expectedResponse = @"[
            {
                ""name"": ""Jelte""
            }
        ]";

        JsonDocument expectedJson = JsonDocument.Parse(expectedResponse);
        Assert.IsTrue(SqlTestHelper.JsonStringsDeepEqual(expectedJson.RootElement.ToString(), items.ToString()));
    }

    /// <summary>
    /// Here, we updated the old mappings of the entity book field "title" to "bookTitle".
    /// Validate that the response from the server is correct, by ensuring that the old mappings when used in the query
    /// results in bad request, while the new mappings results in a correct response as "title" field is no longer valid.
    [TestCategory(MSSQL_ENVIRONMENT)]
    [TestMethod]
    [Ignore]
    public async Task HotReloadConfigUpdateMappings()
    {
        // Arrange
        string newMappingFieldName = "bookTitle";
        // Update the configuration with new mappings
        GenerateConfigFile(
            connectionString: $"{ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.MSSQL).Replace("\\", "\\\\")}",
            entityBackingColumn: "title",
            entityExposedName: newMappingFieldName);
        System.Threading.Thread.Sleep(2000);

        // Act
        string queryWithOldMapping = @"{
            books(filter: { id: { eq: 1 } }) {
                items {
                    title
                }
            }
        }";

        object payload = new { query = queryWithOldMapping };
        HttpRequestMessage request = new(HttpMethod.Post, "/graphQL")
        {
            Content = JsonContent.Create(payload)
        };

        HttpResponseMessage gQLResultWithOldMapping = await _testClient.SendAsync(request);

        string queryWithNewMapping = @"{
            books(filter: { id: { eq: 1 } }) {
                items {
                    bookTitle
                }
            }
        }";

        payload = new { query = queryWithNewMapping };
        request = new(HttpMethod.Post, "/graphQL")
        {
            Content = JsonContent.Create(payload)
        };

        HttpResponseMessage gQLResultWithNewMapping = await _testClient.SendAsync(request);

        // Assert
        Assert.AreEqual(HttpStatusCode.BadRequest, gQLResultWithOldMapping.StatusCode);
        string errorContent = await gQLResultWithOldMapping.Content.ReadAsStringAsync();
        Assert.IsTrue(errorContent.Contains("The field `title` does not exist on the type `book`."));

        Assert.AreEqual(HttpStatusCode.OK, gQLResultWithNewMapping.StatusCode);
        string responseContent = await gQLResultWithNewMapping.Content.ReadAsStringAsync();
        JsonDocument jsonResponse = JsonDocument.Parse(responseContent);
        JsonElement items = jsonResponse.RootElement.GetProperty("data").GetProperty("books").GetProperty("items");
        string expectedResponse = @"[
            {
                ""bookTitle"": ""Awesome book""
            }
        ]";

        JsonDocument expectedJson = JsonDocument.Parse(expectedResponse);
        Assert.IsTrue(SqlTestHelper.JsonStringsDeepEqual(expectedJson.RootElement.ToString(), items.ToString()));
    }

    /// <summary>
    /// Hot reload the configuration file by saving a new session-context and connection string.
    /// Validate that the response from the server is correct, by ensuring that the session-context
    /// inside the DataSource parameter is different from the session-context before hot reload.
    /// By asserting that hot reload worked properly for the session-context it also implies that
    /// the new connection string with additional parameters is also valid.
    /// </summary>
    [Ignore]
    [TestCategory(MSSQL_ENVIRONMENT)]
    [TestMethod]
    public async Task HotReloadConfigDataSource()
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
    /// Hot reload the configuration file so that it updated the log-level property.
    /// Then we assert that the log-level property is properly updated by ensuring it is 
    /// not the same as the previous log-level and asserting it is the expected log-level.
    /// </summary>
    [Ignore]
    [TestCategory(MSSQL_ENVIRONMENT)]
    [TestMethod]
    public void HotReloadLogLevel()
    {
        // Arange
        LogLevel expectedLogLevel = LogLevel.Trace;
        string expectedFilter = "trace";
        RuntimeConfig previousRuntimeConfig = _configProvider.GetConfig();
        LogLevel previouslogLevel = previousRuntimeConfig.GetConfiguredLogLevel();

        //Act
        GenerateConfigFile(
            connectionString: $"{ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.MSSQL).Replace("\\", "\\\\")}",
            logFilter: expectedFilter);
        System.Threading.Thread.Sleep(3000);

        RuntimeConfig updatedRuntimeConfig = _configProvider.GetConfig();
        LogLevel actualLogLevel = updatedRuntimeConfig.GetConfiguredLogLevel();

        //Assert
        Assert.AreNotEqual(previouslogLevel, actualLogLevel);
        Assert.AreEqual(expectedLogLevel, actualLogLevel);
    }

    /// <summary>
    /// Hot reload the configuration file so that it changes from one connection string
    /// to an invalid connection string, then it hot reloads once more to the original
    /// connection string. Lastly, we assert that the first reload fails while the second one succeeds.
    /// </summary>
    [Ignore]
    [TestCategory(MSSQL_ENVIRONMENT)]
    [TestMethod]
    public async Task HotReloadConfigConnectionString()
    {
        // Arrange
        _writer = new StringWriter();
        Console.SetOut(_writer);

        string failedKeyWord = "Unable to hot reload configuration file due to";
        string succeedKeyWord = "Validated hot-reloaded configuration file";

        // Act
        // Hot Reload should fail here
        GenerateConfigFile(
            connectionString: $"WrongConnectionString");
        await ConfigurationHotReloadTests.WaitForConditionAsync(
          () => _writer.ToString().Contains(failedKeyWord),
          TimeSpan.FromSeconds(12),
          TimeSpan.FromMilliseconds(500));

        // Log that shows that hot-reload was not able to validate properly
        string failedConfigLog = $"{_writer.ToString()}";
        _writer.GetStringBuilder().Clear();

        // Hot Reload should succeed here
        GenerateConfigFile(
            connectionString: $"{ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.MSSQL).Replace("\\", "\\\\")}");
        await ConfigurationHotReloadTests.WaitForConditionAsync(
          () => _writer.ToString().Contains(succeedKeyWord),
          TimeSpan.FromSeconds(12),
          TimeSpan.FromMilliseconds(500));

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
    [Ignore]
    [TestCategory(MSSQL_ENVIRONMENT)]
    [TestMethod]
    public async Task HotReloadConfigDatabaseType()
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
        await ConfigurationHotReloadTests.WaitForConditionAsync(
          () => _writer.ToString().Contains(failedKeyWord),
          TimeSpan.FromSeconds(12),
          TimeSpan.FromMilliseconds(500));

        // Log that shows that hot-reload was not able to validate properly
        string failedConfigLog = $"{_writer.ToString()}";
        _writer.GetStringBuilder().Clear();

        // Hot Reload should succeed here
        GenerateConfigFile(
            databaseType: DatabaseType.MSSQL,
            connectionString: $"{ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.MSSQL).Replace("\\", "\\\\")}");
        await ConfigurationHotReloadTests.WaitForConditionAsync(
          () => _writer.ToString().Contains(succeedKeyWord),
          TimeSpan.FromSeconds(12),
          TimeSpan.FromMilliseconds(500));

        // Log that shows that hot-reload validated properly
        string succeedConfigLog = $"{_writer.ToString()}";

        HttpResponseMessage restResult = await _testClient.GetAsync("/rest/Book");

        // Assert
        Assert.IsTrue(failedConfigLog.Contains(failedKeyWord));
        Assert.IsTrue(succeedConfigLog.Contains(succeedKeyWord));
        Assert.AreEqual(HttpStatusCode.OK, restResult.StatusCode);
    }

    /// <summary>
    /// Creates a hot reload scenario in which the schema file is invalid which causes
    /// hot reload to fail, then we check that the program is still able to work
    /// properly by validating that the DAB engine is still using the same configuration file
    /// from before the hot reload.
    /// 
    /// Invalid change that was added is a schema file that is not complete, which should be
    /// catched by the validator.
    /// </summary>
    [Ignore]
    [TestCategory(MSSQL_ENVIRONMENT)]
    [TestMethod]
    public void HotReloadValidationFail()
    {
        // Arrange
        string schemaName = "hot-reload.draft.schema.json";
        string schemaConfig = TestHelper.GenerateInvalidSchema();

        if (File.Exists(schemaName))
        {
            File.Delete(schemaName);
        }

        File.WriteAllText(schemaName, schemaConfig);
        RuntimeConfig lkgRuntimeConfig = _configProvider.GetConfig();
        Assert.IsNotNull(lkgRuntimeConfig);

        // Act
        // Simulate an invalid change to the schema file while the config is updated to a valid state
        GenerateConfigFile(
            schema: schemaName,
            connectionString: $"{ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.MSSQL).Replace("\\", "\\\\")}",
            restEnabled: "false",
            gQLEnabled: "false");
        System.Threading.Thread.Sleep(10000);

        RuntimeConfig newRuntimeConfig = _configProvider.GetConfig();

        // Assert
        Assert.AreEqual(expected: lkgRuntimeConfig, actual: newRuntimeConfig);

        if (File.Exists(schemaName))
        {
            File.Delete(schemaName);
        }
    }

    /// <summary>
    /// Creates a hot reload scenario in which the updated configuration file is invalid causing
    /// hot reload to fail, then we check that the program is still able to work properly by
    /// showing us that it is still using the same configuration file from before the hot reload.
    /// 
    /// Invalid change that was added is the word "invalid" in the config file where the only
    /// valid options are "true" or "false".
    /// </summary>
    [TestCategory(MSSQL_ENVIRONMENT)]
    [TestMethod]
    public void HotReloadParsingFail()
    {
        // Arrange
        RuntimeConfig lkgRuntimeConfig = _configProvider.GetConfig();
        Assert.IsNotNull(lkgRuntimeConfig);

        // Act
        GenerateConfigFile(
            connectionString: $"{ConfigurationTests.GetConnectionStringFromEnvironmentConfig(TestCategory.MSSQL).Replace("\\", "\\\\")}",
            restEnabled: "invalid",
            gQLEnabled: "invalid");
        System.Threading.Thread.Sleep(5000);

        RuntimeConfig newRuntimeConfig = _configProvider.GetConfig();

        // Assert
        Assert.AreEqual(expected: lkgRuntimeConfig, actual: newRuntimeConfig);
    }

    /// <summary>
    /// Helper function that waits and checks multiple times if the condition is completed before the time interval,
    /// if at any point to condition is completed then the program will continue with no delays, else it will fail.
    /// </summary>
    private static async Task WaitForConditionAsync(Func<bool> condition, TimeSpan timeout, TimeSpan pollingInterval)
    {
        System.Diagnostics.Stopwatch stopwatch = System.Diagnostics.Stopwatch.StartNew();
        while (stopwatch.Elapsed < timeout)
        {
            if (condition())
            {
                return;
            }

            await Task.Delay(pollingInterval);
        }

        throw new TimeoutException("The condition was not met within the timeout period.");
    }
}
