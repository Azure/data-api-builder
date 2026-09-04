// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Net;
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
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
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

        #region Result and error handling tests

        [TestMethod]
        public async Task ExecuteEntity_CanceledToken_ReturnsOperationCanceled()
        {
            IServiceProvider serviceProvider = BuildServiceProvider(
                TEST_ENTITY,
                SP_SOURCE_OBJECT,
                EntitySourceType.StoredProcedure,
                new());
            using JsonDocument arguments = JsonDocument.Parse("{\"entity\":\"GetBook\"}");
            using CancellationTokenSource cancellationTokenSource = new();
            cancellationTokenSource.Cancel();

            CallToolResult result = await new ExecuteEntityTool().ExecuteAsync(
                arguments,
                serviceProvider,
                cancellationTokenSource.Token);

            AssertError(result, "OperationCanceled");
        }

        [TestMethod]
        public async Task ExecuteEntity_NullArguments_ReturnsInvalidArguments()
        {
            IServiceProvider serviceProvider = BuildServiceProvider(
                TEST_ENTITY,
                SP_SOURCE_OBJECT,
                EntitySourceType.StoredProcedure,
                new());

            CallToolResult result = await new ExecuteEntityTool().ExecuteAsync(null, serviceProvider, CancellationToken.None);

            AssertError(result, "InvalidArguments");
        }

        [TestMethod]
        public async Task ExecuteEntity_MissingHttpContext_ReturnsPermissionDenied()
        {
            CallToolResult result = await ExecuteWithMockedEngineAsync(
                TEST_ENTITY,
                new(),
                null,
                includeHttpContext: false);

            AssertError(result, "PermissionDenied");
        }

        [TestMethod]
        public async Task ExecuteEntity_UnauthorizedOperation_ReturnsPermissionDenied()
        {
            CallToolResult result = await ExecuteWithMockedEngineAsync(
                TEST_ENTITY,
                new(),
                null,
                authorizeOperation: false);

            AssertError(result, "PermissionDenied");
        }

        [TestMethod]
        public async Task ExecuteEntity_MetadataObjectIsNotStoredProcedure_ReturnsInvalidEntity()
        {
            DatabaseTable table = new("dbo", "books") { SourceType = EntitySourceType.Table };
            CallToolResult result = await ExecuteWithMockedEngineAsync(
                TEST_ENTITY,
                new(),
                null,
                metadataObject: table);

            AssertError(result, "InvalidEntity");
        }

        /// <summary>
        /// Verifies scalar JSON parameters become CLR values while null is preserved and composite input remains JSON text.
        /// </summary>
        [TestMethod]
        public async Task ExecuteEntity_ConvertsJsonParameterKinds()
        {
            Dictionary<string, ParameterDefinition> parameters = new()
            {
                ["text"] = new(),
                ["integer"] = new(),
                ["number"] = new(),
                ["truth"] = new(),
                ["falsehood"] = new(),
                ["nothing"] = new(),
                ["complex"] = new()
            };
            StoredProcedureRequestContext? capturedContext = null;
            IServiceProvider serviceProvider = BuildServiceProvider(
                TEST_ENTITY,
                SP_SOURCE_OBJECT,
                EntitySourceType.StoredProcedure,
                parameters,
                captureContext: context => capturedContext = context);
            using JsonDocument arguments = JsonDocument.Parse(
                "{\"entity\":\"GetBook\",\"parameters\":{" +
                "\"text\":\"value\",\"integer\":42,\"number\":1.5," +
                "\"truth\":true,\"falsehood\":false,\"nothing\":null,\"complex\":{\"x\":1}}}");

            CallToolResult result = await new ExecuteEntityTool().ExecuteAsync(arguments, serviceProvider, CancellationToken.None);

            AssertSuccess(result, "All supported JSON parameter kinds should be converted.");
            Assert.IsNotNull(capturedContext);
            Assert.AreEqual("value", capturedContext.ResolvedParameters["text"]);
            Assert.AreEqual(42L, capturedContext.ResolvedParameters["integer"]);
            Assert.AreEqual(1.5m, capturedContext.ResolvedParameters["number"]);
            Assert.AreEqual(true, capturedContext.ResolvedParameters["truth"]);
            Assert.AreEqual(false, capturedContext.ResolvedParameters["falsehood"]);
            Assert.IsNull(capturedContext.ResolvedParameters["nothing"]);
            Assert.AreEqual("{\"x\":1}", capturedContext.ResolvedParameters["complex"]);
        }

        /// <summary>
        /// Verifies recognizable DAB failure messages map to specific MCP errors and unmatched messages use the DAB fallback.
        /// </summary>
        [DataTestMethod]
        [DataRow("permission denied", "PermissionDenied")]
        [DataRow("invalid parameter type", "InvalidArguments")]
        [DataRow("other DAB error", "DataApiBuilderError")]
        public async Task ExecuteEntity_DataApiBuilderException_MapsError(string message, string expectedError)
        {
            DataApiBuilderException exception = new(
                message,
                HttpStatusCode.BadRequest,
                DataApiBuilderException.SubStatusCodes.BadRequest);

            CallToolResult result = await ExecuteWithMockedEngineAsync(
                TEST_ENTITY,
                new(),
                null,
                queryOutcome: exception);

            AssertError(result, expectedError);
        }

        /// <summary>
        /// Verifies provider, connection, and unexpected execution failures map to their MCP error classifications.
        /// </summary>
        [DataTestMethod]
        [DataRow("provider failure", "DatabaseError")]
        [DataRow("connection unavailable", "ConnectionError")]
        [DataRow("unexpected", "DatabaseError")]
        public async Task ExecuteEntity_ExecutionException_MapsError(string message, string expectedError)
        {
            Exception exception = message switch
            {
                "provider failure" => new FakeDbException(message),
                "connection unavailable" => new InvalidOperationException(message),
                _ => new Exception(message)
            };

            CallToolResult result = await ExecuteWithMockedEngineAsync(
                TEST_ENTITY,
                new(),
                null,
                queryOutcome: exception);

            AssertError(result, expectedError);
        }

        [TestMethod]
        public async Task ExecuteEntity_Timeout_ReturnsTimeoutError()
        {
            CallToolResult result = await ExecuteWithMockedEngineAsync(
                TEST_ENTITY,
                new(),
                null,
                queryOutcome: new TimeoutException());

            AssertError(result, "TimeoutError");
        }

        /// <summary>
        /// Verifies known SQL Server error numbers produce their user-facing messages and unknown numbers use the database fallback.
        /// </summary>
        [DataTestMethod]
        [DataRow(2812, "not found")]
        [DataRow(8144, "too many parameters")]
        [DataRow(201, "were not supplied")]
        [DataRow(245, "Type conversion failed")]
        [DataRow(229, "Permission denied")]
        [DataRow(262, "Permission denied")]
        [DataRow(50000, "Database error")]
        public async Task ExecuteEntity_SqlException_MapsErrorNumber(int errorNumber, string expectedMessage)
        {
            CallToolResult result = await ExecuteWithMockedEngineAsync(
                TEST_ENTITY,
                new(),
                null,
                queryOutcome: SqlTestHelper.CreateSqlException(errorNumber, "provider error"));

            AssertError(result, "DatabaseError");
            StringAssert.Contains(GetFirstText(result), expectedMessage);
        }

        [TestMethod]
        public async Task ExecuteEntity_BadRequestResult_ReturnsError()
        {
            CallToolResult result = await ExecuteWithMockedEngineAsync(
                TEST_ENTITY,
                new(),
                null,
                queryOutcome: new BadRequestObjectResult("bad input"));

            AssertError(result, "BadRequest");
        }

        [TestMethod]
        public async Task ExecuteEntity_UnauthorizedResult_ReturnsPermissionDenied()
        {
            CallToolResult result = await ExecuteWithMockedEngineAsync(
                TEST_ENTITY,
                new(),
                null,
                queryOutcome: new UnauthorizedObjectResult("denied"));

            AssertError(result, "PermissionDenied");
        }

        [TestMethod]
        public async Task ExecuteEntity_NonJsonResult_IsSerialized()
        {
            CallToolResult result = await ExecuteWithMockedEngineAsync(
                TEST_ENTITY,
                new(),
                null,
                queryOutcome: new OkObjectResult(new { count = 2 }));

            AssertSuccess(result, "POCO results should be serialized.");
            StringAssert.Contains(GetFirstText(result), "count");
        }

        [TestMethod]
        public async Task ExecuteEntity_ObjectJsonResult_IsWrappedInArray()
        {
            using JsonDocument document = JsonDocument.Parse("{\"id\":1}");
            CallToolResult result = await ExecuteWithMockedEngineAsync(
                TEST_ENTITY,
                new(),
                null,
                queryOutcome: new OkObjectResult(document.RootElement.Clone()));

            AssertSuccess(result, "Object JSON results should be wrapped in an array.");
            using JsonDocument response = JsonDocument.Parse(GetFirstText(result));
            JsonElement value = response.RootElement.GetProperty("value");
            Assert.AreEqual(JsonValueKind.Array, value.ValueKind);
            Assert.AreEqual(1, value.GetArrayLength());
            Assert.AreEqual(1, value[0].GetProperty("id").GetInt32());
        }

        [TestMethod]
        public async Task ExecuteEntity_UnknownResult_ReturnsEmptyValue()
        {
            CallToolResult result = await ExecuteWithMockedEngineAsync(
                TEST_ENTITY,
                new(),
                null,
                queryOutcome: new NoContentResult());

            AssertSuccess(result, "Unknown result types should produce an empty value array.");
            StringAssert.Contains(GetFirstText(result), "\"value\": []");
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
            Action<StoredProcedureRequestContext>? captureContext = null,
            object? queryOutcome = null,
            DatabaseObject? metadataObject = null,
            bool includeHttpContext = true,
            bool authorizeOperation = true)
        {
            IServiceProvider sp = BuildServiceProvider(
                entityName: entityName,
                sourceObject: SP_SOURCE_OBJECT,
                sourceType: EntitySourceType.StoredProcedure,
                dbParameters: dbParameters,
                captureContext: captureContext,
                queryOutcome: queryOutcome,
                metadataObject: metadataObject,
                includeHttpContext: includeHttpContext,
                authorizeOperation: authorizeOperation);

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
            Action<StoredProcedureRequestContext>? captureContext = null,
            object? queryOutcome = null,
            DatabaseObject? metadataObject = null,
            bool includeHttpContext = true,
            bool authorizeOperation = true)
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
                .Returns(authorizeOperation);
            services.AddSingleton(mockAuthResolver.Object);

            // Mock HttpContext with anonymous role header
            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = "anonymous";
            IHttpContextAccessor httpContextAccessor = new HttpContextAccessor
            {
                HttpContext = includeHttpContext ? httpContext : null
            };
            services.AddSingleton(httpContextAccessor);

            // Mock metadata provider with DB object
            DatabaseObject dbObject = metadataObject ?? (sourceType == EntitySourceType.StoredProcedure
                ? new DatabaseStoredProcedure("dbo", sourceObject)
                {
                    SourceType = EntitySourceType.StoredProcedure,
                    StoredProcedureDefinition = new StoredProcedureDefinition
                    {
                        Parameters = dbParameters
                    }
                }
                : new DatabaseTable("dbo", sourceObject) { SourceType = EntitySourceType.Table });

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
            if (queryOutcome is Exception exception)
            {
                mockQueryEngine
                    .Setup(x => x.ExecuteAsync(It.IsAny<StoredProcedureRequestContext>(), It.IsAny<string>()))
                    .ThrowsAsync(exception);
            }
            else
            {
                mockQueryEngine
                    .Setup(x => x.ExecuteAsync(It.IsAny<StoredProcedureRequestContext>(), It.IsAny<string>()))
                    .Returns((StoredProcedureRequestContext ctx, string ds) =>
                {
                    captureContext?.Invoke(ctx);
                    IActionResult result;
                    if (queryOutcome is IActionResult configuredResult)
                    {
                        result = configuredResult;
                    }
                    else
                    {
                        using JsonDocument doc = JsonDocument.Parse("[]");
                        result = new OkObjectResult(doc.RootElement.Clone());
                    }

                    return Task.FromResult(result);
                });
            }

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

        private static void AssertError(CallToolResult result, string expectedError)
        {
            Assert.IsTrue(result.IsError == true, GetFirstText(result));
            StringAssert.Contains(GetFirstText(result), expectedError);
        }

        #endregion
    }
}
