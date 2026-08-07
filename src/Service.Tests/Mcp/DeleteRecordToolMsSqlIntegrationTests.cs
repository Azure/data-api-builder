// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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

        #region Success Tests

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

        #endregion

        #region Error Cases

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

        #endregion

        #region Verification Tests

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

            AssertSuccess(readResult, "Follow-up read after delete should succeed.");

            JsonElement root = ParseResultRoot(readResult);
            JsonElement records = GetRecordsArray(root);
            Assert.AreEqual(0, records.GetArrayLength(),
                "Deleted record should not be found in subsequent read.");
        }

        #endregion

        #region Helpers

        /// <summary>
        /// Creates a book record and returns its ID using the centralized helper.
        /// </summary>
        private static async Task<int> CreateBookForDeletion(string title)
        {
            return await CreateTestBook(title);
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

        #endregion
    }
}
