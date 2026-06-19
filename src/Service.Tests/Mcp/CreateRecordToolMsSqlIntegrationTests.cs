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
    /// Integration tests for CreateRecordTool against a real MsSql database.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class CreateRecordToolMsSqlIntegrationTests : McpToolTestBase
    {
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        #region Success Tests

        /// <summary>
        /// Creates a new book record with valid data and verifies success. Cleans up after.
        /// </summary>
        [TestMethod]
        public async Task CreateRecord_ValidData_ReturnsSuccess()
        {
            var data = new Dictionary<string, object>
            {
                { "title", "Integration Test Book" },
                { "publisher_id", 1234 }
            };

            CallToolResult result = await ExecuteCreateAsync("Book", data);

            AssertSuccess(result, "CreateRecord should succeed with valid data.");

            JsonElement root = ParseResultRoot(result);
            Assert.AreEqual("Book", root.GetProperty("entity").GetString());
            Assert.IsTrue(root.GetProperty("message").GetString()!.Contains("Successfully created"),
                "Response message should indicate success.");

            int createdId = ExtractCreatedBookId(root);
            await DeleteTestBook(createdId);
        }

        /// <summary>
        /// Creates a record and verifies the returned record contains the inserted data. Cleans up after.
        /// </summary>
        [TestMethod]
        public async Task CreateRecord_ReturnsCreatedData()
        {
            var data = new Dictionary<string, object>
            {
                { "title", "Verify Created Data" },
                { "publisher_id", 2345 }
            };

            CallToolResult result = await ExecuteCreateAsync("Book", data);

            AssertSuccess(result, "CreateRecord should succeed.");

            JsonElement root = ParseResultRoot(result);

            Assert.IsTrue(root.TryGetProperty("result", out JsonElement resultElement),
                "Response should contain 'result'.");
            Assert.AreEqual(JsonValueKind.Object, resultElement.ValueKind,
                "Create response result should be an object.");
            Assert.IsTrue(resultElement.TryGetProperty("value", out JsonElement valueArray),
                "Create response result should contain 'value'.");
            Assert.AreEqual(JsonValueKind.Array, valueArray.ValueKind);
            Assert.IsTrue(valueArray.GetArrayLength() > 0, "Value array should contain the created record.");

            JsonElement created = valueArray[0];
            Assert.AreEqual("Verify Created Data", created.GetProperty("title").GetString());
            Assert.AreEqual(2345, created.GetProperty("publisher_id").GetInt32());
            Assert.IsTrue(created.TryGetProperty("id", out _), "Created record should have an auto-generated id.");

            int createdId = created.GetProperty("id").GetInt32();
            await DeleteTestBook(createdId);
        }

        #endregion

        #region Error Cases

        /// <summary>
        /// Validates error scenarios for CreateRecordTool.
        /// </summary>
        [DataTestMethod]
        [DataRow("NonExistentEntity", "NonExistentEntity", DisplayName = "Invalid entity")]
        public async Task CreateRecord_InvalidEntity_ReturnsError(string entity, string expectedSubstring)
        {
            var data = new Dictionary<string, object> { { "name", "Test" } };

            CallToolResult result = await ExecuteCreateAsync(entity, data);

            AssertError(result, expectedSubstring);
        }

        /// <summary>
        /// Attempts to create a record without providing any arguments.
        /// </summary>
        [TestMethod]
        public async Task CreateRecord_NoArguments_ReturnsError()
        {
            IServiceProvider serviceProvider = BuildMutationServiceProvider();
            CreateRecordTool tool = new();

            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            AssertError(result);
        }

        /// <summary>
        /// Attempts to create a record with missing required NOT NULL field (title).
        /// </summary>
        [TestMethod]
        public async Task CreateRecord_MissingRequiredField_ReturnsError()
        {
            var data = new Dictionary<string, object>
            {
                { "publisher_id", 1234 }
            };

            CallToolResult result = await ExecuteCreateAsync("Book", data);

            AssertError(result, message: "CreateRecord should fail when missing required NOT NULL fields.");
        }

        #endregion

        #region Helpers

        private static async Task<CallToolResult> ExecuteCreateAsync(string entity, Dictionary<string, object> data)
        {
            IServiceProvider serviceProvider = BuildMutationServiceProvider();
            CreateRecordTool tool = new();

            var args = new Dictionary<string, object?>
            {
                { "entity", entity },
                { "data", data }
            };

            return await ExecuteToolAsync(tool, serviceProvider, args);
        }

        #endregion
    }
}
