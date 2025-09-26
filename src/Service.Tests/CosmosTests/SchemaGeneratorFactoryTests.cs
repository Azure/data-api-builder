// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core.Serialization;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Generator;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.CosmosTests
{
    /// <summary>
    /// Contains unit tests for SchemaGeneratorFactory related to Cosmos DB NoSQL.
    /// </summary>
    [TestClass, TestCategory(TestCategory.COSMOSDBNOSQL)]
    public class SchemaGeneratorFactoryTests
    {
        /// <summary>
        /// Validates the schema generation process for a GraphQL API from Cosmos DB using various configurations.
        /// This test ensures the schema is generated successfully or throws expected exceptions in different scenarios.
        /// </summary>
        /// <param name="globalDatabase">
        /// The global database name configured in the runtime. If null or invalid, an exception may occur.
        /// </param>
        /// <param name="globalContainer">
        /// The global container name configured in the runtime. If null or invalid, an exception may occur.
        /// </param>
        /// <param name="entityLevelDatabase">
        /// The database name at the entity level (overrides global database if specified). If null, the global database is used.
        /// </param>
        /// <param name="entityLevelContainer">
        /// The container name at the entity level (overrides global container if specified). If null, the global container is used.
        /// </param>
        /// <param name="exceptionMessage">
        /// The expected exception message if the test is designed to fail. Null for tests expecting successful schema generation.
        /// </param>
        /// <param name="generatedContainerNames">
        /// A comma-separated list of container names expected to appear in the generated GraphQL schema.
        /// </param>
        /// <remarks>
        /// This test uses mocked dependencies to simulate interactions with Cosmos DB. 
        /// It validates scenarios involving global and entity-level database and container configurations.
        ///
        /// Entity Constants:
        /// - <c>Container0</c>: Represents an entity without a specific source database or container configured.
        /// - <c>Container1</c>: Represents an entity with a source defined at the entity level, overriding the global configuration.
        /// - <c>Container2</c>: Represents an entity with a hardcoded database and container (`mydb2.container2`).
        ///
        /// These constants are used to validate schema generation and ensure correct mapping between entities and their GraphQL types.
        /// </remarks>
        [TestMethod]
        [DataRow("noop-database", "Container0", "mydb1", "container1", null, "Container0,Container1,Container2", DisplayName = "Test with global and entity level database and container names")]
        [DataRow(null, "Container0", "mydb1", "container1", "Connection String and Database name must be provided in the config file", null, DisplayName = "Test with missing global database name")]
        [DataRow("noop-database", null, "mydb1", "container1", "Unexpected Source format", null, DisplayName = "Test with missing global container name")]
        [DataRow("noop-database", "Container0", null, "container1", null, "Container0,Container1,Container2", DisplayName = "Test with missing entity level database name")]
        [DataRow("noop-database", "Container0", "mydb1", null, null, "Container0,Container2", DisplayName = "Test with missing entity level container name")]
        public async Task ExportGraphQLFromCosmosDB_GeneratesSchemaSuccessfully(string globalDatabase, string globalContainer, string entityLevelDatabase, string entityLevelContainer, string exceptionMessage, string generatedContainerNames)
        {
            try
            {
                // Arrange: Set up test configuration, mocks, and test data.
                string entitySource = entityLevelContainer != null ? (entityLevelDatabase != null ? $"{entityLevelDatabase}.{entityLevelContainer}" : entityLevelContainer) : null;

                // Runtime configuration for the schema generation.
                RuntimeConfig runtimeConfig = new(
                   Schema: "schema",
                   DataSource: new DataSource(DatabaseType.CosmosDB_NoSQL, "noop-connection-string", new()
                   {
                   {"database", globalDatabase},
                   {"container", globalContainer}
                   }),
                   Runtime: new(Rest: null, GraphQL: new(), Mcp: new(), Host: new(null, null)),
                   Entities: new(new Dictionary<string, Entity>()
                   {
                       {"Container1", new Entity(
                            Source: new(entitySource, EntitySourceType.Table, null, null),
                            Rest: new(Enabled: false),
                            GraphQL: new("Container1", "Container1s"),
                            Permissions: new EntityPermission[] {},
                            Relationships: null,
                            Mappings: null) },
                       {"Container2", new Entity(
                            Source: new("mydb2.container2", EntitySourceType.Table, null, null),
                            Rest: new(Enabled: false),
                            GraphQL: new("Container2", "Container2s"),
                            Permissions: new EntityPermission[] {},
                            Relationships: null,
                            Mappings: null) },
                       {"Container0", new Entity(
                            Source: new(null, EntitySourceType.Table, null, null),
                            Rest: new(Enabled: false),
                            GraphQL: new("Container0", "Container0s"),
                            Permissions: new EntityPermission[] {},
                            Relationships: null,
                            Mappings: null) }
                   })
               );

                Mock<ILogger> mockLogger = new();

                Mock<Container> mockContainer = new();
                Mock<FeedIterator> mockIterator = new();
                mockContainer
                    .SetupSequence(c => c.GetItemQueryStreamIterator(It.IsAny<QueryDefinition>(), It.IsAny<string>(), It.IsAny<QueryRequestOptions>()))
                    .Returns(mockIterator.Object)
                    .Returns(mockIterator.Object)
                    .Returns(mockIterator.Object);

                mockIterator.SetupSequence(i => i.HasMoreResults)
                    .Returns(true)
                    .Returns(false)
                    .Returns(true)
                    .Returns(false)
                    .Returns(true)
                    .Returns(false);

                mockIterator
                    .SetupSequence(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(GetResponse())
                    .ReturnsAsync(GetResponse())
                    .ReturnsAsync(GetResponse());

                // Act: Generate the schema using the SchemaGeneratorFactory.
                string schema = await SchemaGeneratorFactory.Create(config: runtimeConfig,
                                                                    mode: SamplingModes.TopNExtractor.ToString(),
                                                                    sampleCount: null,
                                                                    partitionKeyPath: null,
                                                                    days: null,
                                                                    groupCount: null,
                                                                    logger: mockLogger.Object,
                                                                    container: mockContainer.Object);

                // Assert: Verify the schema generation is successful and contains the expected types.
                Assert.IsNotNull(schema);

                generatedContainerNames.Split(",")
                    .ToList()
                    .ForEach(containerName => Assert.IsTrue(schema.Contains($"type {containerName.Trim()}")));

            }
            catch (System.Exception ex)
            {
                if (exceptionMessage != null)
                {
                    Assert.IsTrue(ex.Message.Contains(exceptionMessage));
                    return;
                }

                throw;
            }

        }

        /// <summary>
        /// Creates a mock response message containing JSON documents.
        /// </summary>
        /// <returns>A mock response message with sample JSON content.</returns>
        private static ResponseMessage GetResponse()
        {
            ResponseMessage message = new(HttpStatusCode.OK);
            List<JsonDocument> jsonDocuments = new()
            {
                JsonDocument.Parse("{\"id\": \"1\", \"name\": \"Test\"}")
            };

            MemoryStream streamPayload = new();
            JsonObjectSerializer systemTextJsonSerializer = new();
            systemTextJsonSerializer.Serialize(streamPayload, jsonDocuments, jsonDocuments.GetType(), default);
            streamPayload.Position = 0;

            message.Content = streamPayload;

            return message;
        }
    }
}
