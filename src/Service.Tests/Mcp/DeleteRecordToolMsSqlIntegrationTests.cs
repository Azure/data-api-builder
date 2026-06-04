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
    /// Integration tests for DeleteRecordTool against a real MsSql database.
    /// Tests that delete records first create a record to avoid interfering with seed data.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class DeleteRecordToolMsSqlIntegrationTests : McpToolTestBase
    {
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        /// <summary>
        /// Creates a book then deletes it, verifying the delete succeeds.
        /// </summary>
        [TestMethod]
        public async Task DeleteRecord_ExistingRecord_ReturnsSuccess()
        {
            int createdId = await CreateBookForDeletion("Delete Me Book");

            var keys = new Dictionary<string, object> { { "id", createdId } };
            CallToolResult result = await ExecuteDeleteAsync("Book", keys);

            AssertSuccess(result, "DeleteRecord should succeed for existing record.");
            Assert.IsFalse(string.IsNullOrWhiteSpace(GetFirstTextContent(result)));
        }

        /// <summary>
        /// Attempts to delete a record with a non-existent key, expecting an error.
        /// </summary>
        [TestMethod]
        public async Task DeleteRecord_NonExistentKey_ReturnsError()
        {
            var keys = new Dictionary<string, object> { { "id", 99999 } };

            CallToolResult result = await ExecuteDeleteAsync("Book", keys);

            AssertError(result);
        }

        /// <summary>
        /// Attempts to delete from a non-existent entity, expecting an error.
        /// </summary>
        [TestMethod]
        public async Task DeleteRecord_InvalidEntity_ReturnsError()
        {
            var keys = new Dictionary<string, object> { { "id", 1 } };

            CallToolResult result = await ExecuteDeleteAsync("NonExistentEntity", keys);

            AssertError(result, "NonExistentEntity");
        }

        /// <summary>
        /// Attempts to delete with no arguments, expecting an error.
        /// </summary>
        [TestMethod]
        public async Task DeleteRecord_NoArguments_ReturnsError()
        {
            IServiceProvider serviceProvider = BuildMutationServiceProvider();
            DeleteRecordTool tool = new();

            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            AssertError(result);
        }

        /// <summary>
        /// Attempts to delete with null key value, expecting an error.
        /// </summary>
        [TestMethod]
        public async Task DeleteRecord_NullKeyValue_ReturnsError()
        {
            var args = new Dictionary<string, object?>
            {
                { "entity", "Book" },
                { "keys", new Dictionary<string, object?> { { "id", null } } }
            };

            IServiceProvider serviceProvider = BuildMutationServiceProvider();
            DeleteRecordTool tool = new();

            string argsJson = JsonSerializer.Serialize(args);
            using JsonDocument arguments = JsonDocument.Parse(argsJson);

            CallToolResult result = await tool.ExecuteAsync(arguments, serviceProvider, CancellationToken.None);

            AssertError(result);
        }

        /// <summary>
        /// Verifies that after successful deletion, the record is no longer accessible.
        /// </summary>
        [TestMethod]
        public async Task DeleteRecord_ThenRead_RecordNotFound()
        {
            int createdId = await CreateBookForDeletion("Delete Then Verify Book");

            // Delete the book
            var keys = new Dictionary<string, object> { { "id", createdId } };
            CallToolResult deleteResult = await ExecuteDeleteAsync("Book", keys);
            AssertSuccess(deleteResult, "Delete should succeed.");

            // Verify it's gone via read
            IServiceProvider readProvider = BuildQueryServiceProvider();
            ReadRecordsTool readTool = new();

            var readArgs = new Dictionary<string, object?> { { "entity", "Book" }, { "filter", $"id eq {createdId}" } };
            CallToolResult readResult = await ExecuteToolAsync(readTool, readProvider, readArgs);

            if (readResult.IsError != true)
            {
                JsonElement root = ParseResultRoot(readResult);
                JsonElement records = root.GetProperty("result").GetProperty("value");
                Assert.AreEqual(0, records.GetArrayLength(),
                    "Deleted record should not be found in subsequent read.");
            }
        }

        /// <summary>
        /// Creates a book record using the CreateRecordTool and returns its ID.
        /// </summary>
        private static async Task<int> CreateBookForDeletion(string title)
        {
            IServiceProvider serviceProvider = BuildMutationServiceProvider();
            CreateRecordTool createTool = new();

            var args = new Dictionary<string, object?>
            {
                { "entity", "Book" },
                { "data", new Dictionary<string, object> { { "title", title }, { "publisher_id", 1234 } } }
            };

            CallToolResult createResult = await ExecuteToolAsync(createTool, serviceProvider, args);
            Assert.IsTrue(createResult.IsError != true, $"Setup: Failed to create book for deletion test. {GetFirstTextContent(createResult)}");

            JsonElement root = ParseResultRoot(createResult);
            if (root.TryGetProperty("result", out JsonElement resultElement) &&
                resultElement.ValueKind == JsonValueKind.Object &&
                resultElement.TryGetProperty("value", out JsonElement valueArray) &&
                valueArray.ValueKind == JsonValueKind.Array &&
                valueArray.GetArrayLength() > 0)
            {
                return valueArray[0].GetProperty("id").GetInt32();
            }

            Assert.Fail("Could not extract ID from created book record.");
            return -1;
        }

        private static async Task<CallToolResult> ExecuteDeleteAsync(string entity, Dictionary<string, object> keys)
        {
            IServiceProvider serviceProvider = BuildMutationServiceProvider();
            DeleteRecordTool tool = new();

            var args = new Dictionary<string, object?>
            {
                { "entity", entity },
                { "keys", keys }
            };

            return await ExecuteToolAsync(tool, serviceProvider, args);
        }
    }
}
