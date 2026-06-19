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
    /// Integration tests for ReadRecordsTool against a real MsSql database.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class ReadRecordsToolMsSqlIntegrationTests : McpToolTestBase
    {
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        #region Tests

        /// <summary>
        /// Reads all records from the Book entity without any filters.
        /// </summary>
        [TestMethod]
        public async Task ReadRecords_AllBooks_ReturnsResults()
        {
            CallToolResult result = await ExecuteReadAsync("Book");

            AssertSuccess(result, "ReadRecords for Book should succeed.");

            JsonElement root = ParseResultRoot(result);
            Assert.AreEqual("Book", root.GetProperty("entity").GetString());

            JsonElement records = GetRecordsArray(root);
            Assert.AreEqual(JsonValueKind.Array, records.ValueKind);
            Assert.IsTrue(records.GetArrayLength() > 0, "Expected at least one book record.");
        }

        /// <summary>
        /// Reads records with a select clause to retrieve only specific fields.
        /// </summary>
        [TestMethod]
        public async Task ReadRecords_WithSelect_ReturnsSelectedFields()
        {
            CallToolResult result = await ExecuteReadAsync("Book", select: "id,title");

            AssertSuccess(result, "ReadRecords with select should succeed.");

            JsonElement root = ParseResultRoot(result);
            JsonElement records = GetRecordsArray(root);
            JsonElement firstRecord = records[0];
            Assert.IsTrue(firstRecord.TryGetProperty("id", out _), "Expected 'id' field in result.");
            Assert.IsTrue(firstRecord.TryGetProperty("title", out _), "Expected 'title' field in result.");
        }

        /// <summary>
        /// Reads records with an OData filter expression and verifies filtered results are returned.
        /// </summary>
        [TestMethod]
        public async Task ReadRecords_WithFilter_ReturnsFilteredResults()
        {
            CallToolResult result = await ExecuteReadAsync("Book", select: "id,title", filter: "id gt 5");

            AssertSuccess(result, "ReadRecords with filter should succeed.");

            JsonElement root = ParseResultRoot(result);
            JsonElement records = GetRecordsArray(root);
            Assert.IsTrue(records.GetArrayLength() > 0, "Expected filtered results.");

            foreach (JsonElement record in records.EnumerateArray())
            {
                Assert.IsTrue(record.GetProperty("id").GetInt32() > 5,
                    "All filtered records should have id > 5.");
            }
        }

        /// <summary>
        /// Reads records with orderby to verify sorting.
        /// </summary>
        [TestMethod]
        public async Task ReadRecords_WithOrderBy_ReturnsSortedResults()
        {
            CallToolResult result = await ExecuteReadAsync("Book", select: "id,title", orderby: new[] { "id desc" });

            AssertSuccess(result, "ReadRecords with orderby should succeed.");

            JsonElement root = ParseResultRoot(result);
            JsonElement records = GetRecordsArray(root);
            Assert.IsTrue(records.GetArrayLength() > 1, "Expected multiple records for ordering test.");

            int previousId = int.MaxValue;
            foreach (JsonElement record in records.EnumerateArray())
            {
                int currentId = record.GetProperty("id").GetInt32();
                Assert.IsTrue(currentId <= previousId, $"Records should be in descending order. Got {currentId} after {previousId}.");
                previousId = currentId;
            }
        }

        /// <summary>
        /// Reads records with first parameter to limit page size.
        /// </summary>
        [TestMethod]
        public async Task ReadRecords_WithFirst_ReturnsLimitedResults()
        {
            CallToolResult result = await ExecuteReadAsync("Book", first: 3);

            AssertSuccess(result, "ReadRecords with first should succeed.");

            JsonElement root = ParseResultRoot(result);
            JsonElement records = GetRecordsArray(root);
            Assert.AreEqual(3, records.GetArrayLength(), "Expected exactly 3 records when first=3.");
        }

        /// <summary>
        /// Reads a single record by primary key filter.
        /// </summary>
        [TestMethod]
        public async Task ReadRecords_FilterById_ReturnsSingleRecord()
        {
            CallToolResult result = await ExecuteReadAsync("Book", select: "id,title", filter: "id eq 1");

            AssertSuccess(result, "ReadRecords with id filter should succeed.");

            JsonElement root = ParseResultRoot(result);
            JsonElement records = GetRecordsArray(root);
            Assert.AreEqual(1, records.GetArrayLength(), "Expected exactly one record with id=1.");
            Assert.AreEqual(1, records[0].GetProperty("id").GetInt32());
        }

        /// <summary>
        /// Reads the first page with first=2, extracts the after cursor, then reads the second page
        /// and verifies it returns different records.
        /// </summary>
        [TestMethod]
        public async Task ReadRecords_WithAfterCursor_ReturnsNextPage()
        {
            // First page: get 2 records ordered by id ascending
            CallToolResult firstPageResult = await ExecuteReadAsync("Book", select: "id,title", orderby: new[] { "id asc" }, first: 2);

            AssertSuccess(firstPageResult, "First page read should succeed.");

            JsonElement firstRoot = ParseResultRoot(firstPageResult);
            JsonElement firstPageRecords = GetRecordsArray(firstRoot);
            Assert.AreEqual(2, firstPageRecords.GetArrayLength(), "First page should return exactly 2 records.");

            // Extract the after cursor from the result
            JsonElement resultElement = firstRoot.GetProperty("result");
            Assert.IsTrue(resultElement.TryGetProperty("after", out JsonElement afterElement),
                "Paginated response should contain an 'after' cursor token.");
            string afterCursor = afterElement.GetString()!;
            Assert.IsFalse(string.IsNullOrWhiteSpace(afterCursor), "After cursor should not be empty.");

            int firstPageLastId = firstPageRecords[1].GetProperty("id").GetInt32();

            // Second page: use the after cursor
            CallToolResult secondPageResult = await ExecuteReadAsync("Book", select: "id,title", orderby: new[] { "id asc" }, first: 2, after: afterCursor);

            AssertSuccess(secondPageResult, "Second page read should succeed.");

            JsonElement secondRoot = ParseResultRoot(secondPageResult);
            JsonElement secondPageRecords = GetRecordsArray(secondRoot);
            Assert.IsTrue(secondPageRecords.GetArrayLength() > 0, "Second page should return records.");

            int secondPageFirstId = secondPageRecords[0].GetProperty("id").GetInt32();
            Assert.IsTrue(secondPageFirstId > firstPageLastId,
                $"Second page first id ({secondPageFirstId}) should be greater than first page last id ({firstPageLastId}).");
        }

        /// <summary>
        /// Reads records for a non-existent entity, expecting an error.
        /// </summary>
        [TestMethod]
        public async Task ReadRecords_InvalidEntity_ReturnsError()
        {
            CallToolResult result = await ExecuteReadAsync("NonExistentEntity");

            AssertError(result, "NonExistentEntity");
        }

        #endregion

        #region Helpers

        private static async Task<CallToolResult> ExecuteReadAsync(
            string entity,
            string? select = null,
            string? filter = null,
            string[]? orderby = null,
            int? first = null,
            string? after = null)
        {
            IServiceProvider serviceProvider = BuildQueryServiceProvider();
            ReadRecordsTool tool = new();

            var args = new Dictionary<string, object?> { { "entity", entity } };

            if (select != null)
            {
                args["select"] = select;
            }

            if (filter != null)
            {
                args["filter"] = filter;
            }

            if (orderby != null)
            {
                args["orderby"] = orderby;
            }

            if (first != null)
            {
                args["first"] = first;
            }

            if (after != null)
            {
                args["after"] = after;
            }

            return await ExecuteToolAsync(tool, serviceProvider, args);
        }

        #endregion
    }
}
