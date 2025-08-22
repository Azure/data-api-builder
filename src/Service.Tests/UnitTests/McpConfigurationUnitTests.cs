// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable disable

using System.Collections.Generic;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Test class for MCP (Model Context Protocol) configuration validation.
    /// </summary>
    [TestClass]
    public class McpConfigurationUnitTests
    {
        /// <summary>
        /// Test that default MCP options are set correctly.
        /// </summary>
        [TestMethod]
        public void ValidateDefaultMcpOptionsConfiguration()
        {
            // Arrange & Act
            McpOptions defaultOptions = new();

            // Assert
            Assert.IsFalse(defaultOptions.Enabled, "MCP should be disabled by default");
            Assert.AreEqual("/mcp", defaultOptions.Path, "Default MCP path should be '/mcp'");
            Assert.AreEqual(McpProtocol.Http, defaultOptions.Protocol, "Default MCP protocol should be HTTP");
        }

        /// <summary>
        /// Test that MCP options can be configured with custom values.
        /// </summary>
        [TestMethod]
        public void ValidateCustomMcpOptionsConfiguration()
        {
            // Arrange & Act
            McpOptions customOptions = new(
                Enabled: true,
                Path: "/custom-mcp",
                Protocol: McpProtocol.Stdio
            );

            // Assert
            Assert.IsTrue(customOptions.Enabled, "MCP should be enabled when explicitly set");
            Assert.AreEqual("/custom-mcp", customOptions.Path, "Custom MCP path should be preserved");
            Assert.AreEqual(McpProtocol.Stdio, customOptions.Protocol, "Custom MCP protocol should be preserved");
        }

        /// <summary>
        /// Test that AI options contain MCP configuration correctly.
        /// </summary>
        [TestMethod]
        public void ValidateAiOptionsWithMcpConfiguration()
        {
            // Arrange
            McpOptions mcpOptions = new(Enabled: true, Path: "/test-mcp", Protocol: McpProtocol.Http);

            // Act
            AiOptions aiOptions = new(Mcp: mcpOptions);

            // Assert
            Assert.IsNotNull(aiOptions.Mcp, "AI options should contain MCP configuration");
            Assert.IsTrue(aiOptions.Mcp.Enabled, "MCP should be enabled in AI options");
            Assert.AreEqual("/test-mcp", aiOptions.Mcp.Path, "MCP path should match in AI options");
            Assert.AreEqual(McpProtocol.Http, aiOptions.Mcp.Protocol, "MCP protocol should match in AI options");
        }

        /// <summary>
        /// Test that RuntimeConfig correctly exposes MCP properties.
        /// </summary>
        [TestMethod]
        public void ValidateRuntimeConfigMcpProperties()
        {
            // Arrange
            McpOptions mcpOptions = new(Enabled: true, Path: "/ai-mcp", Protocol: McpProtocol.Stdio);
            AiOptions aiOptions = new(Mcp: mcpOptions);
            RuntimeOptions runtimeOptions = new(
                Rest: null,
                GraphQL: null,
                Host: null,
                Ai: aiOptions
            );

            DataSource dataSource = new(
                DatabaseType: DatabaseType.MSSQL,
                ConnectionString: "test-connection"
            );

            RuntimeEntities entities = new(new Dictionary<string, Entity>());

            // Act
            RuntimeConfig config = new(
                Schema: RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK,
                DataSource: dataSource,
                Entities: entities,
                Runtime: runtimeOptions
            );

            // Assert
            Assert.IsTrue(config.IsMcpEnabled, "RuntimeConfig should indicate MCP is enabled");
            Assert.AreEqual("/ai-mcp", config.McpPath, "RuntimeConfig should expose correct MCP path");
            Assert.AreEqual(McpProtocol.Stdio, config.McpProtocol, "RuntimeConfig should expose correct MCP protocol");
        }

        /// <summary>
        /// Test that RuntimeConfig defaults work correctly when MCP is not configured.
        /// </summary>
        [TestMethod]
        public void ValidateRuntimeConfigMcpDefaults()
        {
            // Arrange
            DataSource dataSource = new(
                DatabaseType: DatabaseType.MSSQL,
                ConnectionString: "test-connection"
            );

            RuntimeEntities entities = new(new Dictionary<string, Entity>());

            // Act - Create config without AI/MCP options
            RuntimeConfig config = new(
                Schema: RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK,
                DataSource: dataSource,
                Entities: entities,
                Runtime: null
            );

            // Assert
            Assert.IsFalse(config.IsMcpEnabled, "RuntimeConfig should indicate MCP is disabled by default");
            Assert.AreEqual("/mcp", config.McpPath, "RuntimeConfig should use default MCP path");
            Assert.AreEqual(McpProtocol.Http, config.McpProtocol, "RuntimeConfig should use default MCP protocol");
        }

        /// <summary>
        /// Test serialization and deserialization of MCP configuration.
        /// </summary>
        [TestMethod]
        public void ValidateMcpConfigurationSerialization()
        {
            // Arrange
            var originalAiOptions = new AiOptions(
                Mcp: new McpOptions(
                    Enabled: true,
                    Path: "/serialize-test",
                    Protocol: McpProtocol.Stdio
                )
            );

            // Act - Serialize and deserialize
            string jsonString = JsonSerializer.Serialize(originalAiOptions);
            AiOptions deserializedAiOptions = JsonSerializer.Deserialize<AiOptions>(jsonString);

            // Assert
            Assert.IsNotNull(deserializedAiOptions, "Deserialized AI options should not be null");
            Assert.IsNotNull(deserializedAiOptions.Mcp, "Deserialized MCP options should not be null");
            Assert.AreEqual(originalAiOptions.Mcp.Enabled, deserializedAiOptions.Mcp.Enabled, "MCP enabled should match after serialization");
            Assert.AreEqual(originalAiOptions.Mcp.Path, deserializedAiOptions.Mcp.Path, "MCP path should match after serialization");
            Assert.AreEqual(originalAiOptions.Mcp.Protocol, deserializedAiOptions.Mcp.Protocol, "MCP protocol should match after serialization");
        }
    }
}