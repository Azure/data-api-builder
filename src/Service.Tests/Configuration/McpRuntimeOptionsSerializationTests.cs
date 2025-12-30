// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Text.Json;
using Azure.DataApiBuilder.Config;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

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
            bool parseSuccess = RuntimeConfigLoader.TryParseConfig(json, out RuntimeConfig? deserializedConfig);

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
            bool parseSuccess = RuntimeConfigLoader.TryParseConfig(json, out RuntimeConfig? deserializedConfig);

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
            bool parseSuccess = RuntimeConfigLoader.TryParseConfig(json, out RuntimeConfig? deserializedConfig);

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
            string longDescription = new string('A', 5000); // 5000 character description
            McpRuntimeOptions mcpOptions = new(
                Enabled: true,
                Path: "/mcp",
                DmlTools: null,
                Description: longDescription
            );

            RuntimeConfig config = CreateMinimalConfigWithMcp(mcpOptions);

            // Act
            string json = config.ToJson();
            bool parseSuccess = RuntimeConfigLoader.TryParseConfig(json, out RuntimeConfig? deserializedConfig);

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
            bool parseSuccess = RuntimeConfigLoader.TryParseConfig(json, out RuntimeConfig? deserializedConfig);

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
            bool parseSuccess = RuntimeConfigLoader.TryParseConfig(configJson, out RuntimeConfig? deserializedConfig);

            // Assert
            Assert.IsTrue(parseSuccess, "Failed to deserialize config without description field");
            Assert.IsNotNull(deserializedConfig.Runtime?.Mcp, "MCP options should not be null");
            Assert.IsNull(deserializedConfig.Runtime.Mcp.Description, "Description should be null when not present in JSON");
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
    }
}
