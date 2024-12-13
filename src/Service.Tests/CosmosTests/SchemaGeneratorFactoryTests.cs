// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.IO;
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
        /// Tests the ExportGraphQLFromCosmosDB method to ensure it generates a GraphQL schema successfully from Cosmos DB.
        /// </summary>
        [TestMethod]
        public async Task ExportGraphQLFromCosmosDB_GeneratesSchemaSuccessfully()
        {
            // Arrange: Set up test configuration, mocks, and test data.

            // Runtime configuration for the schema generation.
            RuntimeConfig runtimeConfig = new(
               Schema: "schema",
               DataSource: new DataSource(DatabaseType.CosmosDB_NoSQL, "dummy-connection-string", new()
               {
                   {"database", "dummy-database" },
                   {"container", "Container0" }
               }),
               Runtime: new(Rest: null, GraphQL: new(), Host: new(null, null)),
               Entities: new(new Dictionary<string, Entity>()
               {
                       {"Container1", new Entity(
                            Source: new("mydb1.container1", EntitySourceType.Table, null, null),
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

            Assert.IsTrue(schema.Contains("type Container0"));
            Assert.IsTrue(schema.Contains("type Container1"));
            Assert.IsTrue(schema.Contains("type Container2"));
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
