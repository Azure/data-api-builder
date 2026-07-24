// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
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

        /// <summary>
        /// Per spec #3331, the MCP custom tool's input schema must surface the auto-embed
        /// indicator in the parameter description so MCP-aware clients (including LLMs)
        /// see that DAB will convert the value to an embedding before executing the
        /// stored procedure. The schema type is intentionally left as the existing
        /// multi-type array — runtime validation (ParameterEmbeddingHelper) rejects
        /// non-string inputs to auto-embed params with a 400, so the schema doesn't
        /// need to encode that constraint.
        /// </summary>
        [TestMethod]
        public void GetToolMetadata_AutoEmbedParam_DescriptionContainsAutoEmbedIndicator()
        {
            // Arrange — one auto-embed param + one normal param, so we can assert that
            // only the auto-embed param's description picks up the indicator.
            ParameterMetadata[] parameters = new[]
            {
                new ParameterMetadata { Name = "queryText", AutoEmbed = true, Description = "Search text" },
                new ParameterMetadata { Name = "topK",      AutoEmbed = false, Description = "Result count" }
            };
            Entity entity = CreateTestStoredProcedureEntity(parameters: parameters);
            DynamicCustomTool tool = new("SearchProducts", entity);

            // Act
            ModelContextProtocol.Protocol.Tool metadata = tool.GetToolMetadata();
            JsonDocument schemaDoc = JsonDocument.Parse(metadata.InputSchema.GetRawText());
            JsonElement props = schemaDoc.RootElement.GetProperty("properties");

            // Auto-embed param: description mentions auto-embed
            JsonElement autoParam = props.GetProperty("queryText");
            StringAssert.Contains(autoParam.GetProperty("description").GetString()!, "auto-embed",
                "Auto-embed param description should mention the auto-embed behavior.");

            // Non-auto-embed param: description does NOT mention auto-embed
            JsonElement normalParam = props.GetProperty("topK");
            Assert.IsFalse(normalParam.GetProperty("description").GetString()!.Contains("auto-embed"),
                "Non-auto-embed param description must not contain the auto-embed indicator.");
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

        #region Schema Alignment Tests (DB Metadata)

        /// <summary>
        /// After InitializeMetadata is called with valid DB metadata,
        /// GetToolMetadata returns a schema with typed parameters from SP definition.
        /// </summary>
        [TestMethod]
        public void GetToolMetadata_UsesDbMetadata_WhenInitialized()
        {
            // Arrange
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["id"] = new() { SystemType = typeof(int) },
                ["name"] = new() { SystemType = typeof(string) }
            };

            // Act
            JsonElement props = InitializeAndGetSchemaProperties(dbParams);

            // Assert
            Assert.AreEqual("integer", props.GetProperty("id").GetProperty("type").GetString());
            Assert.AreEqual("string", props.GetProperty("name").GetProperty("type").GetString());
        }

        /// <summary>
        /// When InitializeMetadata cannot resolve DB metadata, tool falls back to config schema.
        /// </summary>
        [TestMethod]
        public void GetToolMetadata_FallsBackToConfig_WhenDbMetadataUnavailable()
        {
            // Arrange - use a service provider without metadata factory
            ParameterMetadata[] parameters = new[]
            {
                new ParameterMetadata { Name = "userId", Description = "User ID" }
            };
            Entity entity = CreateTestStoredProcedureEntity(parameters: parameters);
            DynamicCustomTool tool = new("GetUser", entity);

            ServiceCollection services = new();
            services.AddLogging();

            // Act
            tool.InitializeMetadata(services.BuildServiceProvider());
            JsonElement props = ParseSchemaProperties(tool.GetToolMetadata());

            // Assert - should use config-based permissive type array
            Assert.AreEqual(JsonValueKind.Array, props.GetProperty("userId").GetProperty("type").ValueKind,
                "Fallback schema should use multi-type array.");
        }

        /// <summary>
        /// Without calling InitializeMetadata, tool uses config-based schema.
        /// </summary>
        [TestMethod]
        public void GetToolMetadata_UsesConfigSchema_WhenNotInitialized()
        {
            // Arrange
            ParameterMetadata[] parameters = new[]
            {
                new ParameterMetadata { Name = "bookId", Description = "Book identifier" }
            };
            Entity entity = CreateTestStoredProcedureEntity(parameters: parameters);
            DynamicCustomTool tool = new("GetBook", entity);

            // Act - no InitializeMetadata call
            JsonElement props = ParseSchemaProperties(tool.GetToolMetadata());

            // Assert
            JsonElement param = props.GetProperty("bookId");
            Assert.AreEqual(JsonValueKind.Array, param.GetProperty("type").ValueKind, "Config schema uses multi-type array.");
            Assert.AreEqual("Book identifier", param.GetProperty("description").GetString());
        }

        /// <summary>
        /// DB metadata parameters NOT in config are still discovered and included in schema.
        /// </summary>
        [TestMethod]
        public void InitializeMetadata_IncludesAllDbDiscoveredParams()
        {
            // Arrange - config has no parameters, but DB has them
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["id"] = new() { SystemType = typeof(int) },
                ["title"] = new() { SystemType = typeof(string) },
                ["price"] = new() { SystemType = typeof(decimal) }
            };

            // Act
            JsonElement props = InitializeAndGetSchemaProperties(dbParams);

            // Assert - all 3 DB-discovered params should appear
            Assert.AreEqual(3, props.EnumerateObject().Count());
            Assert.IsTrue(props.TryGetProperty("id", out _));
            Assert.IsTrue(props.TryGetProperty("title", out _));
            Assert.IsTrue(props.TryGetProperty("price", out _));
        }

        /// <summary>
        /// Verifies type mapping for common .NET types to JSON Schema types.
        /// </summary>
        [DataTestMethod]
        [DataRow(typeof(int), "integer")]
        [DataRow(typeof(long), "integer")]
        [DataRow(typeof(short), "integer")]
        [DataRow(typeof(byte), "integer")]
        [DataRow(typeof(float), "number")]
        [DataRow(typeof(double), "number")]
        [DataRow(typeof(decimal), "number")]
        [DataRow(typeof(bool), "boolean")]
        [DataRow(typeof(string), "string")]
        [DataRow(typeof(Guid), "string")]
        [DataRow(typeof(DateTime), "string")]
        [DataRow(typeof(DateTimeOffset), "string")]
        public void InitializeMetadata_MapsSystemTypesToJsonSchemaTypes(Type systemType, string expectedJsonType)
        {
            // Arrange & Act
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["param1"] = new() { SystemType = systemType }
            };
            JsonElement props = InitializeAndGetSchemaProperties(dbParams);

            // Assert
            Assert.AreEqual(expectedJsonType, props.GetProperty("param1").GetProperty("type").GetString());
        }

        /// <summary>
        /// When SystemType is null, schema uses a permissive multi-type array.
        /// </summary>
        [TestMethod]
        public void InitializeMetadata_UsesPermissiveType_WhenSystemTypeIsNull()
        {
            // Arrange & Act
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["unknown_param"] = new() { SystemType = null! }
            };
            JsonElement props = InitializeAndGetSchemaProperties(dbParams);

            // Assert
            Assert.AreEqual(JsonValueKind.Array, props.GetProperty("unknown_param").GetProperty("type").ValueKind);
        }

        /// <summary>
        /// When a parameter has HasConfigDefault=true, the description includes the default value.
        /// </summary>
        [TestMethod]
        public void InitializeMetadata_IncludesDefaultInDescription()
        {
            // Arrange & Act
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["tenant"] = new() { SystemType = typeof(string), HasConfigDefault = true, ConfigDefaultValue = "default_tenant" }
            };
            JsonElement props = InitializeAndGetSchemaProperties(dbParams);

            // Assert
            string desc = props.GetProperty("tenant").GetProperty("description").GetString()!;
            StringAssert.Contains(desc, "default: default_tenant");
        }

        /// <summary>
        /// Zero-parameter SP with DB metadata returns empty properties object.
        /// </summary>
        [TestMethod]
        public void InitializeMetadata_ZeroParamSP_ReturnsEmptyProperties()
        {
            // Arrange & Act
            JsonElement props = InitializeAndGetSchemaProperties(new Dictionary<string, ParameterDefinition>());

            // Assert
            Assert.AreEqual(0, props.EnumerateObject().Count());
        }

        /// <summary>
        /// DB-discovered parameters with no config default/override are advertised as required.
        /// </summary>
        [TestMethod]
        public void InitializeMetadata_IncludesRequiredArray_ForParamsWithoutDefaults()
        {
            // Arrange
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["productId"] = new() { SystemType = typeof(int) }
            };

            // Act
            JsonElement schema = InitializeAndGetSchema(dbParams);

            // Assert
            Assert.IsTrue(schema.TryGetProperty("required", out JsonElement required),
                "Schema should expose a 'required' array for parameters without defaults.");
            CollectionAssert.AreEquivalent(
                new[] { "productId" },
                EnumerateStrings(required),
                "'productId' should be listed as required.");
        }

        /// <summary>
        /// Parameters that have a config default or are explicitly marked optional are excluded
        /// from the required array, while parameters without defaults remain required.
        /// </summary>
        [TestMethod]
        public void InitializeMetadata_ExcludesParamsWithDefaultsOrOptionalFlag_FromRequired()
        {
            // Arrange
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["id"] = new() { SystemType = typeof(int) },
                ["title"] = new() { SystemType = typeof(string), HasConfigDefault = true, ConfigDefaultValue = "randomX" },
                ["category"] = new() { SystemType = typeof(string), Required = true, HasConfigDefault = true, ConfigDefaultValue = "defaultCategory" },
                ["publisher_id"] = new() { SystemType = typeof(int), Required = false }
            };

            // Act
            JsonElement schema = InitializeAndGetSchema(dbParams);

            // Assert
            Assert.IsTrue(schema.TryGetProperty("required", out JsonElement required));
            CollectionAssert.AreEquivalent(
                new[] { "id" },
                EnumerateStrings(required),
                "Only 'id' (no default, not marked optional) should be required.");
        }

        /// <summary>
        /// A zero-parameter SP must not emit a 'required' array.
        /// </summary>
        [TestMethod]
        public void InitializeMetadata_ZeroParamSP_OmitsRequiredArray()
        {
            // Arrange & Act
            JsonElement schema = InitializeAndGetSchema(new Dictionary<string, ParameterDefinition>());

            // Assert
            Assert.IsFalse(schema.TryGetProperty("required", out _),
                "Zero-param SP should not include a 'required' array.");
        }

        /// <summary>
        /// When falling back to config-based schema, parameters without a default are required.
        /// </summary>
        [TestMethod]
        public void GetToolMetadata_ConfigFallback_MarksParamsWithoutDefaultsRequired()
        {
            // Arrange - config declares one required param and one with a default
            ParameterMetadata[] parameters = new[]
            {
                new ParameterMetadata { Name = "userId" },
                new ParameterMetadata { Name = "tenant", Default = "contoso" }
            };
            Entity entity = CreateTestStoredProcedureEntity(parameters: parameters);
            DynamicCustomTool tool = new("GetUser", entity);

            // Act - no InitializeMetadata call, so the config-based schema is used
            JsonElement schema = tool.GetToolMetadata().InputSchema;

            // Assert
            Assert.IsTrue(schema.TryGetProperty("required", out JsonElement required));
            CollectionAssert.AreEquivalent(
                new[] { "userId" },
                EnumerateStrings(required),
                "Only 'userId' (no default) should be required in the config-based schema.");
        }

        /// <summary>
        /// Config fallback should exclude parameters with defaults from the required array even when
        /// they are explicitly marked required.
        /// </summary>
        [TestMethod]
        public void GetToolMetadata_ConfigFallback_ExcludesRequiredParamWhenDefaultExists()
        {
            // Arrange
            ParameterMetadata[] parameters = new[]
            {
                new ParameterMetadata { Name = "userId", Required = true },
                new ParameterMetadata { Name = "tenant", Required = true, Default = "contoso" }
            };
            Entity entity = CreateTestStoredProcedureEntity(parameters: parameters);
            DynamicCustomTool tool = new("GetUser", entity);

            // Act
            JsonElement schema = tool.GetToolMetadata().InputSchema;

            // Assert
            Assert.IsTrue(schema.TryGetProperty("required", out JsonElement required));
            CollectionAssert.AreEquivalent(
                new[] { "userId" },
                EnumerateStrings(required),
                "Parameters with config defaults should not be required even when marked required.");
        }

        /// <summary>
        /// Helper: Parses the "required" array values into a list of strings.
        /// </summary>
        private static List<string> EnumerateStrings(JsonElement array)
        {
            List<string> values = new();
            foreach (JsonElement element in array.EnumerateArray())
            {
                values.Add(element.GetString()!);
            }

            return values;
        }

        /// <summary>
        /// Helper: Creates a DynamicCustomTool, initializes it with mocked DB metadata, and returns
        /// the full input schema element.
        /// </summary>
        private static JsonElement InitializeAndGetSchema(
            Dictionary<string, ParameterDefinition> dbParameters,
            string entityName = "TestSP")
        {
            Entity entity = CreateTestStoredProcedureEntity();
            DynamicCustomTool tool = new(entityName, entity);
            IServiceProvider sp = BuildServiceProviderForMetadata(entityName, dbParameters);

            tool.InitializeMetadata(sp);
            return tool.GetToolMetadata().InputSchema;
        }

        /// <summary>
        /// Helper: Creates a DynamicCustomTool, initializes it with mocked DB metadata, and returns
        /// the "properties" element from the resulting input schema.
        /// </summary>
        private static JsonElement InitializeAndGetSchemaProperties(
            Dictionary<string, ParameterDefinition> dbParameters,
            string entityName = "TestSP")
        {
            Entity entity = CreateTestStoredProcedureEntity();
            DynamicCustomTool tool = new(entityName, entity);
            IServiceProvider sp = BuildServiceProviderForMetadata(entityName, dbParameters);

            tool.InitializeMetadata(sp);
            return ParseSchemaProperties(tool.GetToolMetadata());
        }

        /// <summary>
        /// Helper: Parses the "properties" element from a tool's input schema.
        /// </summary>
        private static JsonElement ParseSchemaProperties(ModelContextProtocol.Protocol.Tool metadata)
        {
            return metadata.InputSchema.GetProperty("properties");
        }

        /// <summary>
        /// Builds a service provider with mocked metadata infrastructure for schema tests.
        /// </summary>
        private static IServiceProvider BuildServiceProviderForMetadata(
            string entityName,
            Dictionary<string, ParameterDefinition> dbParameters)
        {
            Entity entity = new(
                Source: new("test_procedure", EntitySourceType.StoredProcedure, Parameters: null, KeyFields: null),
                GraphQL: new(entityName, entityName),
                Rest: new(Enabled: true),
                Fields: null,
                Permissions: new[]
                {
                    new EntityPermission(
                        Role: "anonymous",
                        Actions: new[] { new EntityAction(Action: EntityActionOperation.Execute, Fields: null, Policy: null) })
                },
                Relationships: null,
                Mappings: null,
                Mcp: new EntityMcpOptions(customToolEnabled: true, dmlToolsEnabled: null));

            Dictionary<string, Entity> entities = new() { [entityName] = entity };

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

            // Mock metadata provider with DB object
            DatabaseStoredProcedure dbObject = new("dbo", "test_procedure")
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
                .Returns(new Dictionary<string, DatabaseObject> { [entityName] = dbObject });

            Mock<IMetadataProviderFactory> mockMetadataProviderFactory = new();
            mockMetadataProviderFactory
                .Setup(x => x.GetMetadataProvider(It.IsAny<string>()))
                .Returns(mockSqlMetadataProvider.Object);
            services.AddSingleton(mockMetadataProviderFactory.Object);

            services.AddLogging();

            return services.BuildServiceProvider();
        }

        #endregion
    }
}
