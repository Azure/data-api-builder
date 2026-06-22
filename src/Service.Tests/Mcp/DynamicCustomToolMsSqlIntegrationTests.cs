// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;

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
    public class DynamicCustomToolMsSqlIntegrationTests : McpToolTestBase
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

            AssertError(result, paramName,
                $"Custom tool should reject parameter '{paramName}' not in DB metadata for '{entityName}'.");
        }

        #region Schema Alignment Integration Tests

        /// <summary>
        /// Validates that InitializeMetadata maps DB parameter types to JSON Schema types.
        /// </summary>
        [DataTestMethod]
        [DataRow("GetBook", "id", "integer", DisplayName = "int param maps to integer")]
        [DataRow("InsertBook", "title", "string", DisplayName = "varchar param maps to string")]
        [DataRow("InsertBook", "publisher_id", "integer", DisplayName = "int param maps to integer (multi-param SP)")]
        public void InitializeMetadata_SchemaReflectsDbParameterTypes(string entityName, string paramName, string expectedType)
        {
            IServiceProvider serviceProvider = BuildQueryServiceProvider();
            RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
            Entity entity = configProvider.GetConfig().Entities[entityName];

            DynamicCustomTool tool = new(entityName, entity);
            tool.InitializeMetadata(serviceProvider);

            JsonElement properties = tool.GetToolMetadata().InputSchema.GetProperty("properties");

            Assert.IsTrue(properties.TryGetProperty(paramName, out JsonElement paramProp),
                $"Schema should contain '{paramName}' property.");
            Assert.AreEqual(expectedType, paramProp.GetProperty("type").GetString(),
                $"'{paramName}' should map to JSON Schema type '{expectedType}'.");
        }

        /// <summary>
        /// Validates that zero-param SP produces an empty properties object.
        /// </summary>
        [TestMethod]
        public void InitializeMetadata_ZeroParamSP_HasEmptyProperties()
        {
            IServiceProvider serviceProvider = BuildQueryServiceProvider();
            RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
            Entity entity = configProvider.GetConfig().Entities["GetBooks"];

            DynamicCustomTool tool = new("GetBooks", entity);
            tool.InitializeMetadata(serviceProvider);

            JsonElement properties = tool.GetToolMetadata().InputSchema.GetProperty("properties");

            int paramCount = 0;
            foreach (JsonProperty _ in properties.EnumerateObject())
            {
                paramCount++;
            }

            Assert.AreEqual(0, paramCount, "Zero-param SP should produce empty properties.");
        }

        /// <summary>
        /// Validates that config default values appear in parameter descriptions.
        /// </summary>
        [DataTestMethod]
        [DataRow("InsertBook", "title", "randomX", DisplayName = "title description includes default 'randomX'")]
        [DataRow("InsertBook", "publisher_id", "1234", DisplayName = "publisher_id description includes default '1234'")]
        public void InitializeMetadata_DescriptionIncludesConfigDefaults(string entityName, string paramName, string expectedDefault)
        {
            IServiceProvider serviceProvider = BuildQueryServiceProvider();
            RuntimeConfigProvider configProvider = serviceProvider.GetRequiredService<RuntimeConfigProvider>();
            Entity entity = configProvider.GetConfig().Entities[entityName];

            DynamicCustomTool tool = new(entityName, entity);
            tool.InitializeMetadata(serviceProvider);

            JsonElement properties = tool.GetToolMetadata().InputSchema.GetProperty("properties");
            string description = properties.GetProperty(paramName).GetProperty("description").GetString()!;

            StringAssert.Contains(description, expectedDefault,
                $"'{paramName}' description should mention config default '{expectedDefault}'.");
        }

        #endregion

        /// <summary>
        /// Executes a DynamicCustomTool for the given entity using the shared test fixture.
        /// </summary>
        private static async Task<CallToolResult> ExecuteCustomToolAsync(string entityName, Dictionary<string, object>? parameters)
        {
            IServiceProvider serviceProvider = BuildQueryServiceProvider();

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
    }
}
