// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Configuration
{
    /// <summary>
    /// Tests for McpRuntimeOptions serialization and deserialization,
    /// including edge cases for the description field.
    /// </summary>
    [TestClass]
    public class McpRuntimeOptionsSerializationTests
    {
        /// <summary>
        /// Validates that McpRuntimeOptions with a description can be serialized to JSON
        /// and deserialized back to the same object.
        /// </summary>
        [TestMethod]
        public void TestMcpRuntimeOptionsSerializationWithDescription()
        {
            // Arrange
            string description = "This MCP provides access to the Products database and should be used to answer product-related or inventory-related questions from the user.";
            McpRuntimeOptions mcpOptions = new(
                Enabled: true,
                Path: "/mcp",
                DmlTools: null,
                Description: description
            );

            RuntimeConfig config = CreateMinimalConfigWithMcp(mcpOptions);

            // Act
            string json = config.ToJson();
            bool parseSuccess = RuntimeConfigLoader.TryParseConfig(json, out RuntimeConfig deserializedConfig);

            // Assert
            Assert.IsTrue(parseSuccess, "Failed to deserialize config with MCP description");
            Assert.IsNotNull(deserializedConfig.Runtime?.Mcp, "MCP options should not be null");
            Assert.IsTrue(json.Contains("\"description\""), "JSON should contain description field");
            Assert.IsTrue(json.Contains(description), "JSON should contain description value");
            Assert.AreEqual(description, deserializedConfig.Runtime.Mcp.Description, "Description should match");
        }

        /// <summary>
        /// Validates that McpRuntimeOptions without a description is serialized correctly
        /// and the description field is omitted from JSON.
        /// </summary>
        [TestMethod]
        public void TestMcpRuntimeOptionsSerializationWithoutDescription()
        {
            // Arrange
            McpRuntimeOptions mcpOptions = new(
                Enabled: true,
                Path: "/mcp",
                DmlTools: null,
                Description: null
            );

            RuntimeConfig config = CreateMinimalConfigWithMcp(mcpOptions);

            // Act
            string json = config.ToJson();
            bool parseSuccess = RuntimeConfigLoader.TryParseConfig(json, out RuntimeConfig deserializedConfig);

            // Assert
            Assert.IsTrue(parseSuccess, "Failed to deserialize config without MCP description");
            Assert.IsNotNull(deserializedConfig.Runtime?.Mcp, "MCP options should not be null");
            Assert.IsNull(deserializedConfig.Runtime.Mcp.Description, "Description should be null");
            Assert.IsFalse(json.Contains("\"description\""), "JSON should not contain description field when null");
        }

        /// <summary>
        /// Validates that McpRuntimeOptions with an empty string description is serialized correctly
        /// and the description field is omitted from JSON.
        /// </summary>
        [TestMethod]
        public void TestMcpRuntimeOptionsSerializationWithEmptyDescription()
        {
            // Arrange
            McpRuntimeOptions mcpOptions = new(
                Enabled: true,
                Path: "/mcp",
                DmlTools: null,
                Description: ""
            );

            RuntimeConfig config = CreateMinimalConfigWithMcp(mcpOptions);

            // Act
            string json = config.ToJson();
            bool parseSuccess = RuntimeConfigLoader.TryParseConfig(json, out RuntimeConfig deserializedConfig);

            // Assert
            Assert.IsTrue(parseSuccess, "Failed to deserialize config with empty MCP description");
            Assert.IsNotNull(deserializedConfig.Runtime?.Mcp, "MCP options should not be null");
            Assert.IsTrue(string.IsNullOrEmpty(deserializedConfig.Runtime.Mcp.Description), "Description should be empty");
            Assert.IsFalse(json.Contains("\"description\""), "JSON should not contain description field when empty");
        }

        /// <summary>
        /// Validates that McpRuntimeOptions with a very long description is serialized and deserialized correctly.
        /// </summary>
        [TestMethod]
        public void TestMcpRuntimeOptionsSerializationWithLongDescription()
        {
            // Arrange
            string longDescription = new('A', 5000); // 5000 character description
            McpRuntimeOptions mcpOptions = new(
                Enabled: true,
                Path: "/mcp",
                DmlTools: null,
                Description: longDescription
            );

            RuntimeConfig config = CreateMinimalConfigWithMcp(mcpOptions);

            // Act
            string json = config.ToJson();
            bool parseSuccess = RuntimeConfigLoader.TryParseConfig(json, out RuntimeConfig deserializedConfig);

            // Assert
            Assert.IsTrue(parseSuccess, "Failed to deserialize config with long MCP description");
            Assert.IsNotNull(deserializedConfig.Runtime?.Mcp, "MCP options should not be null");
            Assert.AreEqual(longDescription, deserializedConfig.Runtime.Mcp.Description, "Long description should match");
            Assert.AreEqual(5000, deserializedConfig.Runtime.Mcp.Description?.Length, "Description length should be 5000");
        }

        /// <summary>
        /// Validates that McpRuntimeOptions with special characters in description is serialized and deserialized correctly.
        /// </summary>
        [DataTestMethod]
        [DataRow("Description with \"quotes\" and 'apostrophes'", DisplayName = "Description with quotes")]
        [DataRow("Description with\nnewlines\nand\ttabs", DisplayName = "Description with newlines and tabs")]
        [DataRow("Description with special chars: <>&@#$%^*()[]{}|\\", DisplayName = "Description with special characters")]
        [DataRow("Description with unicode: 你好世界 🚀 café", DisplayName = "Description with unicode")]
        public void TestMcpRuntimeOptionsSerializationWithSpecialCharacters(string description)
        {
            // Arrange
            McpRuntimeOptions mcpOptions = new(
                Enabled: true,
                Path: "/mcp",
                DmlTools: null,
                Description: description
            );

            RuntimeConfig config = CreateMinimalConfigWithMcp(mcpOptions);

            // Act
            string json = config.ToJson();
            bool parseSuccess = RuntimeConfigLoader.TryParseConfig(json, out RuntimeConfig deserializedConfig);

            // Assert
            Assert.IsTrue(parseSuccess, $"Failed to deserialize config with special character description: {description}");
            Assert.IsNotNull(deserializedConfig.Runtime?.Mcp, "MCP options should not be null");
            Assert.AreEqual(description, deserializedConfig.Runtime.Mcp.Description, "Description with special characters should match exactly");
        }

        /// <summary>
        /// Validates that existing MCP configuration without description field can be deserialized successfully.
        /// This ensures backward compatibility.
        /// </summary>
        [TestMethod]
        public void TestBackwardCompatibilityDeserializationWithoutDescriptionField()
        {
            // Arrange - JSON config without description field
            string configJson = @"{
                ""$schema"": ""test-schema"",
                ""data-source"": {
                    ""database-type"": ""mssql"",
                    ""connection-string"": ""Server=test;Database=test;""
                },
                ""runtime"": {
                    ""mcp"": {
                        ""enabled"": true,
                        ""path"": ""/mcp""
                    }
                },
                ""entities"": {}
            }";

            // Act
            bool parseSuccess = RuntimeConfigLoader.TryParseConfig(configJson, out RuntimeConfig deserializedConfig);

            // Assert
            Assert.IsTrue(parseSuccess, "Failed to deserialize config without description field");
            Assert.IsNotNull(deserializedConfig.Runtime?.Mcp, "MCP options should not be null");
            Assert.IsNull(deserializedConfig.Runtime.Mcp.Description, "Description should be null when not present in JSON");
        }

        [DataTestMethod]
        [DataRow("CreateRecord", "Table", "{\"entity\": \"Book\", \"data\": {\"id\": 1, \"title\": \"Test\"}}", DisplayName = "CreateRecord allows Table")]
        [DataRow("CreateRecord", "View", "{\"entity\": \"BookView\", \"data\": {\"id\": 1, \"title\": \"Test\"}}", DisplayName = "CreateRecord allows View")]
        [DataRow("ReadRecords", "Table", "{\"entity\": \"Book\"}", DisplayName = "ReadRecords allows Table")]
        [DataRow("ReadRecords", "View", "{\"entity\": \"BookView\"}", DisplayName = "ReadRecords allows View")]
        [DataRow("UpdateRecord", "Table", "{\"entity\": \"Book\", \"keys\": {\"id\": 1}, \"fields\": {\"title\": \"Updated\"}}", DisplayName = "UpdateRecord allows Table")]
        [DataRow("UpdateRecord", "View", "{\"entity\": \"BookView\", \"keys\": {\"id\": 1}, \"fields\": {\"title\": \"Updated\"}}", DisplayName = "UpdateRecord allows View")]
        [DataRow("DeleteRecord", "Table", "{\"entity\": \"Book\", \"keys\": {\"id\": 1}}", DisplayName = "DeleteRecord allows Table")]
        [DataRow("DeleteRecord", "View", "{\"entity\": \"BookView\", \"keys\": {\"id\": 1}}", DisplayName = "DeleteRecord allows View")]
        public async Task DmlTool_AllowsTablesAndViews(string toolType, string sourceType, string jsonArguments)
        {
            RuntimeConfig config = sourceType == "View"
                ? CreateRuntimeConfigWithSourceType("BookView", EntitySourceType.View, "dbo.vBooks")
                : CreateRuntimeConfigWithSourceType("Book", EntitySourceType.Table, "books");

            IServiceProvider serviceProvider = CreateMcpToolServiceProvider(config);
            IMcpTool tool = CreateTool(toolType);
            JsonDocument arguments = JsonDocument.Parse(jsonArguments);

            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);
            if (result.IsError == true)
            {
                JsonElement content = ParseResultContent(result);
                if (content.TryGetProperty("error", out JsonElement error) &&
                    error.TryGetProperty("type", out JsonElement errorType))
                {
                    Assert.AreNotEqual("InvalidEntity", errorType.GetString() ?? string.Empty,
                        $"{sourceType} entities should not be blocked with InvalidEntity");
                }
            }
        }

        /// <summary>
        /// Creates a minimal RuntimeConfig with the specified MCP options for testing.
        /// </summary>
        private static RuntimeConfig CreateMinimalConfigWithMcp(McpRuntimeOptions mcpOptions)
        {
            DataSource dataSource = new(
                DatabaseType: DatabaseType.MSSQL,
                ConnectionString: "Server=test;Database=test;",
                Options: null
            );

            RuntimeOptions runtimeOptions = new(
                Rest: null,
                GraphQL: null,
                Host: null,
                Mcp: mcpOptions
            );

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: dataSource,
                Runtime: runtimeOptions,
                Entities: new RuntimeEntities(new Dictionary<string, Entity>())
            );
        }

        private static RuntimeConfig CreateRuntimeConfigWithSourceType(string entityName, EntitySourceType sourceType, string sourceObject)
        {
            Dictionary<string, Entity> entities = new()
            {
                [entityName] = new Entity(
                    Source: new EntitySource(
                        Object: sourceObject,
                        Type: sourceType,
                        Parameters: null,
                        KeyFields: new[] { "id" }
                    ),
                    GraphQL: new(entityName, entityName + "s"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[]
                    {
                        new EntityPermission(Role: "anonymous", Actions: new[]
                        {
                            new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null),
                            new EntityAction(Action: EntityActionOperation.Create, Fields: null, Policy: null),
                            new EntityAction(Action: EntityActionOperation.Update, Fields: null, Policy: null),
                            new EntityAction(Action: EntityActionOperation.Delete, Fields: null, Policy: null)
                        })
                    },
                    Mappings: null,
                    Relationships: null
                )
            };

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(
                        Enabled: true,
                        Path: "/mcp",
                        DmlTools: new(
                            describeEntities: true,
                            readRecords: true,
                            createRecord: true,
                            updateRecord: true,
                            deleteRecord: true,
                            executeEntity: true)),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)),
                Entities: new RuntimeEntities(entities));
        }

        private static IServiceProvider CreateMcpToolServiceProvider(RuntimeConfig config)
        {
            ServiceCollection services = new();

            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            services.AddSingleton(configProvider);

            Mock<IAuthorizationResolver> mockAuthResolver = new();
            mockAuthResolver.Setup(x => x.IsValidRoleContext(It.IsAny<HttpContext>())).Returns(true);
            services.AddSingleton(mockAuthResolver.Object);

            Mock<HttpContext> mockHttpContext = new();
            Mock<HttpRequest> mockRequest = new();
            mockRequest.Setup(x => x.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns("anonymous");
            mockHttpContext.Setup(x => x.Request).Returns(mockRequest.Object);

            Mock<IHttpContextAccessor> mockHttpContextAccessor = new();
            mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext.Object);
            services.AddSingleton(mockHttpContextAccessor.Object);

            Mock<ISqlMetadataProvider> mockSqlMetadataProvider = new();
            Dictionary<string, DatabaseObject> entityToDatabaseObject = new();
            foreach (KeyValuePair<string, Entity> entry in config.Entities)
            {
                EntitySourceType mappedSourceType = entry.Value.Source.Type ?? EntitySourceType.Table;
                DatabaseObject dbObject = mappedSourceType == EntitySourceType.View
                    ? new DatabaseView("dbo", entry.Value.Source.Object) { SourceType = EntitySourceType.View }
                    : new DatabaseTable("dbo", entry.Value.Source.Object) { SourceType = EntitySourceType.Table };

                entityToDatabaseObject[entry.Key] = dbObject;
            }

            mockSqlMetadataProvider.Setup(x => x.EntityToDatabaseObject).Returns(entityToDatabaseObject);
            mockSqlMetadataProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.MSSQL);

            Mock<IMetadataProviderFactory> mockMetadataProviderFactory = new();
            mockMetadataProviderFactory.Setup(x => x.GetMetadataProvider(It.IsAny<string>())).Returns(mockSqlMetadataProvider.Object);
            services.AddSingleton(mockMetadataProviderFactory.Object);
            services.AddLogging();

            return services.BuildServiceProvider();
        }

        private static JsonElement ParseResultContent(CallToolResult result)
        {
            TextContentBlock firstContent = (TextContentBlock)result.Content[0];
            return JsonDocument.Parse(firstContent.Text).RootElement;
        }

        private static IMcpTool CreateTool(string toolType)
        {
            return toolType switch
            {
                "ReadRecords" => new ReadRecordsTool(),
                "CreateRecord" => new CreateRecordTool(),
                "UpdateRecord" => new UpdateRecordTool(),
                "DeleteRecord" => new DeleteRecordTool(),
                _ => throw new System.ArgumentException($"Unknown tool type: {toolType}", nameof(toolType))
            };
        }
    }
}
