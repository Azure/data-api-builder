// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Models;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services;
using Azure.DataApiBuilder.Core.Services.MetadataProviders;
using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;

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

        #region Parameter Validation Tests (ExecuteAsync)

        /// <summary>
        /// A parameter that exists in DB metadata is accepted during execution.
        /// </summary>
        [TestMethod]
        public async Task ExecuteAsync_AcceptsDbDiscoveredParam()
        {
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["id"] = new()
            };

            CallToolResult result = await ExecuteCustomToolAsync(
                dbParameters: dbParams,
                userParameters: new() { { "id", 1 } });

            AssertSuccess(result, "Should accept DB-discovered param 'id'.");
        }

        /// <summary>
        /// A parameter name NOT in StoredProcedureDefinition.Parameters is rejected.
        /// </summary>
        [DataTestMethod]
        [DataRow("nonexistent", DisplayName = "Unknown param")]
        [DataRow("ID", DisplayName = "Case-sensitive mismatch")]
        public async Task ExecuteAsync_RejectsInvalidParamName(string invalidParamName)
        {
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["id"] = new()
            };

            CallToolResult result = await ExecuteCustomToolAsync(
                dbParameters: dbParams,
                userParameters: new() { { invalidParamName, "value" } });

            Assert.IsTrue(result.IsError == true,
                $"Should reject param '{invalidParamName}' not in DB metadata.");
            string content = GetFirstText(result);
            StringAssert.Contains(content, invalidParamName);
        }

        /// <summary>
        /// Config defaults from ParameterDefinition.HasConfigDefault are applied for missing params.
        /// </summary>
        [TestMethod]
        public async Task ExecuteAsync_AppliesConfigDefaults_ForMissingParams()
        {
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["title"] = new() { HasConfigDefault = true, ConfigDefaultValue = "defaultTitle" },
                ["publisher_id"] = new() { HasConfigDefault = true, ConfigDefaultValue = "999" }
            };

            StoredProcedureRequestContext? capturedContext = null;
            CallToolResult result = await ExecuteCustomToolAsync(
                dbParameters: dbParams,
                userParameters: null,
                captureContext: ctx => capturedContext = ctx);

            AssertSuccess(result, "Should succeed with config defaults.");
            Assert.IsNotNull(capturedContext);
            Assert.AreEqual("defaultTitle", capturedContext!.ResolvedParameters["title"]);
            Assert.AreEqual("999", capturedContext.ResolvedParameters["publisher_id"]);
        }

        /// <summary>
        /// User-supplied parameters override config defaults.
        /// </summary>
        [TestMethod]
        public async Task ExecuteAsync_UserParams_OverrideConfigDefaults()
        {
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["title"] = new() { HasConfigDefault = true, ConfigDefaultValue = "defaultTitle" },
                ["publisher_id"] = new() { HasConfigDefault = true, ConfigDefaultValue = "999" }
            };

            StoredProcedureRequestContext? capturedContext = null;
            CallToolResult result = await ExecuteCustomToolAsync(
                dbParameters: dbParams,
                userParameters: new() { { "title", "UserTitle" } },
                captureContext: ctx => capturedContext = ctx);

            AssertSuccess(result, "Should succeed with user-supplied params.");
            Assert.IsNotNull(capturedContext);
            Assert.AreEqual("UserTitle", capturedContext!.ResolvedParameters["title"]);
            Assert.AreEqual("999", capturedContext.ResolvedParameters["publisher_id"]);
        }

        /// <summary>
        /// Parameters without HasConfigDefault are NOT injected.
        /// </summary>
        [TestMethod]
        public async Task ExecuteAsync_DoesNotInjectParams_WithoutConfigDefault()
        {
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["id"] = new(),
                ["tenant"] = new() { HasConfigDefault = true, ConfigDefaultValue = "default_tenant" }
            };

            StoredProcedureRequestContext? capturedContext = null;
            CallToolResult result = await ExecuteCustomToolAsync(
                dbParameters: dbParams,
                userParameters: new() { { "id", 42 } },
                captureContext: ctx => capturedContext = ctx);

            AssertSuccess(result, "Should succeed with partial params.");
            Assert.IsNotNull(capturedContext);
            Assert.IsTrue(capturedContext!.ResolvedParameters.ContainsKey("id"));
            Assert.IsTrue(capturedContext.ResolvedParameters.ContainsKey("tenant"));
            Assert.AreEqual("default_tenant", capturedContext.ResolvedParameters["tenant"]);
        }

        /// <summary>
        /// Zero-param SP with no user params passes empty parameters.
        /// </summary>
        [TestMethod]
        public async Task ExecuteAsync_ZeroParamSP_PassesEmptyParams()
        {
            Dictionary<string, ParameterDefinition> dbParams = new();

            StoredProcedureRequestContext? capturedContext = null;
            CallToolResult result = await ExecuteCustomToolAsync(
                dbParameters: dbParams,
                userParameters: null,
                captureContext: ctx => capturedContext = ctx);

            AssertSuccess(result, "Should succeed for zero-param SP.");
            Assert.IsNotNull(capturedContext);
            Assert.AreEqual(0, capturedContext!.ResolvedParameters.Count);
        }

        #endregion

        #region Execution Helpers

        private const string TEST_ENTITY = "GetBook";
        private const string SP_SOURCE_OBJECT = "get_book";

        /// <summary>
        /// Executes a DynamicCustomTool with mocked services.
        /// </summary>
        private static async Task<CallToolResult> ExecuteCustomToolAsync(
            Dictionary<string, ParameterDefinition> dbParameters,
            Dictionary<string, object>? userParameters,
            Action<StoredProcedureRequestContext>? captureContext = null)
        {
            IServiceProvider sp = BuildExecutionServiceProvider(
                dbParameters: dbParameters,
                captureContext: captureContext);

            Entity entity = CreateTestStoredProcedureEntity();
            DynamicCustomTool tool = new(TEST_ENTITY, entity);

            string argsJson = userParameters != null
                ? JsonSerializer.Serialize(userParameters)
                : "{}";
            using JsonDocument arguments = JsonDocument.Parse(argsJson);

            return await tool.ExecuteAsync(arguments, sp, CancellationToken.None);
        }

        /// <summary>
        /// Builds a mocked service provider for DynamicCustomTool execution tests.
        /// </summary>
        private static IServiceProvider BuildExecutionServiceProvider(
            Dictionary<string, ParameterDefinition> dbParameters,
            Action<StoredProcedureRequestContext>? captureContext = null)
        {
            Entity entity = new(
                Source: new(SP_SOURCE_OBJECT, EntitySourceType.StoredProcedure, Parameters: null, KeyFields: null),
                GraphQL: new(TEST_ENTITY, TEST_ENTITY),
                Rest: new(Enabled: true),
                Fields: null,
                Permissions: new[]
                {
                    new EntityPermission(
                        Role: "anonymous",
                        Actions: new[]
                        {
                            new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null)
                        })
                },
                Relationships: null,
                Mappings: null,
                Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: null));

            Dictionary<string, Entity> entities = new() { [TEST_ENTITY] = entity };

            RuntimeConfig config = new(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(Enabled: true, Path: "/mcp", DmlTools: null),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(entities));

            ServiceCollection services = new();

            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            services.AddSingleton(configProvider);

            // Mock authorization resolver
            Mock<IAuthorizationResolver> mockAuthResolver = new();
            mockAuthResolver.Setup(x => x.IsValidRoleContext(It.IsAny<HttpContext>())).Returns(true);
            mockAuthResolver
                .Setup(x => x.AreRoleAndOperationDefinedForEntity(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EntityActionOperation>()))
                .Returns(true);
            services.AddSingleton(mockAuthResolver.Object);

            // Mock HttpContext
            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = "anonymous";
            IHttpContextAccessor httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
            services.AddSingleton(httpContextAccessor);

            // Mock metadata provider with DatabaseStoredProcedure
            DatabaseObject dbObject = new DatabaseStoredProcedure("dbo", SP_SOURCE_OBJECT)
            {
                SourceType = EntitySourceType.StoredProcedure,
                StoredProcedureDefinition = new StoredProcedureDefinition
                {
                    Parameters = dbParameters
                }
            };

            Mock<ISqlMetadataProvider> mockSqlMetadataProvider = new();
            mockSqlMetadataProvider
                .Setup(x => x.EntityToDatabaseObject)
                .Returns(new Dictionary<string, DatabaseObject> { [TEST_ENTITY] = dbObject });
            mockSqlMetadataProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.MSSQL);

            Mock<IMetadataProviderFactory> mockMetadataProviderFactory = new();
            mockMetadataProviderFactory
                .Setup(x => x.GetMetadataProvider(It.IsAny<string>()))
                .Returns(mockSqlMetadataProvider.Object);
            services.AddSingleton(mockMetadataProviderFactory.Object);

            // Mock query engine
            Mock<IQueryEngine> mockQueryEngine = new();
            mockQueryEngine
                .Setup(x => x.ExecuteAsync(It.IsAny<StoredProcedureRequestContext>(), It.IsAny<string>()))
                .Returns((StoredProcedureRequestContext ctx, string ds) =>
                {
                    captureContext?.Invoke(ctx);
                    using JsonDocument doc = JsonDocument.Parse("[]");
                    return Task.FromResult<IActionResult>(new OkObjectResult(doc.RootElement.Clone()));
                });

            Mock<IQueryEngineFactory> mockQueryEngineFactory = new();
            mockQueryEngineFactory
                .Setup(x => x.GetQueryEngine(It.IsAny<DatabaseType>()))
                .Returns(mockQueryEngine.Object);
            services.AddSingleton(mockQueryEngineFactory.Object);

            services.AddLogging();

            return services.BuildServiceProvider();
        }

        private static void AssertSuccess(CallToolResult result, string message)
        {
            Assert.IsTrue(result.IsError != true,
                $"{message} Content: {GetFirstText(result)}");
        }

        private static string GetFirstText(CallToolResult result)
        {
            if (result.Content is null || result.Content.Count == 0)
            {
                return string.Empty;
            }

            return result.Content[0] is TextContentBlock textBlock
                ? textBlock.Text ?? string.Empty
                : string.Empty;
        }

        #endregion

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
