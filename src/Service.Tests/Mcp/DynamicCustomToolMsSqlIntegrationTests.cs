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
using Azure.DataApiBuilder.Mcp.Core;
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
    /// Integration tests for DynamicCustomTool's parameter validation and default application.
    /// Verifies the same execution-time fixes applied to ExecuteEntityTool also work correctly
    /// for per-entity custom tools:
    /// - Parameters are validated against StoredProcedureDefinition.Parameters (DB metadata).
    /// - Config defaults are applied from ParameterDefinition.HasConfigDefault/ConfigDefaultValue.
    ///
    /// Uses SPs defined in DatabaseSchema-MsSql.sql / dab-config.MsSql.json:
    ///   - GetBook   -> SP get_book_by_id(@id int)
    ///   - InsertBook -> SP insert_book(@title, @publisher_id), config defaults title=randomX, publisher_id=1234
    ///   - GetBooks  -> SP get_books, zero params
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class DynamicCustomToolMsSqlIntegrationTests : SqlTestBase
    {
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        /// <summary>
        /// Data-driven test validating successful SP execution via DynamicCustomTool.
        /// </summary>
        [DataTestMethod]
        [DataRow("GetBook", "{\"id\": 1}", DisplayName = "DB-discovered param accepted")]
        [DataRow("InsertBook", null, DisplayName = "Config defaults applied when no params supplied")]
        [DataRow("InsertBook", "{\"title\": \"Custom Tool Test\", \"publisher_id\": 2345}", DisplayName = "User params override defaults")]
        [DataRow("GetBooks", null, DisplayName = "Zero-param SP succeeds")]
        public async Task DynamicCustomTool_SuccessfulExecution(string entityName, string? parametersJson)
        {
            Dictionary<string, object>? parameters = parametersJson != null
                ? JsonSerializer.Deserialize<Dictionary<string, object>>(parametersJson)
                : null;

            CallToolResult result = await ExecuteCustomToolAsync(entityName, parameters);

            AssertSuccess(result,
                $"Custom tool failed for entity '{entityName}' with params '{parametersJson}'.");

            string content = GetFirstTextContent(result);
            Assert.IsFalse(string.IsNullOrWhiteSpace(content), $"Expected non-empty result for entity '{entityName}'.");

            using JsonDocument doc = JsonDocument.Parse(content);
            JsonElement root = doc.RootElement;
            Assert.AreEqual(entityName, root.GetProperty("entity").GetString());
            Assert.AreEqual("Stored procedure executed successfully", root.GetProperty("message").GetString());
        }

        /// <summary>
        /// Verify GetBook with id=1 returns a matching record through DynamicCustomTool.
        /// </summary>
        [TestMethod]
        public async Task DynamicCustomTool_GetBookById_ReturnsMatchingRecord()
        {
            Dictionary<string, object> parameters = new() { { "id", 1 } };
            CallToolResult result = await ExecuteCustomToolAsync("GetBook", parameters);

            AssertSuccess(result, "GetBook with id=1 should succeed.");

            using JsonDocument doc = JsonDocument.Parse(GetFirstTextContent(result));
            JsonElement root = doc.RootElement;

            Assert.IsTrue(root.TryGetProperty("value", out JsonElement valueWrapper), "Response should contain 'value' property.");

            JsonElement records = valueWrapper.ValueKind == JsonValueKind.Object
                ? valueWrapper.GetProperty("value")
                : valueWrapper;

            Assert.AreEqual(JsonValueKind.Array, records.ValueKind);
            Assert.IsTrue(records.GetArrayLength() > 0, "Expected at least one book record.");
            Assert.AreEqual(1, records[0].GetProperty("id").GetInt32());
        }

        /// <summary>
        /// Reject a parameter name that does not exist in the DB metadata.
        /// </summary>
        [DataTestMethod]
        [DataRow("GetBook", "nonexistent_param", "value", DisplayName = "Rejects unknown param on single-param SP")]
        [DataRow("GetBooks", "bogus", "123", DisplayName = "Rejects any param on zero-param SP")]
        public async Task DynamicCustomTool_InvalidParamName_ReturnsError(string entityName, string paramName, string paramValue)
        {
            Dictionary<string, object> parameters = new() { { paramName, paramValue } };
            CallToolResult result = await ExecuteCustomToolAsync(entityName, parameters);

            Assert.IsTrue(result.IsError == true,
                $"Custom tool should reject parameter '{paramName}' not in DB metadata for '{entityName}'.");
            string content = GetFirstTextContent(result);
            StringAssert.Contains(content, paramName);
        }

        /// <summary>
        /// Executes a DynamicCustomTool for the given entity using the shared test fixture.
        /// </summary>
        private static async Task<CallToolResult> ExecuteCustomToolAsync(string entityName, Dictionary<string, object>? parameters)
        {
            IServiceProvider serviceProvider = BuildServiceProvider();

            // Resolve the entity config from the runtime config
            RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
            RuntimeConfig config = configProvider.GetConfig();
            Entity entity = config.Entities[entityName];

            DynamicCustomTool tool = new(entityName, entity);

            // DynamicCustomTool expects parameters as top-level JSON properties (no "entity" wrapper)
            string argsJson = parameters != null
                ? JsonSerializer.Serialize(parameters)
                : "{}";
            using JsonDocument arguments = JsonDocument.Parse(argsJson);

            return await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);
        }

        /// <summary>
        /// Builds a service provider wired to the shared fixture's real providers.
        /// Uses the same pattern as ExecuteEntityToolMsSqlIntegrationTests.
        /// </summary>
        private static IServiceProvider BuildServiceProvider()
        {
            ServiceCollection services = new();

            RuntimeConfigProvider configProvider = _application.Services.GetRequiredService<RuntimeConfigProvider>();
            services.AddSingleton(configProvider);

            services.AddSingleton(_metadataProviderFactory.Object);
            services.AddSingleton(_authorizationResolver);

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
