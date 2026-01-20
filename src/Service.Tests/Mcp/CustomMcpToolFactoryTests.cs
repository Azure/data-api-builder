// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Minimal unit tests for CustomMcpToolFactory covering filtering and creation logic.
    /// Comprehensive tests will be added in subsequent PRs.
    /// </summary>
    [TestClass]
    public class CustomMcpToolFactoryTests
    {
        /// <summary>
        /// Test that CreateCustomTools returns empty collection when config is null.
        /// </summary>
        [TestMethod]
        public void CreateCustomTools_ReturnsEmptyCollection_WhenConfigIsNull()
        {
            // Act
            System.Collections.Generic.IEnumerable<Azure.DataApiBuilder.Mcp.Model.IMcpTool> tools = CustomMcpToolFactory.CreateCustomTools(null!, null);

            // Assert
            Assert.IsNotNull(tools);
            Assert.AreEqual(0, tools.Count());
        }

        /// <summary>
        /// Test that CreateCustomTools returns empty collection when no entities exist.
        /// </summary>
        [TestMethod]
        public void CreateCustomTools_ReturnsEmptyCollection_WhenNoEntities()
        {
            // Arrange
            RuntimeConfig config = CreateEmptyConfig();

            // Act
            System.Collections.Generic.IEnumerable<Azure.DataApiBuilder.Mcp.Model.IMcpTool> tools = CustomMcpToolFactory.CreateCustomTools(config, null);

            // Assert
            Assert.IsNotNull(tools);
            Assert.AreEqual(0, tools.Count());
        }

        /// <summary>
        /// Test that CreateCustomTools filters entities correctly for custom tools.
        /// Should only include stored procedures with custom-tool enabled.
        /// </summary>
        [TestMethod]
        public void CreateCustomTools_FiltersEntitiesCorrectly()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithMixedEntities();

            // Act
            System.Collections.Generic.IEnumerable<Azure.DataApiBuilder.Mcp.Model.IMcpTool> tools = CustomMcpToolFactory.CreateCustomTools(config, null);

            // Assert
            Assert.IsNotNull(tools);
            // Should only include GetBook (SP with custom-tool enabled)
            Assert.AreEqual(1, tools.Count());
            Assert.AreEqual("get_book", tools.First().GetToolMetadata().Name);
        }

        /// <summary>
        /// Test that CreateCustomTools generates correct metadata for tools.
        /// </summary>
        [TestMethod]
        public void CreateCustomTools_GeneratesCorrectMetadata()
        {
            // Arrange
            RuntimeConfig config = CreateConfigWithDescribedEntity();

            // Act
            System.Collections.Generic.IEnumerable<Azure.DataApiBuilder.Mcp.Model.IMcpTool> tools = CustomMcpToolFactory.CreateCustomTools(config, null);

            // Assert
            Assert.AreEqual(1, tools.Count());
            ModelContextProtocol.Protocol.Tool metadata = tools.First().GetToolMetadata();
            Assert.AreEqual("get_user", metadata.Name);
            Assert.AreEqual("Gets user by ID", metadata.Description);
        }

        #region Helper Methods

        private static RuntimeConfig CreateEmptyConfig()
        {
            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: null,
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(new Dictionary<string, Entity>())
            );
        }

        private static RuntimeConfig CreateConfigWithMixedEntities()
        {
            Dictionary<string, Entity> entities = new()
            {
                // Table entity - should be filtered out
                ["Book"] = new Entity(
                    Source: new("books", EntitySourceType.Table, null, null),
                    GraphQL: new("Book", "Books"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null) }) },
                    Mappings: null,
                    Relationships: null
                ),
                // SP without custom-tool enabled - should be filtered out
                ["CountBooks"] = new Entity(
                    Source: new("count_books", EntitySourceType.StoredProcedure, null, null),
                    GraphQL: new("CountBooks", "CountBooks"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null) }) },
                    Mappings: null,
                    Relationships: null,
                    Mcp: new EntityMcpOptions(customToolEnabled: false, dmlToolsEnabled: null)
                ),
                // SP with custom-tool enabled - should be included
                ["GetBook"] = new Entity(
                    Source: new("get_book", EntitySourceType.StoredProcedure, null, null),
                    GraphQL: new("GetBook", "GetBook"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null) }) },
                    Mappings: null,
                    Relationships: null,
                    Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: null)
                )
            };

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: null,
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(entities)
            );
        }

        private static RuntimeConfig CreateConfigWithDescribedEntity()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["GetUser"] = new Entity(
                    Source: new("get_user", EntitySourceType.StoredProcedure, null, null),
                    GraphQL: new("GetUser", "GetUser"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null) }) },
                    Mappings: null,
                    Relationships: null,
                    Description: "Gets user by ID",
                    Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: null)
                )
            };

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: null,
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(entities)
            );
        }

        #endregion
    }
}
