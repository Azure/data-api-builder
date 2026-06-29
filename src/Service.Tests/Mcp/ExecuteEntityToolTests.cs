// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
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
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Unit tests for ExecuteEntityTool parameter validation and default application.
    /// Uses mocked metadata and query engine to isolate the tool's logic from real DB.
    ///
    /// Key behaviors tested:
    /// - Parameters validated against StoredProcedureDefinition.Parameters (DB metadata).
    /// - Config defaults (HasConfigDefault/ConfigDefaultValue) applied for missing params.
    /// - Invalid parameter names rejected.
    /// - Entity-level and runtime-level gating.
    /// </summary>
    [TestClass]
    public class ExecuteEntityToolTests
    {
        private const string TEST_ENTITY = "GetBook";
        private const string SP_SOURCE_OBJECT = "get_book";

        #region Parameter Validation Tests

        /// <summary>
        /// A parameter that exists in DB metadata (StoredProcedureDefinition.Parameters)
        /// is accepted even if it has no config-side entry.
        /// </summary>
        [TestMethod]
        public async Task ExecuteEntity_AcceptsDbDiscoveredParam_NotInConfig()
        {
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["id"] = new()
            };

            CallToolResult result = await ExecuteWithMockedEngineAsync(
                entityName: TEST_ENTITY,
                dbParameters: dbParams,
                userParameters: new() { { "id", 1 } });

            AssertSuccess(result, "Should accept DB-discovered param 'id'.");
        }

        /// <summary>
        /// A parameter name NOT in StoredProcedureDefinition.Parameters is rejected
        /// with an InvalidArguments error.
        /// </summary>
        [DataTestMethod]
        [DataRow("nonexistent", DisplayName = "Completely unknown param")]
        [DataRow("ID", DisplayName = "Case-sensitive mismatch")]
        public async Task ExecuteEntity_RejectsInvalidParamName(string invalidParamName)
        {
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["id"] = new()
            };

            CallToolResult result = await ExecuteWithMockedEngineAsync(
                entityName: TEST_ENTITY,
                dbParameters: dbParams,
                userParameters: new() { { invalidParamName, "value" } });

            Assert.IsTrue(result.IsError == true,
                $"Should reject param '{invalidParamName}' not in DB metadata.");
            string content = GetFirstText(result);
            StringAssert.Contains(content, invalidParamName);
            StringAssert.Contains(content, "InvalidArguments");
        }

        /// <summary>
        /// Multiple parameters can be provided when all exist in DB metadata.
        /// </summary>
        [TestMethod]
        public async Task ExecuteEntity_AcceptsMultipleValidParams()
        {
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["title"] = new(),
                ["publisher_id"] = new()
            };

            CallToolResult result = await ExecuteWithMockedEngineAsync(
                entityName: TEST_ENTITY,
                dbParameters: dbParams,
                userParameters: new() { { "title", "Test" }, { "publisher_id", 123 } });

            AssertSuccess(result, "Should accept all valid params.");
        }

        /// <summary>
        /// If one param in a multi-param request is invalid, the entire request is rejected.
        /// </summary>
        [TestMethod]
        public async Task ExecuteEntity_RejectsRequest_WhenAnyParamInvalid()
        {
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["id"] = new()
            };

            CallToolResult result = await ExecuteWithMockedEngineAsync(
                entityName: TEST_ENTITY,
                dbParameters: dbParams,
                userParameters: new() { { "id", 1 }, { "bogus", "x" } });

            Assert.IsTrue(result.IsError == true,
                "Should reject request when any param is invalid.");
            StringAssert.Contains(GetFirstText(result), "bogus");
        }

        #endregion

        #region Default Application Tests

        /// <summary>
        /// Config defaults are applied for parameters the user did not supply.
        /// Verifies that the context passed to the query engine includes the default values.
        /// </summary>
        [TestMethod]
        public async Task ExecuteEntity_AppliesConfigDefaults_ForMissingParams()
        {
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["title"] = new() { HasConfigDefault = true, ConfigDefaultValue = "defaultTitle" },
                ["publisher_id"] = new() { HasConfigDefault = true, ConfigDefaultValue = "999" }
            };

            StoredProcedureRequestContext? capturedContext = null;
            CallToolResult result = await ExecuteWithMockedEngineAsync(
                entityName: TEST_ENTITY,
                dbParameters: dbParams,
                userParameters: null,
                captureContext: ctx => capturedContext = ctx);

            AssertSuccess(result, "Should succeed with config defaults.");
            Assert.IsNotNull(capturedContext, "Query engine should have been called.");
            Assert.IsTrue(capturedContext!.ResolvedParameters.ContainsKey("title"));
            Assert.IsTrue(capturedContext.ResolvedParameters.ContainsKey("publisher_id"));
            Assert.AreEqual("defaultTitle", capturedContext.ResolvedParameters["title"]);
            Assert.AreEqual("999", capturedContext.ResolvedParameters["publisher_id"]);
        }

        /// <summary>
        /// User-supplied parameters override config defaults.
        /// </summary>
        [TestMethod]
        public async Task ExecuteEntity_UserParams_OverrideConfigDefaults()
        {
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["title"] = new() { HasConfigDefault = true, ConfigDefaultValue = "defaultTitle" },
                ["publisher_id"] = new() { HasConfigDefault = true, ConfigDefaultValue = "999" }
            };

            StoredProcedureRequestContext? capturedContext = null;
            CallToolResult result = await ExecuteWithMockedEngineAsync(
                entityName: TEST_ENTITY,
                dbParameters: dbParams,
                userParameters: new() { { "title", "UserTitle" } },
                captureContext: ctx => capturedContext = ctx);

            AssertSuccess(result, "Should succeed with user-supplied params.");
            Assert.IsNotNull(capturedContext);
            Assert.AreEqual("UserTitle", capturedContext!.ResolvedParameters["title"]);
            // publisher_id should get the config default since user didn't supply it
            Assert.AreEqual("999", capturedContext.ResolvedParameters["publisher_id"]);
        }

        /// <summary>
        /// Parameters without config defaults are NOT injected into the request.
        /// Only params with HasConfigDefault=true get applied.
        /// </summary>
        [TestMethod]
        public async Task ExecuteEntity_DoesNotInjectParams_WithoutConfigDefault()
        {
            Dictionary<string, ParameterDefinition> dbParams = new()
            {
                ["id"] = new(), // No config default
                ["tenant"] = new() { HasConfigDefault = true, ConfigDefaultValue = "default_tenant" }
            };

            StoredProcedureRequestContext? capturedContext = null;
            CallToolResult result = await ExecuteWithMockedEngineAsync(
                entityName: TEST_ENTITY,
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
        /// Zero-parameter SP with no user params and no config defaults: no parameters
        /// are passed to the query engine.
        /// </summary>
        [TestMethod]
        public async Task ExecuteEntity_ZeroParamSP_PassesEmptyParams()
        {
            Dictionary<string, ParameterDefinition> dbParams = new();

            StoredProcedureRequestContext? capturedContext = null;
            CallToolResult result = await ExecuteWithMockedEngineAsync(
                entityName: TEST_ENTITY,
                dbParameters: dbParams,
                userParameters: null,
                captureContext: ctx => capturedContext = ctx);

            AssertSuccess(result, "Should succeed for zero-param SP.");
            Assert.IsNotNull(capturedContext);
            Assert.AreEqual(0, capturedContext!.ResolvedParameters.Count);
        }

        #endregion

        #region Gating Tests

        /// <summary>
        /// When the entity is not a stored procedure, ExecuteEntityTool returns InvalidEntity.
        /// </summary>
        [TestMethod]
        public async Task ExecuteEntity_RejectsNonStoredProcedureEntity()
        {
            IServiceProvider sp = BuildServiceProvider(
                entityName: "Book",
                sourceObject: "books",
                sourceType: EntitySourceType.Table,
                dbParameters: new());

            ExecuteEntityTool tool = new();
            using JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);

            Assert.IsTrue(result.IsError == true);
            StringAssert.Contains(GetFirstText(result), "InvalidEntity");
        }

        /// <summary>
        /// When the entity does not exist in config, returns EntityNotFound.
        /// </summary>
        [TestMethod]
        public async Task ExecuteEntity_ReturnsError_WhenEntityNotFound()
        {
            IServiceProvider sp = BuildServiceProvider(
                entityName: TEST_ENTITY,
                sourceObject: SP_SOURCE_OBJECT,
                sourceType: EntitySourceType.StoredProcedure,
                dbParameters: new() { ["id"] = new() });

            ExecuteEntityTool tool = new();
            using JsonDocument args = JsonDocument.Parse("{\"entity\": \"NonExistent\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);

            Assert.IsTrue(result.IsError == true);
            StringAssert.Contains(GetFirstText(result), "EntityNotFound");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Runs ExecuteEntityTool with a mocked query engine that captures the
        /// StoredProcedureRequestContext and returns an empty result.
        /// </summary>
        private static async Task<CallToolResult> ExecuteWithMockedEngineAsync(
            string entityName,
            Dictionary<string, ParameterDefinition> dbParameters,
            Dictionary<string, object>? userParameters,
            Action<StoredProcedureRequestContext>? captureContext = null)
        {
            IServiceProvider sp = BuildServiceProvider(
                entityName: entityName,
                sourceObject: SP_SOURCE_OBJECT,
                sourceType: EntitySourceType.StoredProcedure,
                dbParameters: dbParameters,
                captureContext: captureContext);

            ExecuteEntityTool tool = new();

            var args = new Dictionary<string, object> { { "entity", entityName } };
            if (userParameters != null)
            {
                args["parameters"] = userParameters;
            }

            string argsJson = JsonSerializer.Serialize(args);
            using JsonDocument arguments = JsonDocument.Parse(argsJson);

            return await tool.ExecuteAsync(arguments, sp, CancellationToken.None);
        }

        /// <summary>
        /// Builds a fully mocked service provider for ExecuteEntityTool.
        /// </summary>
        private static IServiceProvider BuildServiceProvider(
            string entityName,
            string sourceObject,
            EntitySourceType sourceType,
            Dictionary<string, ParameterDefinition> dbParameters,
            Action<StoredProcedureRequestContext>? captureContext = null)
        {
            Entity entity = new(
                Source: new(sourceObject, sourceType, Parameters: null, KeyFields: null),
                GraphQL: new(entityName, entityName),
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
                Mcp: null);

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

            // Mock authorization resolver
            Mock<IAuthorizationResolver> mockAuthResolver = new();
            mockAuthResolver.Setup(x => x.IsValidRoleContext(It.IsAny<HttpContext>())).Returns(true);
            mockAuthResolver
                .Setup(x => x.AreRoleAndOperationDefinedForEntity(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<EntityActionOperation>()))
                .Returns(true);
            services.AddSingleton(mockAuthResolver.Object);

            // Mock HttpContext with anonymous role header
            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = "anonymous";
            IHttpContextAccessor httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
            services.AddSingleton(httpContextAccessor);

            // Mock metadata provider with DB object
            DatabaseObject dbObject = sourceType == EntitySourceType.StoredProcedure
                ? new DatabaseStoredProcedure("dbo", sourceObject)
                {
                    SourceType = EntitySourceType.StoredProcedure,
                    StoredProcedureDefinition = new StoredProcedureDefinition
                    {
                        Parameters = dbParameters
                    }
                }
                : new DatabaseTable("dbo", sourceObject) { SourceType = EntitySourceType.Table };

            Mock<ISqlMetadataProvider> mockSqlMetadataProvider = new();
            mockSqlMetadataProvider
                .Setup(x => x.EntityToDatabaseObject)
                .Returns(new Dictionary<string, DatabaseObject> { [entityName] = dbObject });
            mockSqlMetadataProvider.Setup(x => x.GetDatabaseType()).Returns(DatabaseType.MSSQL);

            Mock<IMetadataProviderFactory> mockMetadataProviderFactory = new();
            mockMetadataProviderFactory
                .Setup(x => x.GetMetadataProvider(It.IsAny<string>()))
                .Returns(mockSqlMetadataProvider.Object);
            services.AddSingleton(mockMetadataProviderFactory.Object);

            // Mock query engine factory
            Mock<IQueryEngine> mockQueryEngine = new();
            mockQueryEngine
                .Setup(x => x.ExecuteAsync(It.IsAny<StoredProcedureRequestContext>(), It.IsAny<string>()))
                .Returns((StoredProcedureRequestContext ctx, string ds) =>
                {
                    captureContext?.Invoke(ctx);
                    // Return empty JSON array result
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
    }
}
