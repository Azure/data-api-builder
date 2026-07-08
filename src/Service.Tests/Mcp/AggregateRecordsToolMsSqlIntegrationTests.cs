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
    /// Integration tests for AggregateRecordsTool against a real MsSql database.
    /// The books table has: id (int PK), title (varchar), publisher_id (int).
    /// Seed data: 21 books with publisher_ids: 1234, 2345, 2323, 2324, 1940, 1941.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class AggregateRecordsToolMsSqlIntegrationTests : McpToolTestBase
    {
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        #region COUNT Tests

        /// <summary>
        /// Counts all records in the Book entity using COUNT(*).
        /// </summary>
        [TestMethod]
        public async Task Aggregate_CountAll_ReturnsCorrectCount()
        {
            CallToolResult result = await ExecuteAggregateAsync("Book", "count", "*");

            AssertSuccess(result, "COUNT(*) should succeed.");

            JsonElement root = ParseResultRoot(result);
            Assert.AreEqual("Book", root.GetProperty("entity").GetString());
            JsonElement resultArray = root.GetProperty("result");
            Assert.AreEqual(JsonValueKind.Array, resultArray.ValueKind, "Simple aggregate result should be an array.");
            int count = resultArray[0].GetProperty("count").GetInt32();
            Assert.IsTrue(count >= 21,
                $"Expected at least 21 books (seed data), got {count}.");
        }

        /// <summary>
        /// Counts records with an OData filter (publisher_id eq 1234).
        /// </summary>
        [TestMethod]
        public async Task Aggregate_CountWithFilter_ReturnsFilteredCount()
        {
            CallToolResult result = await ExecuteAggregateAsync(
                "Book", "count", "*", filter: "publisher_id eq 1234");

            AssertSuccess(result, "COUNT with filter should succeed.");

            JsonElement root = ParseResultRoot(result);
            JsonElement resultArray = root.GetProperty("result");
            int count = resultArray[0].GetProperty("count").GetInt32();
            Assert.IsTrue(count >= 10,
                $"Expected at least 10 books with publisher_id=1234, got {count}.");
        }

        /// <summary>
        /// Counts distinct publisher_id values.
        /// </summary>
        [TestMethod]
        public async Task Aggregate_CountDistinct_ReturnsDistinctCount()
        {
            CallToolResult result = await ExecuteAggregateAsync(
                "Book", "count", "publisher_id", distinct: true);

            AssertSuccess(result, "COUNT DISTINCT should succeed.");

            JsonElement root = ParseResultRoot(result);
            JsonElement resultArray = root.GetProperty("result");
            Assert.AreEqual(JsonValueKind.Array, resultArray.ValueKind, "Result should be an array.");
            Assert.IsTrue(resultArray.GetArrayLength() > 0, "Result array should not be empty.");

            JsonElement firstRow = resultArray[0];
            int count = 0;
            if (firstRow.TryGetProperty("count", out JsonElement countElement))
            {
                count = countElement.GetInt32();
            }
            else if (firstRow.TryGetProperty("count_publisher_id", out JsonElement aliasElement))
            {
                count = aliasElement.GetInt32();
            }

            Assert.IsTrue(count >= 6, $"Expected at least 6 distinct publisher_ids, got {count}.");
        }

        #endregion

        #region SUM/AVG/MIN/MAX Tests

        /// <summary>
        /// Validates that numeric aggregation functions (sum, avg, min, max) succeed and return
        /// the expected alias property with a numeric value.
        /// </summary>
        [DataTestMethod]
        [DataRow("sum", "sum_publisher_id", DisplayName = "SUM of publisher_id")]
        [DataRow("avg", "avg_publisher_id", DisplayName = "AVG of publisher_id")]
        [DataRow("min", "min_publisher_id", DisplayName = "MIN of publisher_id")]
        [DataRow("max", "max_publisher_id", DisplayName = "MAX of publisher_id")]
        public async Task Aggregate_NumericFunction_ReturnsExpectedAlias(string function, string expectedAlias)
        {
            CallToolResult result = await ExecuteAggregateAsync("Book", function, "publisher_id");

            AssertSuccess(result, $"{function.ToUpper()} should succeed on numeric field.");

            JsonElement root = ParseResultRoot(result);
            JsonElement resultArray = root.GetProperty("result");
            Assert.AreEqual(JsonValueKind.Array, resultArray.ValueKind,
                $"{function.ToUpper()} result should be an array.");
            Assert.IsTrue(resultArray.GetArrayLength() > 0,
                $"{function.ToUpper()} result array should not be empty.");
            Assert.IsTrue(resultArray[0].TryGetProperty(expectedAlias, out JsonElement aliasValue),
                $"Result should contain '{expectedAlias}' property.");
            Assert.AreEqual(JsonValueKind.Number, aliasValue.ValueKind,
                $"'{expectedAlias}' should be a numeric value.");
        }

        /// <summary>
        /// Validates that MIN returns the expected minimum value.
        /// </summary>
        [TestMethod]
        public async Task Aggregate_Min_ReturnsExpectedMinValue()
        {
            CallToolResult result = await ExecuteAggregateAsync("Book", "min", "publisher_id");
            AssertSuccess(result, "MIN should succeed.");

            JsonElement root = ParseResultRoot(result);
            JsonElement resultArray = root.GetProperty("result");
            Assert.AreEqual(JsonValueKind.Array, resultArray.ValueKind);
            Assert.IsTrue(resultArray.GetArrayLength() > 0, "Result array should not be empty.");
            Assert.AreEqual(1234, resultArray[0].GetProperty("min_publisher_id").GetInt32(),
                "MIN publisher_id should be 1234 (from seed data).");
        }

        /// <summary>
        /// Validates that MAX returns the expected maximum value.
        /// </summary>
        [TestMethod]
        public async Task Aggregate_Max_ReturnsExpectedMaxValue()
        {
            CallToolResult result = await ExecuteAggregateAsync("Book", "max", "publisher_id");
            AssertSuccess(result, "MAX should succeed.");

            JsonElement root = ParseResultRoot(result);
            JsonElement resultArray = root.GetProperty("result");
            Assert.AreEqual(JsonValueKind.Array, resultArray.ValueKind);
            Assert.IsTrue(resultArray.GetArrayLength() > 0, "Result array should not be empty.");
            Assert.AreEqual(2345, resultArray[0].GetProperty("max_publisher_id").GetInt32(),
                "MAX publisher_id should be 2345 (from seed data).");
        }

        #endregion

        #region GROUP BY Tests

        /// <summary>
        /// Groups by publisher_id and counts records per group.
        /// </summary>
        [TestMethod]
        public async Task Aggregate_GroupByWithCount_ReturnsGroupedResults()
        {
            CallToolResult result = await ExecuteAggregateAsync(
                "Book", "count", "*", groupby: new[] { "publisher_id" });

            AssertSuccess(result, "COUNT with GROUP BY should succeed.");

            JsonElement root = ParseResultRoot(result);
            JsonElement resultElement = root.GetProperty("result");

            // Non-paginated GROUP BY returns result as an array
            if (resultElement.ValueKind == JsonValueKind.Array)
            {
                Assert.IsTrue(resultElement.GetArrayLength() > 1, "Expected multiple groups.");
            }
            else if (resultElement.ValueKind == JsonValueKind.Object &&
                     resultElement.TryGetProperty("items", out JsonElement itemsElement))
            {
                Assert.IsTrue(itemsElement.GetArrayLength() > 1, "Expected multiple groups.");
            }
            else
            {
                Assert.Fail("Unexpected result shape for GROUP BY response.");
            }
        }

        /// <summary>
        /// Groups by publisher_id with first parameter for pagination.
        /// </summary>
        [TestMethod]
        public async Task Aggregate_GroupByWithFirst_ReturnsPaginatedResults()
        {
            CallToolResult result = await ExecuteAggregateAsync(
                "Book", "count", "*", groupby: new[] { "publisher_id" }, first: 2);

            AssertSuccess(result, "COUNT with GROUP BY and first should succeed.");

            JsonElement root = ParseResultRoot(result);
            JsonElement resultElement = root.GetProperty("result");

            // Paginated GROUP BY returns result as { items: [...], endCursor, hasNextPage }
            Assert.AreEqual(JsonValueKind.Object, resultElement.ValueKind,
                "Paginated GROUP BY result should be an object.");
            Assert.IsTrue(resultElement.TryGetProperty("items", out JsonElement itemsElement),
                "Paginated response should contain 'items' property.");
            Assert.IsTrue(itemsElement.GetArrayLength() <= 2, "Expected at most 2 items when first=2.");
            Assert.IsTrue(resultElement.TryGetProperty("hasNextPage", out _),
                "Paginated response should include hasNextPage.");
        }

        #endregion

        #region Error Cases

        /// <summary>
        /// Validates error scenarios for AggregateRecordsTool.
        /// </summary>
        [DataTestMethod]
        [DataRow("NonExistentEntity", "count", "*", "NonExistentEntity", DisplayName = "Invalid entity")]
        [DataRow("Book", "sum", "nonexistent_field", null, DisplayName = "Invalid field")]
        public async Task Aggregate_ErrorScenarios(string entity, string function, string field, string? expectedSubstring)
        {
            CallToolResult result = await ExecuteAggregateAsync(entity, function, field);

            Assert.IsTrue(result.IsError == true, $"Aggregate({entity}, {function}, {field}) should fail.");
            if (expectedSubstring != null)
            {
                StringAssert.Contains(GetFirstTextContent(result), expectedSubstring);
            }
        }

        /// <summary>
        /// Attempts aggregation with no arguments.
        /// </summary>
        [TestMethod]
        public async Task Aggregate_NoArguments_ReturnsError()
        {
            IServiceProvider serviceProvider = BuildQueryServiceProvider();
            AggregateRecordsTool tool = new();

            CallToolResult result = await tool.ExecuteAsync(null, serviceProvider, CancellationToken.None);

            AssertError(result);
        }

        /// <summary>
        /// Attempts aggregation with an invalid function name.
        /// </summary>
        [TestMethod]
        public async Task Aggregate_InvalidFunction_ReturnsError()
        {
            CallToolResult result = await ExecuteAggregateAsync("Book", "invalid_func", "*");

            AssertError(result);
        }

        #endregion

        private static async Task<CallToolResult> ExecuteAggregateAsync(
            string entity,
            string function,
            string field,
            string? filter = null,
            bool distinct = false,
            string[]? groupby = null,
            string? orderby = null,
            int? first = null,
            string? after = null)
        {
            IServiceProvider serviceProvider = BuildQueryServiceProvider();
            AggregateRecordsTool tool = new();

            var args = new Dictionary<string, object?>
            {
                { "entity", entity },
                { "function", function },
                { "field", field }
            };

            if (filter != null)
            {
                args["filter"] = filter;
            }

            if (distinct)
            {
                args["distinct"] = true;
            }

            if (groupby != null)
            {
                args["groupby"] = groupby;
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
    }
}
