// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Integration tests for UpdateRecordTool against a real MsSql database.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class UpdateRecordToolMsSqlIntegrationTests : McpToolTestBase
    {
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        /// <summary>
        /// Updates an existing book record (id=1) with new title and verifies success.
        /// </summary>
        [TestMethod]
        public async Task UpdateRecord_ValidKeysAndFields_ReturnsSuccess()
        {
            var keys = new Dictionary<string, object> { { "id", 1 } };
            var fields = new Dictionary<string, object> { { "title", "Updated Title via MCP" } };

            CallToolResult result = await ExecuteUpdateAsync("Book", keys, fields);

            AssertSuccess(result, "UpdateRecord should succeed for existing record.");

            JsonElement root = ParseResultRoot(result);
            Assert.AreEqual("Book", root.GetProperty("entity").GetString());
            Assert.IsTrue(root.GetProperty("message").GetString()!.Contains("Successfully updated"),
                "Response message should indicate success.");

            if (root.TryGetProperty("result", out JsonElement resultElement) &&
                resultElement.TryGetProperty("title", out JsonElement titleElement))
            {
                Assert.AreEqual("Updated Title via MCP", titleElement.GetString());
            }
        }

        /// <summary>
        /// Updates publisher_id field of an existing record and verifies the change.
        /// </summary>
        [TestMethod]
        public async Task UpdateRecord_UpdateNumericField_ReturnsUpdatedValue()
        {
            var keys = new Dictionary<string, object> { { "id", 2 } };
            var fields = new Dictionary<string, object> { { "publisher_id", 9999 } };

            CallToolResult result = await ExecuteUpdateAsync("Book", keys, fields);

            AssertSuccess(result, "UpdateRecord should succeed for numeric field update.");

            JsonElement root = ParseResultRoot(result);
            if (root.TryGetProperty("result", out JsonElement resultElement) &&
                resultElement.TryGetProperty("publisher_id", out JsonElement pubElement))
            {
                Assert.AreEqual(9999, pubElement.GetInt32());
            }
        }

        /// <summary>
        /// Validates error scenarios for UpdateRecordTool.
        /// </summary>
        [DataTestMethod]
        [DataRow(99999, "Book", DisplayName = "Non-existent key")]
        public async Task UpdateRecord_NonExistentKey_ReturnsError(int keyId, string entity)
        {
            var keys = new Dictionary<string, object> { { "id", keyId } };
            var fields = new Dictionary<string, object> { { "title", "Ghost Book" } };

            CallToolResult result = await ExecuteUpdateAsync(entity, keys, fields);

            AssertError(result);
        }

        /// <summary>
        /// Attempts to update a non-existent entity, expecting an error.
        /// </summary>
        [TestMethod]
        public async Task UpdateRecord_InvalidEntity_ReturnsError()
        {
            var keys = new Dictionary<string, object> { { "id", 1 } };
            var fields = new Dictionary<string, object> { { "name", "Test" } };

            CallToolResult result = await ExecuteUpdateAsync("NonExistentEntity", keys, fields);

            AssertError(result, "NonExistentEntity");
        }

        /// <summary>
        /// Attempts to update with no arguments, expecting an error.
        /// </summary>
        [TestMethod]
        public async Task UpdateRecord_NoArguments_ReturnsError()
        {
            IServiceProvider serviceProvider = BuildMutationServiceProvider();
            UpdateRecordTool tool = new();

            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            AssertError(result);
        }

        /// <summary>
        /// Attempts to update with null key value, expecting an error.
        /// </summary>
        [TestMethod]
        public async Task UpdateRecord_NullKeyValue_ReturnsError()
        {
            var args = new Dictionary<string, object?>
            {
                { "entity", "Book" },
                { "keys", new Dictionary<string, object?> { { "id", null } } },
                { "fields", new Dictionary<string, object> { { "title", "Test" } } }
            };

            IServiceProvider serviceProvider = BuildMutationServiceProvider();
            UpdateRecordTool tool = new();

            string argsJson = JsonSerializer.Serialize(args);
            using JsonDocument arguments = JsonDocument.Parse(argsJson);

            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);

            AssertError(result);
        }

        private static async Task<CallToolResult> ExecuteUpdateAsync(
            string entity,
            Dictionary<string, object> keys,
            Dictionary<string, object> fields)
        {
            IServiceProvider serviceProvider = BuildMutationServiceProvider();
            UpdateRecordTool tool = new();

            var args = new Dictionary<string, object?>
            {
                { "entity", entity },
                { "keys", keys },
                { "fields", fields }
            };

            return await ExecuteToolAsync(tool, serviceProvider, args);
        }
    }
}
