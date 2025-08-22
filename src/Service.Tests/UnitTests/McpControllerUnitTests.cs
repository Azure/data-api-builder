// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable disable

using System.Collections.Generic;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Controllers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Test class for MCP Controller functionality.
    /// </summary>
    [TestClass]
    public class McpControllerUnitTests
    {
        private Mock<ILogger<McpController>> _mockLogger;
        private Mock<RuntimeConfigProvider> _mockConfigProvider;

        [TestInitialize]
        public void Setup()
        {
            _mockLogger = new Mock<ILogger<McpController>>();
            _mockConfigProvider = new Mock<RuntimeConfigProvider>();
        }

        /// <summary>
        /// Test that ListTools returns 404 when MCP is disabled.
        /// </summary>
        [TestMethod]
        public void ListTools_ReturnNotFound_WhenMcpDisabled()
        {
            // Arrange
            var config = CreateTestConfig(mcpEnabled: false);
            _mockConfigProvider.Setup(x => x.GetConfig()).Returns(config);
            
            var controller = new McpController(_mockLogger.Object, _mockConfigProvider.Object);

            // Act
            var result = controller.ListTools();

            // Assert
            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
            var notFoundResult = result as NotFoundObjectResult;
            Assert.AreEqual("MCP endpoint is disabled", notFoundResult?.Value);
        }

        /// <summary>
        /// Test that ListTools returns 200 with tools when MCP is enabled.
        /// </summary>
        [TestMethod]
        public void ListTools_ReturnOk_WhenMcpEnabled()
        {
            // Arrange
            var config = CreateTestConfig(mcpEnabled: true);
            _mockConfigProvider.Setup(x => x.GetConfig()).Returns(config);
            
            var controller = new McpController(_mockLogger.Object, _mockConfigProvider.Object);

            // Act
            var result = controller.ListTools();

            // Assert
            Assert.IsInstanceOfType(result, typeof(OkObjectResult));
            var okResult = result as OkObjectResult;
            Assert.IsNotNull(okResult?.Value);
        }

        /// <summary>
        /// Test that CallTool returns 404 when MCP is disabled.
        /// </summary>
        [TestMethod]
        public void CallTool_ReturnNotFound_WhenMcpDisabled()
        {
            // Arrange
            var config = CreateTestConfig(mcpEnabled: false);
            _mockConfigProvider.Setup(x => x.GetConfig()).Returns(config);
            
            var controller = new McpController(_mockLogger.Object, _mockConfigProvider.Object);
            var request = new McpCallRequest { Name = "test_tool" };

            // Act
            var result = controller.CallTool(request);

            // Assert
            Assert.IsInstanceOfType(result, typeof(NotFoundObjectResult));
            var notFoundResult = result as NotFoundObjectResult;
            Assert.AreEqual("MCP endpoint is disabled", notFoundResult?.Value);
        }

        /// <summary>
        /// Test that CallTool returns 400 for invalid tool name.
        /// </summary>
        [TestMethod]
        public void CallTool_ReturnBadRequest_WhenToolNameInvalid()
        {
            // Arrange
            var config = CreateTestConfig(mcpEnabled: true);
            _mockConfigProvider.Setup(x => x.GetConfig()).Returns(config);
            
            var controller = new McpController(_mockLogger.Object, _mockConfigProvider.Object);
            var request = new McpCallRequest { Name = "invalid_tool_name_format" };

            // Act
            var result = controller.CallTool(request);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
        }

        /// <summary>
        /// Test that CallTool returns 400 for null request.
        /// </summary>
        [TestMethod]
        public void CallTool_ReturnBadRequest_WhenRequestIsNull()
        {
            // Arrange
            var config = CreateTestConfig(mcpEnabled: true);
            _mockConfigProvider.Setup(x => x.GetConfig()).Returns(config);
            
            var controller = new McpController(_mockLogger.Object, _mockConfigProvider.Object);

            // Act
            var result = controller.CallTool(null);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
        }

        /// <summary>
        /// Test that CallTool returns 400 for empty tool name.
        /// </summary>
        [TestMethod]
        public void CallTool_ReturnBadRequest_WhenToolNameEmpty()
        {
            // Arrange
            var config = CreateTestConfig(mcpEnabled: true);
            _mockConfigProvider.Setup(x => x.GetConfig()).Returns(config);
            
            var controller = new McpController(_mockLogger.Object, _mockConfigProvider.Object);
            var request = new McpCallRequest { Name = "" };

            // Act
            var result = controller.CallTool(request);

            // Assert
            Assert.IsInstanceOfType(result, typeof(BadRequestObjectResult));
        }

        /// <summary>
        /// Helper method to create a test RuntimeConfig.
        /// </summary>
        private static RuntimeConfig CreateTestConfig(bool mcpEnabled)
        {
            var mcpOptions = new McpOptions(Enabled: mcpEnabled);
            var aiOptions = new AiOptions(Mcp: mcpOptions);
            var runtimeOptions = new RuntimeOptions(
                Rest: null,
                GraphQL: null,
                Host: null,
                Ai: aiOptions
            );

            var dataSource = new DataSource(
                DatabaseType: DatabaseType.MSSQL,
                ConnectionString: "test-connection"
            );

            var entities = new RuntimeEntities(new Dictionary<string, Entity>
            {
                ["TestEntity"] = new Entity(
                    Source: new EntitySource(Object: "test_table", Type: EntitySourceType.Table, Parameters: null, KeyFields: null),
                    GraphQL: new EntityGraphQLOptions(Singular: "testEntity", Plural: "testEntities"),
                    Rest: new EntityRestOptions(Methods: new[] { SupportedHttpVerb.Get }),
                    Permissions: new[]
                    {
                        new EntityPermission(
                            Role: "anonymous",
                            Actions: new[]
                            {
                                new EntityAction(
                                    Action: EntityActionOperation.Read,
                                    Fields: null,
                                    Policy: null
                                )
                            }
                        )
                    },
                    Mappings: null,
                    Relationships: null
                )
            });

            return new RuntimeConfig(
                Schema: RuntimeConfig.DEFAULT_CONFIG_SCHEMA_LINK,
                DataSource: dataSource,
                Entities: entities,
                Runtime: runtimeOptions
            );
        }
    }
}