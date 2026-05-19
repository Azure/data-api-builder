// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Core.Resolvers.Factories;
using Azure.DataApiBuilder.Core.Services.Cache;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;
using ZiggyCreatures.Caching.Fusion;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Integration tests for ExecuteEntityTool's parameter validation and default application.
    /// Verifies the end-to-end behavior after the fix:
    /// - Parameters are validated against StoredProcedureDefinition.Parameters (DB metadata),
    ///   not config-side parameters alone.
    /// - Config defaults are applied from ParameterDefinition.HasConfigDefault/ConfigDefaultValue
    ///   for any parameter the user did not supply.
    ///
    /// Scenarios (reuse SPs already defined in DatabaseSchema-MsSql.sql / dab-config.MsSql.json):
    ///   - GetBook   -> SP get_book_by_id(@id int), no config params.
    ///   - InsertBook -> SP insert_book(@title, @publisher_id), config defaults applied.
    ///   - GetBooks  -> SP get_books, zero params.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class ExecuteEntityToolMsSqlIntegrationTests : SqlTestBase
    {
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        /// <summary>
        /// Data-driven test validating successful SP execution across multiple parameter scenarios.
        /// Each row exercises a distinct code path in ExecuteEntityTool:
        /// - DB-discovered param with no config entry (validates the fix for param validation).
        /// - Config defaults applied when user omits params.
        /// - User-supplied params override config defaults.
        /// - Zero-param SP succeeds with no parameters.
        /// </summary>
        [DataTestMethod]
        [DataRow("GetBook", "{\"id\": 1}", DisplayName = "DB-discovered param accepted (no config entry)")]
        [DataRow("InsertBook", null, DisplayName = "Config defaults applied when no params supplied")]
        [DataRow("InsertBook", "{\"title\": \"Integration Test Book\", \"publisher_id\": 2345}", DisplayName = "User-supplied params override defaults")]
        [DataRow("GetBooks", null, DisplayName = "Zero-param SP succeeds")]
        public async Task ExecuteEntity_SuccessfulExecution(string entityName, string parametersJson)
        {
            Dictionary<string, object> parameters = parametersJson != null
                ? JsonSerializer.Deserialize<Dictionary<string, object>>(parametersJson)
                : null;

            CallToolResult result = await ExecuteEntityAsync(entityName, parameters);

            AssertSuccess(result,
                $"execute_entity failed for entity '{entityName}' with params '{parametersJson}'.");

            // Parse response and verify structure
            string content = GetFirstTextContent(result);
            Assert.IsFalse(string.IsNullOrWhiteSpace(content), $"Expected non-empty result for entity '{entityName}'.");

            using JsonDocument doc = JsonDocument.Parse(content);
            JsonElement root = doc.RootElement;
            Assert.AreEqual(entityName, root.GetProperty("entity").GetString());
            Assert.AreEqual("Stored procedure executed successfully", root.GetProperty("message").GetString());
        }

        /// <summary>
        /// Verify that GetBook with id=1 returns the actual book record from the database.
        /// This ensures the parameter value is correctly passed to the stored procedure.
        /// </summary>
        [TestMethod]
        public async Task ExecuteEntity_GetBookById_ReturnsMatchingRecord()
        {
            Dictionary<string, object> parameters = new() { { "id", 1 } };
            CallToolResult result = await ExecuteEntityAsync("GetBook", parameters);

            AssertSuccess(result, "GetBook with id=1 should succeed.");

            using JsonDocument doc = JsonDocument.Parse(GetFirstTextContent(result));
            JsonElement root = doc.RootElement;

            // Verify the value property contains the SP result with at least one record with id=1.
            // SqlResponseHelpers.OkResponse wraps results in { value: [...] }, and
            // BuildExecuteSuccessResponse serializes that as-is into the "value" field.
            Assert.IsTrue(root.TryGetProperty("value", out JsonElement valueWrapper), "Response should contain 'value' property.");

            // The value may be the wrapper object { "value": [...] } or directly an array.
            JsonElement records = valueWrapper.ValueKind == JsonValueKind.Object
                ? valueWrapper.GetProperty("value")
                : valueWrapper;

            Assert.AreEqual(JsonValueKind.Array, records.ValueKind);
            Assert.IsTrue(records.GetArrayLength() > 0, "Expected at least one book record.");
            Assert.AreEqual(1, records[0].GetProperty("id").GetInt32());
        }

        /// <summary>
        /// Verify that InsertBook with no user params applies config defaults (title="randomX", publisher_id="1234").
        /// The SP inserts using those defaults. We verify the tool reports success (the SP executed without error).
        /// </summary>
        [TestMethod]
        public async Task ExecuteEntity_InsertBookWithDefaults_ExecutesSuccessfully()
        {
            CallToolResult result = await ExecuteEntityAsync("InsertBook", parameters: null);

            AssertSuccess(result, "InsertBook with config defaults should succeed.");

            using JsonDocument doc = JsonDocument.Parse(GetFirstTextContent(result));
            JsonElement root = doc.RootElement;
            Assert.AreEqual("InsertBook", root.GetProperty("entity").GetString());
        }

        /// <summary>
        /// Reject a parameter name that does not exist in the DB metadata.
        /// Validation against StoredProcedureDefinition.Parameters should catch this.
        /// </summary>
        [DataTestMethod]
        [DataRow("GetBook", "nonexistent_param", "value", DisplayName = "Rejects unknown param on single-param SP")]
        [DataRow("GetBooks", "bogus", "123", DisplayName = "Rejects any param on zero-param SP")]
        public async Task ExecuteEntity_InvalidParamName_ReturnsError(string entityName, string paramName, string paramValue)
        {
            Dictionary<string, object> parameters = new() { { paramName, paramValue } };
            CallToolResult result = await ExecuteEntityAsync(entityName, parameters);

            Assert.IsTrue(result.IsError == true,
                $"execute_entity should reject parameter '{paramName}' not in DB metadata for '{entityName}'.");
            string content = GetFirstTextContent(result);
            StringAssert.Contains(content, paramName);
        }

        private static async Task<CallToolResult> ExecuteEntityAsync(string entityName, Dictionary<string, object> parameters)
        {
            IServiceProvider serviceProvider = BuildExecuteEntityServiceProvider();
            ExecuteEntityTool tool = new();

            var args = new Dictionary<string, object> { { "entity", entityName } };
            if (parameters != null)
            {
                args["parameters"] = parameters;
            }

            string argsJson = JsonSerializer.Serialize(args);
            using JsonDocument arguments = JsonDocument.Parse(argsJson);

            return await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);
        }

        /// <summary>
        /// Builds a service provider that wires ExecuteEntityTool to the shared fixture's
        /// real ISqlMetadataProvider, real IQueryEngine (SqlQueryEngine), and real
        /// authorization resolver, with a DefaultHttpContext carrying the anonymous role header.
        /// Uses the RuntimeConfigProvider from the WebApplicationFactory so that the datasource
        /// name matches what the real MsSqlQueryExecutor was initialized with.
        /// </summary>
        private static IServiceProvider BuildExecuteEntityServiceProvider()
        {
            ServiceCollection services = new();

            // Use the RuntimeConfigProvider from the WebApplicationFactory — this is the same
            // provider that initialized _queryExecutor, so its DefaultDataSourceName matches
            // the key in _queryExecutor.ConnectionStringBuilders.
            RuntimeConfigProvider configProvider = _application.Services.GetRequiredService<RuntimeConfigProvider>();
            services.AddSingleton(configProvider);

            // Real metadata-provider factory backed by the shared fixture's live provider.
            services.AddSingleton(_metadataProviderFactory.Object);

            // Real authorization resolver wired by SqlTestBase against the live config + provider.
            services.AddSingleton(_authorizationResolver);

            // Real HttpContext carrying the anonymous role header and a ClaimsPrincipal
            // with the anonymous role claim so that AuthorizationResolver.IsValidRoleContext
            // (which calls httpContext.User.IsInRole) returns true.
            DefaultHttpContext httpContext = new();
            httpContext.Request.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER] = AuthorizationResolver.ROLE_ANONYMOUS;
            ClaimsIdentity identity = new(
                authenticationType: "TestAuth",
                nameType: null,
                roleType: AuthenticationOptions.ROLE_CLAIM_TYPE);
            identity.AddClaim(new Claim(AuthenticationOptions.ROLE_CLAIM_TYPE, AuthorizationResolver.ROLE_ANONYMOUS));
            httpContext.User = new ClaimsPrincipal(identity);
            IHttpContextAccessor httpContextAccessor = new HttpContextAccessor { HttpContext = httpContext };
            services.AddSingleton(httpContextAccessor);

            // Build a real SqlQueryEngine using the shared fixtures.
            Mock<IFusionCache> cache = new();
            DabCacheService cacheService = new(cache.Object, logger: null, httpContextAccessor);

            SqlQueryEngine queryEngine = new(
                _queryManagerFactory.Object,
                _metadataProviderFactory.Object,
                httpContextAccessor,
                _authorizationResolver,
                _gqlFilterParser,
                new Mock<ILogger<IQueryEngine>>().Object,
                configProvider,
                cacheService);

            // Wrap in a mock IQueryEngineFactory that returns the real engine.
            Mock<IQueryEngineFactory> queryEngineFactory = new();
            queryEngineFactory
                .Setup(f => f.GetQueryEngine(It.IsAny<DatabaseType>()))
                .Returns(queryEngine);
            services.AddSingleton(queryEngineFactory.Object);

            services.AddLogging();

            return services.BuildServiceProvider();
        }

        private static string GetFirstTextContent(CallToolResult result)
        {
            if (result.Content is null || result.Content.Count == 0)
            {
                return string.Empty;
            }

            return result.Content[0] is TextContentBlock textBlock
                ? textBlock.Text ?? string.Empty
                : string.Empty;
        }

        private static void AssertSuccess(CallToolResult result, string message)
        {
            Assert.IsTrue(result.IsError != true,
                $"{message} Content: {GetFirstTextContent(result)}");
        }
    }
}
