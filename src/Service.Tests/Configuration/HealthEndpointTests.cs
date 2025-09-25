// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions.TestingHelpers;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.HealthCheck;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Product;
using Azure.DataApiBuilder.Service.HealthCheck;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;

namespace Azure.DataApiBuilder.Service.Tests.Configuration
{
    [TestClass]
    public class HealthEndpointTests
    {
        private const string CUSTOM_CONFIG_FILENAME = "custom_config.json";
        private const string BASE_DAB_URL = "http://localhost:5000";

        [TestCleanup]
        public void CleanupAfterEachTest()
        {
            if (File.Exists(CUSTOM_CONFIG_FILENAME))
            {
                File.Delete(CUSTOM_CONFIG_FILENAME);
            }

            TestHelper.UnsetAllDABEnvironmentVariables();
        }

        /// <summary>
        /// Simulates a GET request to DAB's comprehensive health check endpoint ('/health') and validates the contents of the response.
        /// The expected format of the response is the comprehensive health check response.
        /// </summary>
        [TestMethod]
        [TestCategory(TestCategory.MSSQL)]
        [DataRow(true, true, true, true, true, true, true, true, DisplayName = "Validate Health Report all enabled.")]
        [DataRow(false, true, true, true, true, true, true, true, DisplayName = "Validate when Comprehensive Health Report is disabled")]
        [DataRow(true, true, true, false, true, true, true, true, DisplayName = "Validate Health Report when global MCP health is disabled")]
        [DataRow(true, true, true, true, false, true, true, true, DisplayName = "Validate Health Report when data-source health is disabled")]
        [DataRow(true, true, true, true, true, false, true, true, DisplayName = "Validate Health Report when entity health is disabled")]
        [DataRow(true, false, true, true, true, true, true, true, DisplayName = "Validate Health Report when global REST health is disabled")]
        [DataRow(true, true, false, true, true, true, true, true, DisplayName = "Validate Health Report when global GraphQL health is disabled")]
        [DataRow(true, true, true, true, true, true, false, true, DisplayName = "Validate Health Report when entity REST health is disabled")]
        [DataRow(true, true, true, true, true, true, true, false, DisplayName = "Validate Health Report when entity GraphQL health is disabled")]
        public async Task ComprehensiveHealthEndpoint_ValidateContents(
            bool enableGlobalHealth,
            bool enableGlobalRest,
            bool enableGlobalGraphql,
            bool enableGlobalMcp,
            bool enableDatasourceHealth,
            bool enableEntityHealth,
            bool enableEntityRest,
            bool enableEntityGraphQL)
        {
            // The body remains exactly the same except passing enableGlobalMcp
            RuntimeConfig runtimeConfig = SetupCustomConfigFile(
                enableGlobalHealth,
                enableGlobalRest,
                enableGlobalGraphql,
                enableGlobalMcp,
                enableDatasourceHealth,
                enableEntityHealth,
                enableEntityRest,
                enableEntityGraphQL);

            WriteToCustomConfigFile(runtimeConfig);

            string[] args = new[]
            {
                $"--ConfigFileName={CUSTOM_CONFIG_FILENAME}"
            };

            using (TestServer server = new(Program.CreateWebHostBuilder(args)))
            using (HttpClient client = server.CreateClient())
            {
                HttpRequestMessage healthRequest = new(HttpMethod.Get, $"{BASE_DAB_URL}/health");
                HttpResponseMessage response = await client.SendAsync(healthRequest);

                if (!enableGlobalHealth)
                {
                    Assert.AreEqual(expected: HttpStatusCode.NotFound, actual: response.StatusCode, message: "Received unexpected HTTP code from health check endpoint.");
                }
                else
                {
                    string responseBody = await response.Content.ReadAsStringAsync();
                    Dictionary<string, JsonElement> responseProperties = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(responseBody);
                    Assert.AreEqual(expected: HttpStatusCode.OK, actual: response.StatusCode, message: "Received unexpected HTTP code from health check endpoint.");

                    ValidateBasicDetailsHealthCheckResponse(responseProperties);
                    ValidateConfigurationDetailsHealthCheckResponse(responseProperties, enableGlobalRest, enableGlobalGraphql, enableGlobalMcp);
                    ValidateIfAttributePresentInResponse(responseProperties, enableDatasourceHealth, HealthCheckConstants.DATASOURCE);
                    ValidateIfAttributePresentInResponse(responseProperties, enableEntityHealth, HealthCheckConstants.ENDPOINT);
                    if (enableEntityHealth)
                    {
                        ValidateEntityRestAndGraphQLResponse(responseProperties, enableEntityRest, enableEntityGraphQL, enableGlobalRest, enableGlobalGraphql);
                    }
                }
            }
        }

        /// <summary>
        /// Simulates the function call to HttpUtilities.ExecuteRestQueryAsync.
        /// while setting up mock HTTP client to simulate the response from the server to send OK code.
        /// Validates the response to ensure no error message is received.
        /// </summary>
        [TestMethod]
        public async Task TestHealthCheckRestResponseAsync()
        {
            // Arrange
            RuntimeConfig runtimeConfig = SetupCustomConfigFile(true, true, true, true, true, true, true, true);
            HttpUtilities httpUtilities = SetupRestTest(runtimeConfig);

            // Act
            // Call the ExecuteRestQuery method with the mock HttpClient
            // Simulate a REST API call to the endpoint
            // Response should be null as error message is not expected to be returned
            string errorMessageFromRest = await httpUtilities.ExecuteRestQueryAsync(
                restUriSuffix: runtimeConfig.RestPath,
                entityName: runtimeConfig.Entities.First().Key,
                first: runtimeConfig.Entities.First().Value.Health.First,
                incomingRoleHeader: string.Empty,
                incomingRoleToken: string.Empty
            );

            // Assert
            // Validate the null response from the REST API call 
            Assert.IsNull(errorMessageFromRest);
        }

        /// <summary>
        /// Simulates the function call to HttpUtilities.ExecuteRestQueryAsync.
        /// while setting up mock HTTP client to simulate the response from the server to send BadRequest code.
        /// Validates the response to ensure error message is received.
        /// </summary>
        [TestMethod]
        public async Task TestFailureHealthCheckRestResponseAsync()
        {
            // Arrange
            RuntimeConfig runtimeConfig = SetupCustomConfigFile(true, true, true, true, true, true, true, true);
            HttpUtilities httpUtilities = SetupGraphQLTest(runtimeConfig, HttpStatusCode.BadRequest);

            // Act
            // Call the ExecuteRestQuery method with the mock HttpClient
            // Simulate a REST API call to the endpoint
            // Response should be null as error message is not expected to be returned
            string errorMessageFromRest = await httpUtilities.ExecuteRestQueryAsync(
                restUriSuffix: runtimeConfig.RestPath,
                entityName: runtimeConfig.Entities.First().Key,
                first: runtimeConfig.Entities.First().Value.Health.First,
                incomingRoleHeader: string.Empty,
                incomingRoleToken: string.Empty
            );

            // Assert
            Assert.IsNotNull(errorMessageFromRest);
        }

        /// <summary>
        /// Simulates the function call to HttpUtilities.ExecuteGraphQLQueryAsync.
        /// while setting up mock HTTP client to simulate the response from the server to send OK code.
        /// Validates the response to ensure no error message is received.
        /// </summary>
        [TestMethod]
        public async Task TestHealthCheckGraphQLResponseAsync()
        {
            // Arrange
            RuntimeConfig runtimeConfig = SetupCustomConfigFile(true, true, true, true, true, true, true, true);
            HttpUtilities httpUtilities = SetupGraphQLTest(runtimeConfig);

            // Act
            string errorMessageFromGraphQL = await httpUtilities.ExecuteGraphQLQueryAsync(
                graphqlUriSuffix: "/graphql",
                entityName: runtimeConfig.Entities.First().Key,
                entity: runtimeConfig.Entities.First().Value,
                incomingRoleHeader: string.Empty,
                incomingRoleToken: string.Empty);

            // Assert
            Assert.IsNull(errorMessageFromGraphQL);
        }

        /// <summary>
        /// Simulates the function call to HttpUtilities.ExecuteGraphQLQueryAsync.
        /// while setting up mock HTTP client to simulate the response from the server to send InternalServerError code.
        /// Validates the response to ensure error message is received.
        /// </summary>
        [TestMethod]
        public async Task TestFailureHealthCheckGraphQLResponseAsync()
        {
            // Arrange
            RuntimeConfig runtimeConfig = SetupCustomConfigFile(true, true, true, true, true, true, true, true);
            HttpUtilities httpUtilities = SetupGraphQLTest(runtimeConfig, HttpStatusCode.InternalServerError);

            // Act
            string errorMessageFromGraphQL = await httpUtilities.ExecuteGraphQLQueryAsync(
                graphqlUriSuffix: "/graphql",
                entityName: runtimeConfig.Entities.First().Key,
                entity: runtimeConfig.Entities.First().Value,
                incomingRoleHeader: string.Empty,
                incomingRoleToken: string.Empty);

            // Assert
            Assert.IsNotNull(errorMessageFromGraphQL);
        }

        /// <summary>
        /// Tests the serialization behavior of <see cref="RuntimeHealthCheckConfig"/> for the <see cref="RuntimeHealthCheckConfig.MaxQueryParallelism"/> property."
        /// </summary>
        /// <remarks>This test ensures that the JSON serialization behavior of <see
        /// cref="RuntimeHealthCheckConfig"/>  adheres to the expected behavior where default values are omitted from
        /// the output.</remarks>
        [TestMethod]
        public void MaxQueryParallelismSerializationDependsOnUserInput()
        {
            // Case 1: default value NOT explicitly provided => should NOT serialize
            RuntimeHealthCheckConfig configWithDefault = new(
                enabled: true,
                roles: null,
                cacheTtlSeconds: null,
                maxQueryParallelism: null // implicit default
            );

            Assert.IsFalse(configWithDefault.UserProvidedMaxQueryParallelism, "UserProvidedMaxQueryParallelism should be false for default value.");

            // Case 2: default value EXPLICITLY provided => should serialize
            RuntimeHealthCheckConfig configWithExplicitDefault = new(
                enabled: true,
                roles: null,
                cacheTtlSeconds: null,
                maxQueryParallelism: RuntimeHealthCheckConfig.DEFAULT_MAX_QUERY_PARALLELISM
            );

            Assert.IsTrue(configWithExplicitDefault.UserProvidedMaxQueryParallelism, "UserProvidedMaxQueryParallelism should be true for explicit default value.");

            // Case 3: non-default value => should serialize
            RuntimeHealthCheckConfig configWithCustomValue = new(
                enabled: true,
                roles: null,
                cacheTtlSeconds: null,
                maxQueryParallelism: RuntimeHealthCheckConfig.DEFAULT_MAX_QUERY_PARALLELISM + 1
            );

            Assert.IsTrue(configWithCustomValue.UserProvidedMaxQueryParallelism, "UserProvidedMaxQueryParallelism should be true for custom value.");
        }

        #region Helper Methods
        private static HttpUtilities SetupRestTest(RuntimeConfig runtimeConfig, HttpStatusCode httpStatusCode = HttpStatusCode.OK)
        {
            // Arrange
            // Create a mock entity map with a single entity for testing and load in RuntimeConfigProvider
            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(runtimeConfig.ToJson()));
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader);

            // Create a Mock of HttpMessageHandler
            Mock<HttpMessageHandler> mockHandler = new();
            // Mocking the handler to return a specific response for SendAsync
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req =>
                        req.Method == HttpMethod.Get
                        && req.RequestUri.Equals($"{BASE_DAB_URL}/{runtimeConfig.RestPath.Trim('/')}/{runtimeConfig.Entities.First().Key}?$first={runtimeConfig.Entities.First().Value.Health.First}")),
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(httpStatusCode)
                {
                    Content = new StringContent("{\"message\":\"Rest response\"}")
                });

            // Mocking IHttpClientFactory
            Mock<IHttpClientFactory> mockHttpClientFactory = new();
            mockHttpClientFactory.Setup(x => x.CreateClient("ContextConfiguredHealthCheckClient"))
                .Returns(new HttpClient(mockHandler.Object)
                {
                    BaseAddress = new Uri($"{BASE_DAB_URL}")
                });

            Mock<ILogger<HttpUtilities>> _logger = new();

            // Create the mock HttpContext to return the expected scheme and host
            // when the ConfigureApiRoute method is called.
            Mock<HttpContext> mockHttpContext = new();
            Mock<HttpRequest> mockHttpRequest = new();
            mockHttpRequest.Setup(r => r.Scheme).Returns("http");
            mockHttpRequest.Setup(r => r.Host).Returns(new HostString("localhost", 5000));
            mockHttpContext.Setup(c => c.Request).Returns(mockHttpRequest.Object);

            return new(
                _logger.Object,
                metadataProviderFactory.Object,
                provider,
                mockHttpClientFactory.Object);
        }

        private static HttpUtilities SetupGraphQLTest(RuntimeConfig runtimeConfig, HttpStatusCode httpStatusCode = HttpStatusCode.OK)
        {
            // Arrange
            // Create a mock entity map with a single entity for testing and load in RuntimeConfigProvider            
            MockFileSystem fileSystem = new();
            fileSystem.AddFile(FileSystemRuntimeConfigLoader.DEFAULT_CONFIG_FILE_NAME, new MockFileData(runtimeConfig.ToJson()));
            FileSystemRuntimeConfigLoader loader = new(fileSystem);
            RuntimeConfigProvider provider = new(loader);
            Mock<IMetadataProviderFactory> metadataProviderFactory = new();
            Mock<ISqlMetadataProvider> sqlMetadataProvider = new();

            // Setup the mock database object with a source definition
            SourceDefinition sourceDef = new();
            sourceDef.Columns.Add("id", new ColumnDefinition());
            sourceDef.Columns.Add("title", new ColumnDefinition());
            // Mock DB Object to return the source definition
            Mock<DatabaseObject> mockDbObject = new();
            mockDbObject.SetupGet(x => x.SourceDefinition).Returns(sourceDef);
            // Mocking the metadata provider to return the mock database object
            sqlMetadataProvider.Setup(x => x.GetDatabaseObjectByKey(runtimeConfig.Entities.First().Key)).Returns(mockDbObject.Object);
            metadataProviderFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(sqlMetadataProvider.Object);

            Mock<HttpMessageHandler> mockHandler = new();
            mockHandler.Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.Is<HttpRequestMessage>(req =>
                        req.Method == HttpMethod.Post &&
                        req.RequestUri == new Uri($"{BASE_DAB_URL}/graphql") &&
                        req.Content.ReadAsStringAsync().Result.Equals("{\"query\":\"{bookLists (first: 100) {items { id title }}}\"}")), // Use the correct GraphQL query format
                    ItExpr.IsAny<CancellationToken>())
                .ReturnsAsync(new HttpResponseMessage(httpStatusCode)
                {
                    Content = new StringContent("{\"errors\":[{\"message\":\"Internal Server Error\"}]}")
                });

            Mock<IHttpClientFactory> mockHttpClientFactory = new();
            mockHttpClientFactory.Setup(x => x.CreateClient("ContextConfiguredHealthCheckClient"))
                .Returns(new HttpClient(mockHandler.Object)
                {
                    BaseAddress = new Uri($"{BASE_DAB_URL}")
                });

            Mock<ILogger<HttpUtilities>> logger = new();

            return new(
                logger.Object,
                metadataProviderFactory.Object,
                provider,
                mockHttpClientFactory.Object);
        }

        private static void ValidateEntityRestAndGraphQLResponse(
            Dictionary<string, JsonElement> responseProperties,
            bool enableEntityRest,
            bool enableEntityGraphQL,
            bool enableGlobalRest,
            bool enableGlobalGraphQL)
        {
            bool hasRestTag = false, hasGraphQLTag = false;
            if (responseProperties.TryGetValue("checks", out JsonElement checksElement) && checksElement.ValueKind == JsonValueKind.Array)
            {
                checksElement.EnumerateArray().ToList().ForEach(entityCheck =>
                {
                    // Check if the 'tags' property exists and is of type array
                    if (entityCheck.TryGetProperty("tags", out JsonElement tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
                    {
                        hasRestTag = hasRestTag || tagsElement.EnumerateArray().Any(tag => tag.ToString() == HealthCheckConstants.REST);
                        hasGraphQLTag = hasGraphQLTag || tagsElement.EnumerateArray().Any(tag => tag.ToString() == HealthCheckConstants.GRAPHQL);
                    }
                });

                if (enableGlobalRest)
                {
                    // When both enableEntityRest and hasRestTag match the same value
                    Assert.AreEqual(enableEntityRest, hasRestTag);
                }
                else
                {
                    Assert.IsFalse(hasRestTag);
                }

                if (enableGlobalGraphQL)
                {
                    // When both enableEntityGraphQL and hasGraphQLTag match the same value
                    Assert.AreEqual(enableEntityGraphQL, hasGraphQLTag);
                }
                else
                {
                    Assert.IsFalse(hasGraphQLTag);
                }
            }
        }

        private static void ValidateIfAttributePresentInResponse(
            Dictionary<string, JsonElement> responseProperties,
            bool enableFlag,
            string checkString)
        {
            if (responseProperties.TryGetValue("checks", out JsonElement checksElement) && checksElement.ValueKind == JsonValueKind.Array)
            {
                bool checksTags = checksElement.EnumerateArray().Any(entityCheck =>
                {
                    if (entityCheck.TryGetProperty("tags", out JsonElement tagsElement) && tagsElement.ValueKind == JsonValueKind.Array)
                    {
                        return tagsElement.EnumerateArray().Any(tag => tag.ToString() == checkString);
                    }

                    return false;
                });

                Assert.AreEqual(enableFlag, checksTags);
            }
            else
            {
                Assert.Fail("Checks array is not present in the Comprehensive Health Check Report.");
            }
        }

        private static void ValidateConfigurationIsNotNull(Dictionary<string, JsonElement> configPropertyValues, string objectKey)
        {
            Assert.IsTrue(configPropertyValues.ContainsKey(objectKey), $"Expected {objectKey} to be present in the configuration object.");
            Assert.IsNotNull(configPropertyValues[objectKey], $"Expected {objectKey} to be non-null.");
        }

        private static void ValidateConfigurationIsCorrectFlag(Dictionary<string, JsonElement> configElement, string objectKey, bool enableFlag)
        {
            Assert.AreEqual(enableFlag, configElement[objectKey].GetBoolean(), $"Expected {objectKey} to be set to {enableFlag}.");
        }

        private static void ValidateConfigurationDetailsHealthCheckResponse(Dictionary<string, JsonElement> responseProperties, bool enableGlobalRest, bool enableGlobalGraphQL, bool enableGlobalMcp)
        {
            if (responseProperties.TryGetValue("configuration", out JsonElement configElement) && configElement.ValueKind == JsonValueKind.Object)
            {
                Dictionary<string, JsonElement> configPropertyValues = new();

                // Enumerate through the configProperty's object properties and add them to the dictionary
                foreach (JsonProperty property in configElement.EnumerateObject().ToList())
                {
                    configPropertyValues[property.Name] = property.Value;
                }

                ValidateConfigurationIsNotNull(configPropertyValues, "rest");
                ValidateConfigurationIsCorrectFlag(configPropertyValues, "rest", enableGlobalRest);
                ValidateConfigurationIsNotNull(configPropertyValues, "graphql");
                ValidateConfigurationIsCorrectFlag(configPropertyValues, "graphql", enableGlobalGraphQL);
                ValidateConfigurationIsNotNull(configPropertyValues, "mcp");
                ValidateConfigurationIsCorrectFlag(configPropertyValues, "mcp", enableGlobalMcp);
                ValidateConfigurationIsNotNull(configPropertyValues, "caching");
                ValidateConfigurationIsNotNull(configPropertyValues, "telemetry");
                ValidateConfigurationIsNotNull(configPropertyValues, "mode");
            }
            else
            {
                Assert.Fail("Missing 'configuration' object in Health Check Response.");
            }
        }

        public static void ValidateBasicDetailsHealthCheckResponse(Dictionary<string, JsonElement> responseProperties)
        {
            // Validate value of 'status' property in response.
            if (responseProperties.TryGetValue(key: "status", out JsonElement statusValue))
            {
                Assert.IsTrue(statusValue.ValueKind == JsonValueKind.String, "Unexpected or missing status value as string.");
            }
            else
            {
                Assert.Fail();
            }

            // Validate value of 'version' property in response.
            if (responseProperties.TryGetValue(key: BasicHealthCheck.DAB_VERSION_KEY, out JsonElement versionValue))
            {
                Assert.AreEqual(
                    expected: ProductInfo.GetProductVersion(),
                    actual: versionValue.ToString(),
                    message: "Unexpected or missing version value.");
            }
            else
            {
                Assert.Fail();
            }

            // Validate value of 'app-name' property in response.
            if (responseProperties.TryGetValue(key: BasicHealthCheck.DAB_APPNAME_KEY, out JsonElement appNameValue))
            {
                Assert.AreEqual(
                    expected: ProductInfo.GetDataApiBuilderUserAgent(),
                    actual: appNameValue.ToString(),
                    message: "Unexpected or missing DAB user agent string.");
            }
            else
            {
                Assert.Fail();
            }
        }

        private static RuntimeConfig SetupCustomConfigFile(bool enableGlobalHealth, bool enableGlobalRest, bool enableGlobalGraphql, bool enabledGlobalMcp, bool enableDatasourceHealth, bool enableEntityHealth, bool enableEntityRest, bool enableEntityGraphQL)
        {
            // At least one entity is required in the runtime config for the engine to start.
            // Even though this entity is not under test, it must be supplied enable successful
            // config file creation.
            Entity requiredEntity = new(
                Health: new(enabled: enableEntityHealth),
                Source: new("books", EntitySourceType.Table, null, null),
                Rest: new(Enabled: enableEntityRest),
                GraphQL: new("book", "bookLists", enableEntityGraphQL),
                Permissions: new[] { ConfigurationTests.GetMinimalPermissionConfig(AuthorizationResolver.ROLE_ANONYMOUS) },
                Relationships: null,
                Mappings: null);

            Dictionary<string, Entity> entityMap = new()
            {
                { "Book", requiredEntity }
            };

            return CreateRuntimeConfig(entityMap, enableGlobalRest, enableGlobalGraphql, enabledGlobalMcp, enableGlobalHealth, enableDatasourceHealth, HostMode.Development);
        }

        /// <summary>
        /// Helper function to write custom configuration file. with minimal REST/GraphQL global settings
        /// using the supplied entities.
        /// </summary>
        /// <param name="entityMap">Collection of entityName -> Entity object.</param>
        /// <param name="enableGlobalRest">flag to enable or disabled REST globally.</param>
        private static RuntimeConfig CreateRuntimeConfig(Dictionary<string, Entity> entityMap, bool enableGlobalRest = true, bool enableGlobalGraphql = true, bool enabledGlobalMcp = true, bool enableGlobalHealth = true, bool enableDatasourceHealth = true, HostMode hostMode = HostMode.Production)
        {
            DataSource dataSource = new(
                DatabaseType.MSSQL,
                ConfigurationTests.GetConnectionStringFromEnvironmentConfig(environment: TestCategory.MSSQL),
                Options: null,
                Health: new(enableDatasourceHealth));
            HostOptions hostOptions = new(Mode: hostMode, Cors: null, Authentication: new() { Provider = nameof(EasyAuthType.StaticWebApps) });

            RuntimeConfig runtimeConfig = new(
                Schema: string.Empty,
                DataSource: dataSource,
                Runtime: new(
                    Health: new(enabled: enableGlobalHealth),
                    Rest: new(Enabled: enableGlobalRest),
                    GraphQL: new(Enabled: enableGlobalGraphql),
                    Mcp: new(Enabled: enabledGlobalMcp),
                    Host: hostOptions
                ),
                Entities: new(entityMap));

            return runtimeConfig;
        }

        private static void WriteToCustomConfigFile(RuntimeConfig runtimeConfig)
        {
            File.WriteAllText(
                path: CUSTOM_CONFIG_FILENAME,
                contents: runtimeConfig.ToJson());
        }
    }
    #endregion
}
