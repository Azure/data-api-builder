// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Linq;
using System.Text.Json;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Minimal unit tests for DynamicCustomTool covering critical functionality.
    /// Comprehensive tests will be added in subsequent PRs.
    /// </summary>
    [TestClass]
    public class DynamicCustomToolTests
    {
        /// <summary>
        /// Test that DynamicCustomTool correctly converts PascalCase entity names to snake_case tool names.
        /// </summary>
        [TestMethod]
        [DataRow("GetUserProfile", "get_user_profile")]
        [DataRow("GetBook", "get_book")]
        [DataRow("InsertBookRecord", "insert_book_record")]
        [DataRow("CountBooks", "count_books")]
        [DataRow("lowercase", "lowercase")]
        [DataRow("UPPERCASE", "uppercase")]
        public void GetToolMetadata_ConvertsEntityNameToSnakeCase(string entityName, string expectedToolName)
        {
            // Arrange
            Entity entity = CreateTestStoredProcedureEntity();
            DynamicCustomTool tool = new(entityName, entity);

            // Act
            ModelContextProtocol.Protocol.Tool metadata = tool.GetToolMetadata();

            // Assert
            Assert.AreEqual(expectedToolName, metadata.Name);
        }

        /// <summary>
        /// Test that tool metadata includes entity description when provided.
        /// </summary>
        [TestMethod]
        public void GetToolMetadata_UsesEntityDescription_WhenProvided()
        {
            // Arrange
            string description = "Retrieves a book by ID";
            Entity entity = CreateTestStoredProcedureEntity(description: description);
            DynamicCustomTool tool = new("GetBook", entity);

            // Act
            ModelContextProtocol.Protocol.Tool metadata = tool.GetToolMetadata();

            // Assert
            Assert.AreEqual(description, metadata.Description);
        }

        /// <summary>
        /// Test that tool metadata generates default description when not provided.
        /// </summary>
        [TestMethod]
        public void GetToolMetadata_GeneratesDefaultDescription_WhenNotProvided()
        {
            // Arrange
            Entity entity = CreateTestStoredProcedureEntity();
            DynamicCustomTool tool = new("GetBook", entity);

            // Act
            ModelContextProtocol.Protocol.Tool metadata = tool.GetToolMetadata();

            // Assert
            Assert.IsTrue(metadata.Description?.Contains("get_book") ?? false);
            Assert.IsTrue(metadata.Description?.Contains("stored procedure") ?? false);
        }

        /// <summary>
        /// Test that constructor throws ArgumentNullException when entity name is null.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ThrowsArgumentNullException_WhenEntityNameIsNull()
        {
            // Arrange
            Entity entity = CreateTestStoredProcedureEntity();

            // Act
            _ = new DynamicCustomTool(null!, entity);
        }

        /// <summary>
        /// Test that constructor throws ArgumentNullException when entity is null.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ArgumentNullException))]
        public void Constructor_ThrowsArgumentNullException_WhenEntityIsNull()
        {
            // Act
            _ = new DynamicCustomTool("TestEntity", null!);
        }

        /// <summary>
        /// Test that constructor throws ArgumentException when entity is not a stored procedure.
        /// </summary>
        [TestMethod]
        [ExpectedException(typeof(ArgumentException))]
        public void Constructor_ThrowsArgumentException_WhenEntityIsNotStoredProcedure()
        {
            // Arrange - Create table entity
            Entity tableEntity = new(
                Source: new("books", EntitySourceType.Table, null, null),
                GraphQL: new("Book", "Books"),
                Fields: null,
                Rest: new(Enabled: true),
                Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] { new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null) }) },
                Mappings: null,
                Relationships: null
            );

            // Act
            _ = new DynamicCustomTool("Book", tableEntity);
        }

        /// <summary>
        /// Test that input schema is generated with empty properties when no parameters.
        /// </summary>
        [TestMethod]
        public void GetToolMetadata_GeneratesEmptySchema_WhenNoParameters()
        {
            // Arrange
            Entity entity = CreateTestStoredProcedureEntity();
            DynamicCustomTool tool = new("GetBooks", entity);

            // Act
            ModelContextProtocol.Protocol.Tool metadata = tool.GetToolMetadata();

            // Assert
            Assert.IsNotNull(metadata.InputSchema);
            JsonDocument schemaObj = JsonDocument.Parse(metadata.InputSchema.GetRawText());
            Assert.IsTrue(schemaObj.RootElement.TryGetProperty("properties", out JsonElement props));
            Assert.AreEqual(JsonValueKind.Object, props.ValueKind);
            Assert.AreEqual(0, props.EnumerateObject().Count());
        }

        /// <summary>
        /// Test that input schema includes parameter definitions with descriptions.
        /// </summary>
        [TestMethod]
        public void GetToolMetadata_GeneratesSchemaWithParameters_WhenParametersProvided()
        {
            // Arrange
            ParameterMetadata[] parameters = new[]
            {
                new ParameterMetadata { Name = "id", Description = "The book ID" },
                new ParameterMetadata { Name = "title", Description = "The book title" }
            };
            Entity entity = CreateTestStoredProcedureEntity(parameters: parameters);
            DynamicCustomTool tool = new("GetBook", entity);

            // Act
            ModelContextProtocol.Protocol.Tool metadata = tool.GetToolMetadata();

            // Assert
            JsonDocument schemaObj = JsonDocument.Parse(metadata.InputSchema.GetRawText());
            Assert.IsTrue(schemaObj.RootElement.TryGetProperty("properties", out JsonElement props));
            Assert.IsTrue(props.TryGetProperty("id", out JsonElement idParam));
            Assert.IsTrue(idParam.TryGetProperty("description", out JsonElement idDesc));
            Assert.AreEqual("The book ID", idDesc.GetString());
            Assert.IsTrue(props.TryGetProperty("title", out JsonElement titleParam));
            Assert.IsTrue(titleParam.TryGetProperty("description", out JsonElement titleDesc));
            Assert.AreEqual("The book title", titleDesc.GetString());
        }

        /// <summary>
        /// Test that parameter schema uses default description when not provided.
        /// </summary>
        [TestMethod]
        public void GetToolMetadata_UsesDefaultParameterDescription_WhenNotProvided()
        {
            // Arrange
            ParameterMetadata[] parameters = new[]
            {
                new ParameterMetadata { Name = "userId" }
            };
            Entity entity = CreateTestStoredProcedureEntity(parameters: parameters);
            DynamicCustomTool tool = new("GetUser", entity);

            // Act
            ModelContextProtocol.Protocol.Tool metadata = tool.GetToolMetadata();

            // Assert
            JsonDocument schemaObj = JsonDocument.Parse(metadata.InputSchema.GetRawText());
            Assert.IsTrue(schemaObj.RootElement.TryGetProperty("properties", out JsonElement props));
            Assert.IsTrue(props.TryGetProperty("userId", out JsonElement userIdParam));
            Assert.IsTrue(userIdParam.TryGetProperty("description", out JsonElement desc));
            Assert.IsTrue(desc.GetString()!.Contains("userId"));
        }

        /// <summary>
        /// Helper method to create a test stored procedure entity.
        /// </summary>
        private static Entity CreateTestStoredProcedureEntity(
            string? description = null,
            ParameterMetadata[]? parameters = null)
        {
            return new Entity(
                Source: new(
                    Object: "test_procedure",
                    Type: EntitySourceType.StoredProcedure,
                    Parameters: parameters?.ToList(),
                    KeyFields: null
                ),
                GraphQL: new(Singular: "TestProcedure", Plural: "TestProcedures"),
                Fields: null,
                Rest: new(Enabled: true),
                Permissions: new[]
                {
                    new EntityPermission(
                        Role: "anonymous",
                        Actions: new[] { new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null) }
                    )
                },
                Mappings: null,
                Relationships: null,
                Cache: null,
                IsLinkingEntity: false,
                Health: null,
                Description: description,
                Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: null)
            );
        }
    }
}
