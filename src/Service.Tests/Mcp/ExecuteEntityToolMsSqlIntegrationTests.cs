// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Integration tests for ExecuteEntityTool's parameter validation and default application.
    /// Verifies:
    /// - Parameters validated against StoredProcedureDefinition.Parameters (DB metadata).
    /// - Config defaults applied from ParameterDefinition.HasConfigDefault/ConfigDefaultValue.
    ///
    /// Uses SPs defined in DatabaseSchema-MsSql.sql / dab-config.MsSql.json:
    ///   - GetBook   -> SP get_book_by_id(@id int), no config params.
    ///   - InsertBook -> SP insert_book(@title, @publisher_id), config defaults applied.
    ///   - GetBooks  -> SP get_books, zero params.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class ExecuteEntityToolMsSqlIntegrationTests : McpToolTestBase
    {
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        /// <summary>
        /// Data-driven test validating successful SP execution across multiple parameter scenarios.
        /// </summary>
        [DataTestMethod]
        [DataRow("GetBook", "{\"id\": 1}", DisplayName = "DB-discovered param accepted (no config entry)")]
        [DataRow("InsertBook", null, DisplayName = "Config defaults applied when no params supplied")]
        [DataRow("InsertBook", "{\"title\": \"Integration Test Book\", \"publisher_id\": 2345}", DisplayName = "User-supplied params override defaults")]
        [DataRow("GetBooks", null, DisplayName = "Zero-param SP succeeds")]
        public async Task ExecuteEntity_SuccessfulExecution(string entityName, string? parametersJson)
        {
            Dictionary<string, object>? parameters = parametersJson != null
                ? JsonSerializer.Deserialize<Dictionary<string, object>>(parametersJson)
                : null;

            CallToolResult result = await ExecuteEntityAsync(entityName, parameters);

            AssertSuccess(result,
                $"execute_entity failed for entity '{entityName}' with params '{parametersJson}'.");

            string content = GetFirstTextContent(result);
            Assert.IsFalse(string.IsNullOrWhiteSpace(content), $"Expected non-empty result for entity '{entityName}'.");

            using JsonDocument doc = JsonDocument.Parse(content);
            JsonElement root = doc.RootElement;
            Assert.AreEqual(entityName, root.GetProperty("entity").GetString());
            Assert.AreEqual("Stored procedure executed successfully", root.GetProperty("message").GetString());
        }

        /// <summary>
        /// Verify that GetBook with id=1 returns the actual book record from the database.
        /// </summary>
        [TestMethod]
        public async Task ExecuteEntity_GetBookById_ReturnsMatchingRecord()
        {
            Dictionary<string, object> parameters = new() { { "id", 1 } };
            CallToolResult result = await ExecuteEntityAsync("GetBook", parameters);

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
        /// Verify that InsertBook with no user params applies config defaults.
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
        /// </summary>
        [DataTestMethod]
        [DataRow("GetBook", "nonexistent_param", "value", DisplayName = "Rejects unknown param on single-param SP")]
        [DataRow("GetBooks", "bogus", "123", DisplayName = "Rejects any param on zero-param SP")]
        public async Task ExecuteEntity_InvalidParamName_ReturnsError(string entityName, string paramName, string paramValue)
        {
            Dictionary<string, object> parameters = new() { { paramName, paramValue } };
            CallToolResult result = await ExecuteEntityAsync(entityName, parameters);

            AssertError(result, paramName,
                $"execute_entity should reject parameter '{paramName}' not in DB metadata for '{entityName}'.");
        }

        private static async Task<CallToolResult> ExecuteEntityAsync(string entityName, Dictionary<string, object>? parameters)
        {
            IServiceProvider serviceProvider = BuildQueryServiceProvider();
            ExecuteEntityTool tool = new();

            var args = new Dictionary<string, object?> { { "entity", entityName } };
            if (parameters != null)
            {
                args["parameters"] = parameters;
            }

            return await ExecuteToolAsync(tool, serviceProvider, args);
        }
    }
}
