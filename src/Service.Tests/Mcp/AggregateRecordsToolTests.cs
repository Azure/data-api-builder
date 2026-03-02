// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Auth;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Azure.DataApiBuilder.Mcp.Model;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using ModelContextProtocol.Protocol;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.Mcp
{
    /// <summary>
    /// Tests for the AggregateRecordsTool MCP tool.
    /// Covers:
    /// - Tool metadata and schema validation
    /// - Runtime-level enabled/disabled configuration
    /// - Entity-level DML tool configuration
    /// - Input validation (missing/invalid arguments)
    /// - In-memory aggregation logic (count, avg, sum, min, max)
    /// - distinct, groupby, having, orderby
    /// - Alias convention
    /// </summary>
    [TestClass]
    public class AggregateRecordsToolTests
    {
        #region Tool Metadata Tests

        [TestMethod]
        public void GetToolMetadata_ReturnsCorrectName()
        {
            AggregateRecordsTool tool = new();
            Tool metadata = tool.GetToolMetadata();
            Assert.AreEqual("aggregate_records", metadata.Name);
        }

        [TestMethod]
        public void GetToolMetadata_ReturnsCorrectToolType()
        {
            AggregateRecordsTool tool = new();
            Assert.AreEqual(McpEnums.ToolType.BuiltIn, tool.ToolType);
        }

        [TestMethod]
        public void GetToolMetadata_HasInputSchema()
        {
            AggregateRecordsTool tool = new();
            Tool metadata = tool.GetToolMetadata();
            Assert.AreEqual(JsonValueKind.Object, metadata.InputSchema.ValueKind);
            Assert.IsTrue(metadata.InputSchema.TryGetProperty("properties", out JsonElement properties));
            Assert.IsTrue(metadata.InputSchema.TryGetProperty("required", out JsonElement required));

            List<string> requiredFields = new();
            foreach (JsonElement r in required.EnumerateArray())
            {
                requiredFields.Add(r.GetString()!);
            }

            CollectionAssert.Contains(requiredFields, "entity");
            CollectionAssert.Contains(requiredFields, "function");
            CollectionAssert.Contains(requiredFields, "field");

            // Verify first and after properties exist in schema
            Assert.IsTrue(properties.TryGetProperty("first", out JsonElement firstProp));
            Assert.AreEqual("integer", firstProp.GetProperty("type").GetString());
            Assert.IsTrue(properties.TryGetProperty("after", out JsonElement afterProp));
            Assert.AreEqual("string", afterProp.GetProperty("type").GetString());
        }

        #endregion

        #region Configuration Tests

        [TestMethod]
        public async Task AggregateRecords_DisabledAtRuntimeLevel_ReturnsToolDisabledError()
        {
            RuntimeConfig config = CreateConfig(aggregateRecordsEnabled: false);
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            AssertToolDisabledError(content);
        }

        [TestMethod]
        public async Task AggregateRecords_DisabledAtEntityLevel_ReturnsToolDisabledError()
        {
            RuntimeConfig config = CreateConfigWithEntityDmlDisabled();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            AssertToolDisabledError(content);
        }

        #endregion

        #region Input Validation Tests

        [TestMethod]
        public async Task AggregateRecords_NullArguments_ReturnsInvalidArguments()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            CallToolResult result = await tool.ExecuteAsync(null, sp, CancellationToken.None);
            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            Assert.AreEqual("InvalidArguments", error.GetProperty("type").GetString());
        }

        [TestMethod]
        public async Task AggregateRecords_MissingEntity_ReturnsInvalidArguments()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            JsonDocument args = JsonDocument.Parse("{\"function\": \"count\", \"field\": \"*\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);
            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            Assert.AreEqual("InvalidArguments", content.GetProperty("error").GetProperty("type").GetString());
        }

        [TestMethod]
        public async Task AggregateRecords_MissingFunction_ReturnsInvalidArguments()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\", \"field\": \"*\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);
            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            Assert.AreEqual("InvalidArguments", content.GetProperty("error").GetProperty("type").GetString());
        }

        [TestMethod]
        public async Task AggregateRecords_MissingField_ReturnsInvalidArguments()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\", \"function\": \"count\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);
            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            Assert.AreEqual("InvalidArguments", content.GetProperty("error").GetProperty("type").GetString());
        }

        [TestMethod]
        public async Task AggregateRecords_InvalidFunction_ReturnsInvalidArguments()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\", \"function\": \"median\", \"field\": \"price\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, CancellationToken.None);
            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            Assert.AreEqual("InvalidArguments", content.GetProperty("error").GetProperty("type").GetString());
            Assert.IsTrue(content.GetProperty("error").GetProperty("message").GetString()!.Contains("median"));
        }

        #endregion

        #region Alias Convention Tests

        [TestMethod]
        public void ComputeAlias_CountStar_ReturnsCount()
        {
            Assert.AreEqual("count", AggregateRecordsTool.ComputeAlias("count", "*"));
        }

        [TestMethod]
        public void ComputeAlias_CountField_ReturnsFunctionField()
        {
            Assert.AreEqual("count_supplierId", AggregateRecordsTool.ComputeAlias("count", "supplierId"));
        }

        [TestMethod]
        public void ComputeAlias_AvgField_ReturnsFunctionField()
        {
            Assert.AreEqual("avg_unitPrice", AggregateRecordsTool.ComputeAlias("avg", "unitPrice"));
        }

        [TestMethod]
        public void ComputeAlias_SumField_ReturnsFunctionField()
        {
            Assert.AreEqual("sum_unitPrice", AggregateRecordsTool.ComputeAlias("sum", "unitPrice"));
        }

        [TestMethod]
        public void ComputeAlias_MinField_ReturnsFunctionField()
        {
            Assert.AreEqual("min_price", AggregateRecordsTool.ComputeAlias("min", "price"));
        }

        [TestMethod]
        public void ComputeAlias_MaxField_ReturnsFunctionField()
        {
            Assert.AreEqual("max_price", AggregateRecordsTool.ComputeAlias("max", "price"));
        }

        #endregion

        #region In-Memory Aggregation Tests

        [TestMethod]
        public void PerformAggregation_CountStar_ReturnsCount()
        {
            JsonElement records = ParseArray("[{\"id\":1},{\"id\":2},{\"id\":3}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "count", "*", false, new(), null, null, "desc", "count");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3.0, result[0]["count"]);
        }

        [TestMethod]
        public void PerformAggregation_Avg_ReturnsAverage()
        {
            JsonElement records = ParseArray("[{\"price\":10},{\"price\":20},{\"price\":30}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "avg", "price", false, new(), null, null, "desc", "avg_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(20.0, result[0]["avg_price"]);
        }

        [TestMethod]
        public void PerformAggregation_Sum_ReturnsSum()
        {
            JsonElement records = ParseArray("[{\"price\":10},{\"price\":20},{\"price\":30}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new(), null, null, "desc", "sum_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(60.0, result[0]["sum_price"]);
        }

        [TestMethod]
        public void PerformAggregation_Min_ReturnsMin()
        {
            JsonElement records = ParseArray("[{\"price\":10},{\"price\":20},{\"price\":5}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "min", "price", false, new(), null, null, "desc", "min_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(5.0, result[0]["min_price"]);
        }

        [TestMethod]
        public void PerformAggregation_Max_ReturnsMax()
        {
            JsonElement records = ParseArray("[{\"price\":10},{\"price\":20},{\"price\":5}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "max", "price", false, new(), null, null, "desc", "max_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(20.0, result[0]["max_price"]);
        }

        [TestMethod]
        public void PerformAggregation_CountDistinct_ReturnsDistinctCount()
        {
            JsonElement records = ParseArray("[{\"supplierId\":1},{\"supplierId\":2},{\"supplierId\":1},{\"supplierId\":3}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "count", "supplierId", true, new(), null, null, "desc", "count_supplierId");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3.0, result[0]["count_supplierId"]);
        }

        [TestMethod]
        public void PerformAggregation_AvgDistinct_ReturnsDistinctAvg()
        {
            JsonElement records = ParseArray("[{\"price\":10},{\"price\":10},{\"price\":20},{\"price\":30}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "avg", "price", true, new(), null, null, "desc", "avg_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(20.0, result[0]["avg_price"]);
        }

        [TestMethod]
        public void PerformAggregation_GroupBy_ReturnsGroupedResults()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\",\"price\":10},{\"category\":\"A\",\"price\":20},{\"category\":\"B\",\"price\":50}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new() { "category" }, null, null, "desc", "sum_price");

            Assert.AreEqual(2, result.Count);
            // Desc order: B(50) first, then A(30)
            Assert.AreEqual("B", result[0]["category"]?.ToString());
            Assert.AreEqual(50.0, result[0]["sum_price"]);
            Assert.AreEqual("A", result[1]["category"]?.ToString());
            Assert.AreEqual(30.0, result[1]["sum_price"]);
        }

        [TestMethod]
        public void PerformAggregation_GroupBy_Asc_ReturnsSortedAsc()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\",\"price\":10},{\"category\":\"B\",\"price\":30},{\"category\":\"A\",\"price\":20}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new() { "category" }, null, null, "asc", "sum_price");

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("A", result[0]["category"]?.ToString());
            Assert.AreEqual(30.0, result[0]["sum_price"]);
            Assert.AreEqual("B", result[1]["category"]?.ToString());
            Assert.AreEqual(30.0, result[1]["sum_price"]);
        }

        [TestMethod]
        public void PerformAggregation_CountStar_GroupBy_ReturnsGroupCounts()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\"},{\"category\":\"A\"},{\"category\":\"B\"}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "count", "*", false, new() { "category" }, null, null, "desc", "count");

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("A", result[0]["category"]?.ToString());
            Assert.AreEqual(2.0, result[0]["count"]);
            Assert.AreEqual("B", result[1]["category"]?.ToString());
            Assert.AreEqual(1.0, result[1]["count"]);
        }

        [TestMethod]
        public void PerformAggregation_HavingGt_FiltersResults()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\",\"price\":10},{\"category\":\"A\",\"price\":20},{\"category\":\"B\",\"price\":5}]");
            var having = new Dictionary<string, double> { ["gt"] = 10 };
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new() { "category" }, having, null, "desc", "sum_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("A", result[0]["category"]?.ToString());
            Assert.AreEqual(30.0, result[0]["sum_price"]);
        }

        [TestMethod]
        public void PerformAggregation_HavingGteLte_FiltersRange()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\",\"price\":100},{\"category\":\"B\",\"price\":20},{\"category\":\"C\",\"price\":1}]");
            var having = new Dictionary<string, double> { ["gte"] = 10, ["lte"] = 50 };
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new() { "category" }, having, null, "desc", "sum_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("B", result[0]["category"]?.ToString());
        }

        [TestMethod]
        public void PerformAggregation_HavingIn_FiltersExactValues()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\"},{\"category\":\"A\"},{\"category\":\"B\"},{\"category\":\"C\"},{\"category\":\"C\"},{\"category\":\"C\"}]");
            var havingIn = new List<double> { 2, 3 };
            var result = AggregateRecordsTool.PerformAggregation(records, "count", "*", false, new() { "category" }, null, havingIn, "desc", "count");

            Assert.AreEqual(2, result.Count);
            // C(3) desc, A(2)
            Assert.AreEqual("C", result[0]["category"]?.ToString());
            Assert.AreEqual(3.0, result[0]["count"]);
            Assert.AreEqual("A", result[1]["category"]?.ToString());
            Assert.AreEqual(2.0, result[1]["count"]);
        }

        [TestMethod]
        public void PerformAggregation_HavingEq_FiltersSingleValue()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\",\"price\":10},{\"category\":\"B\",\"price\":20}]");
            var having = new Dictionary<string, double> { ["eq"] = 10 };
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new() { "category" }, having, null, "desc", "sum_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("A", result[0]["category"]?.ToString());
        }

        [TestMethod]
        public void PerformAggregation_HavingNeq_FiltersOutValue()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\",\"price\":10},{\"category\":\"B\",\"price\":20}]");
            var having = new Dictionary<string, double> { ["neq"] = 10 };
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new() { "category" }, having, null, "desc", "sum_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("B", result[0]["category"]?.ToString());
        }

        [TestMethod]
        public void PerformAggregation_EmptyRecords_ReturnsNull()
        {
            JsonElement records = ParseArray("[]");
            var result = AggregateRecordsTool.PerformAggregation(records, "avg", "price", false, new(), null, null, "desc", "avg_price");

            Assert.AreEqual(1, result.Count);
            Assert.IsNull(result[0]["avg_price"]);
        }

        [TestMethod]
        public void PerformAggregation_EmptyRecordsCountStar_ReturnsZero()
        {
            JsonElement records = ParseArray("[]");
            var result = AggregateRecordsTool.PerformAggregation(records, "count", "*", false, new(), null, null, "desc", "count");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(0.0, result[0]["count"]);
        }

        [TestMethod]
        public void PerformAggregation_MultipleGroupByFields_ReturnsCorrectGroups()
        {
            JsonElement records = ParseArray("[{\"cat\":\"A\",\"region\":\"East\",\"price\":10},{\"cat\":\"A\",\"region\":\"East\",\"price\":20},{\"cat\":\"A\",\"region\":\"West\",\"price\":5}]");
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new() { "cat", "region" }, null, null, "desc", "sum_price");

            Assert.AreEqual(2, result.Count);
            // (A,East)=30 desc, (A,West)=5
            Assert.AreEqual("A", result[0]["cat"]?.ToString());
            Assert.AreEqual("East", result[0]["region"]?.ToString());
            Assert.AreEqual(30.0, result[0]["sum_price"]);
        }

        [TestMethod]
        public void PerformAggregation_HavingNoResults_ReturnsEmpty()
        {
            JsonElement records = ParseArray("[{\"category\":\"A\",\"price\":10}]");
            var having = new Dictionary<string, double> { ["gt"] = 100 };
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new() { "category" }, having, null, "desc", "sum_price");

            Assert.AreEqual(0, result.Count);
        }

        [TestMethod]
        public void PerformAggregation_HavingOnSingleResult_Passes()
        {
            JsonElement records = ParseArray("[{\"price\":50},{\"price\":60}]");
            var having = new Dictionary<string, double> { ["gte"] = 100 };
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new(), having, null, "desc", "sum_price");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(110.0, result[0]["sum_price"]);
        }

        [TestMethod]
        public void PerformAggregation_HavingOnSingleResult_Fails()
        {
            JsonElement records = ParseArray("[{\"price\":50},{\"price\":60}]");
            var having = new Dictionary<string, double> { ["gt"] = 200 };
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "price", false, new(), having, null, "desc", "sum_price");

            Assert.AreEqual(0, result.Count);
        }

        #endregion

        #region Pagination Tests

        [TestMethod]
        public void ApplyPagination_FirstOnly_ReturnsFirstNItems()
        {
            List<Dictionary<string, object?>> allResults = new()
            {
                new() { ["category"] = "A", ["count"] = 10.0 },
                new() { ["category"] = "B", ["count"] = 8.0 },
                new() { ["category"] = "C", ["count"] = 6.0 },
                new() { ["category"] = "D", ["count"] = 4.0 },
                new() { ["category"] = "E", ["count"] = 2.0 }
            };

            AggregateRecordsTool.PaginationResult result = AggregateRecordsTool.ApplyPagination(allResults, 3, null);

            Assert.AreEqual(3, result.Items.Count);
            Assert.AreEqual("A", result.Items[0]["category"]?.ToString());
            Assert.AreEqual("C", result.Items[2]["category"]?.ToString());
            Assert.IsTrue(result.HasNextPage);
            Assert.IsNotNull(result.EndCursor);
        }

        [TestMethod]
        public void ApplyPagination_FirstWithAfter_ReturnsNextPage()
        {
            List<Dictionary<string, object?>> allResults = new()
            {
                new() { ["category"] = "A", ["count"] = 10.0 },
                new() { ["category"] = "B", ["count"] = 8.0 },
                new() { ["category"] = "C", ["count"] = 6.0 },
                new() { ["category"] = "D", ["count"] = 4.0 },
                new() { ["category"] = "E", ["count"] = 2.0 }
            };

            // First page
            AggregateRecordsTool.PaginationResult firstPage = AggregateRecordsTool.ApplyPagination(allResults, 3, null);
            Assert.AreEqual(3, firstPage.Items.Count);
            Assert.IsTrue(firstPage.HasNextPage);

            // Second page using cursor from first page
            AggregateRecordsTool.PaginationResult secondPage = AggregateRecordsTool.ApplyPagination(allResults, 3, firstPage.EndCursor);
            Assert.AreEqual(2, secondPage.Items.Count);
            Assert.AreEqual("D", secondPage.Items[0]["category"]?.ToString());
            Assert.AreEqual("E", secondPage.Items[1]["category"]?.ToString());
            Assert.IsFalse(secondPage.HasNextPage);
        }

        [TestMethod]
        public void ApplyPagination_FirstExceedsTotalCount_ReturnsAllItems()
        {
            List<Dictionary<string, object?>> allResults = new()
            {
                new() { ["category"] = "A", ["count"] = 10.0 },
                new() { ["category"] = "B", ["count"] = 8.0 }
            };

            AggregateRecordsTool.PaginationResult result = AggregateRecordsTool.ApplyPagination(allResults, 5, null);

            Assert.AreEqual(2, result.Items.Count);
            Assert.IsFalse(result.HasNextPage);
        }

        [TestMethod]
        public void ApplyPagination_FirstExactlyMatchesTotalCount_HasNextPageIsFalse()
        {
            List<Dictionary<string, object?>> allResults = new()
            {
                new() { ["category"] = "A", ["count"] = 10.0 },
                new() { ["category"] = "B", ["count"] = 8.0 },
                new() { ["category"] = "C", ["count"] = 6.0 }
            };

            AggregateRecordsTool.PaginationResult result = AggregateRecordsTool.ApplyPagination(allResults, 3, null);

            Assert.AreEqual(3, result.Items.Count);
            Assert.IsFalse(result.HasNextPage);
        }

        [TestMethod]
        public void ApplyPagination_EmptyResults_ReturnsEmptyPage()
        {
            List<Dictionary<string, object?>> allResults = new();

            AggregateRecordsTool.PaginationResult result = AggregateRecordsTool.ApplyPagination(allResults, 5, null);

            Assert.AreEqual(0, result.Items.Count);
            Assert.IsFalse(result.HasNextPage);
            Assert.IsNull(result.EndCursor);
        }

        [TestMethod]
        public void ApplyPagination_InvalidCursor_StartsFromBeginning()
        {
            List<Dictionary<string, object?>> allResults = new()
            {
                new() { ["category"] = "A", ["count"] = 10.0 },
                new() { ["category"] = "B", ["count"] = 8.0 }
            };

            AggregateRecordsTool.PaginationResult result = AggregateRecordsTool.ApplyPagination(allResults, 5, "not-valid-base64!!!");

            Assert.AreEqual(2, result.Items.Count);
            Assert.AreEqual("A", result.Items[0]["category"]?.ToString());
            Assert.IsFalse(result.HasNextPage);
            Assert.IsNotNull(result.EndCursor);
        }

        [TestMethod]
        public void ApplyPagination_CursorBeyondResults_ReturnsEmptyPage()
        {
            List<Dictionary<string, object?>> allResults = new()
            {
                new() { ["category"] = "A", ["count"] = 10.0 }
            };

            // Cursor pointing beyond the end
            string cursor = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes("100"));
            AggregateRecordsTool.PaginationResult result = AggregateRecordsTool.ApplyPagination(allResults, 5, cursor);

            Assert.AreEqual(0, result.Items.Count);
            Assert.IsFalse(result.HasNextPage);
            Assert.IsNull(result.EndCursor);
        }

        [TestMethod]
        public void ApplyPagination_MultiplePages_TraversesAllResults()
        {
            List<Dictionary<string, object?>> allResults = new();
            for (int i = 0; i < 8; i++)
            {
                allResults.Add(new() { ["category"] = $"Cat{i}", ["count"] = (double)(8 - i) });
            }

            // Page 1
            AggregateRecordsTool.PaginationResult page1 = AggregateRecordsTool.ApplyPagination(allResults, 3, null);
            Assert.AreEqual(3, page1.Items.Count);
            Assert.IsTrue(page1.HasNextPage);

            // Page 2
            AggregateRecordsTool.PaginationResult page2 = AggregateRecordsTool.ApplyPagination(allResults, 3, page1.EndCursor);
            Assert.AreEqual(3, page2.Items.Count);
            Assert.IsTrue(page2.HasNextPage);

            // Page 3 (last page)
            AggregateRecordsTool.PaginationResult page3 = AggregateRecordsTool.ApplyPagination(allResults, 3, page2.EndCursor);
            Assert.AreEqual(2, page3.Items.Count);
            Assert.IsFalse(page3.HasNextPage);
        }

        #endregion

        #region Timeout and Cancellation Tests

        /// <summary>
        /// Verifies that OperationCanceledException produces a model-explicit error
        /// that clearly states the operation was canceled, not errored.
        /// </summary>
        [TestMethod]
        public async Task AggregateRecords_OperationCanceled_ReturnsExplicitCanceledMessage()
        {
            RuntimeConfig config = CreateConfig();
            IServiceProvider sp = CreateServiceProvider(config);
            AggregateRecordsTool tool = new();

            // Create a pre-canceled token
            CancellationTokenSource cts = new();
            cts.Cancel();

            JsonDocument args = JsonDocument.Parse("{\"entity\": \"Book\", \"function\": \"count\", \"field\": \"*\"}");
            CallToolResult result = await tool.ExecuteAsync(args, sp, cts.Token);

            Assert.IsTrue(result.IsError == true);
            JsonElement content = ParseContent(result);
            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            string errorType = error.GetProperty("type").GetString();
            string errorMessage = error.GetProperty("message").GetString();

            // Verify the error type identifies it as a cancellation
            Assert.AreEqual("OperationCanceled", errorType);

            // Verify the message explicitly tells the model this is NOT a tool error
            Assert.IsTrue(errorMessage.Contains("NOT a tool error"), "Message must explicitly state this is NOT a tool error.");

            // Verify the message tells the model what happened
            Assert.IsTrue(errorMessage.Contains("canceled"), "Message must mention the operation was canceled.");

            // Verify the message tells the model it can retry
            Assert.IsTrue(errorMessage.Contains("retry"), "Message must tell the model it can retry.");
        }

        /// <summary>
        /// Verifies that the timeout error message provides explicit guidance to the model
        /// about what happened and what to do next.
        /// </summary>
        [TestMethod]
        public void TimeoutErrorMessage_ContainsModelGuidance()
        {
            // Simulate what the tool builds for a TimeoutException response
            string entityName = "Product";
            string expectedMessage = $"The aggregation query for entity '{entityName}' timed out. "
                + "This is NOT a tool error. The database did not respond in time. "
                + "This may occur with large datasets or complex aggregations. "
                + "Try narrowing results with a 'filter', reducing 'groupby' fields, or adding 'first' for pagination.";

            // Verify message explicitly states it's NOT a tool error
            Assert.IsTrue(expectedMessage.Contains("NOT a tool error"), "Timeout message must state this is NOT a tool error.");

            // Verify message explains the cause
            Assert.IsTrue(expectedMessage.Contains("database did not respond"), "Timeout message must explain the database didn't respond.");

            // Verify message mentions large datasets
            Assert.IsTrue(expectedMessage.Contains("large datasets"), "Timeout message must mention large datasets as a possible cause.");

            // Verify message provides actionable remediation steps
            Assert.IsTrue(expectedMessage.Contains("filter"), "Timeout message must suggest using a filter.");
            Assert.IsTrue(expectedMessage.Contains("groupby"), "Timeout message must suggest reducing groupby fields.");
            Assert.IsTrue(expectedMessage.Contains("first"), "Timeout message must suggest using pagination with first.");
        }

        /// <summary>
        /// Verifies that TaskCanceledException (which typically signals HTTP/DB timeout)
        /// produces a TimeoutError, not a cancellation error.
        /// </summary>
        [TestMethod]
        public void TaskCanceledErrorMessage_ContainsTimeoutGuidance()
        {
            // Simulate what the tool builds for a TaskCanceledException response
            string entityName = "Product";
            string expectedMessage = $"The aggregation query for entity '{entityName}' was canceled, likely due to a timeout. "
                + "This is NOT a tool error. The database did not respond in time. "
                + "Try narrowing results with a 'filter', reducing 'groupby' fields, or adding 'first' for pagination.";

            // TaskCanceledException should produce a TimeoutError, not OperationCanceled
            Assert.IsTrue(expectedMessage.Contains("NOT a tool error"), "TaskCanceled message must state this is NOT a tool error.");
            Assert.IsTrue(expectedMessage.Contains("timeout"), "TaskCanceled message must reference timeout as the cause.");
            Assert.IsTrue(expectedMessage.Contains("filter"), "TaskCanceled message must suggest filter as remediation.");
            Assert.IsTrue(expectedMessage.Contains("first"), "TaskCanceled message must suggest first for pagination.");
        }

        /// <summary>
        /// Verifies that the OperationCanceled error message for a specific entity
        /// includes the entity name so the model knows which aggregation failed.
        /// </summary>
        [TestMethod]
        public void CanceledErrorMessage_IncludesEntityName()
        {
            string entityName = "LargeProductCatalog";
            string expectedMessage = $"The aggregation query for entity '{entityName}' was canceled before completion. "
                + "This is NOT a tool error. The operation was interrupted, possibly due to a timeout or client disconnect. "
                + "No results were returned. You may retry the same request.";

            Assert.IsTrue(expectedMessage.Contains(entityName), "Canceled message must include the entity name.");
            Assert.IsTrue(expectedMessage.Contains("No results were returned"), "Canceled message must state no results were returned.");
        }

        /// <summary>
        /// Verifies that the timeout error message for a specific entity
        /// includes the entity name so the model knows which aggregation timed out.
        /// </summary>
        [TestMethod]
        public void TimeoutErrorMessage_IncludesEntityName()
        {
            string entityName = "HugeTransactionLog";
            string expectedMessage = $"The aggregation query for entity '{entityName}' timed out. "
                + "This is NOT a tool error. The database did not respond in time. "
                + "This may occur with large datasets or complex aggregations. "
                + "Try narrowing results with a 'filter', reducing 'groupby' fields, or adding 'first' for pagination.";

            Assert.IsTrue(expectedMessage.Contains(entityName), "Timeout message must include the entity name.");
        }

        #endregion

        #region Spec Example Tests

        /// <summary>
        /// Spec Example 1: "How many products are there?"
        /// COUNT(*) → 77
        /// </summary>
        [TestMethod]
        public void SpecExample01_CountStar_ReturnsTotal()
        {
            // Build 77 product records
            List<string> items = new();
            for (int i = 1; i <= 77; i++)
            {
                items.Add($"{{\"id\":{i}}}");
            }

            JsonElement records = ParseArray($"[{string.Join(",", items)}]");
            string alias = AggregateRecordsTool.ComputeAlias("count", "*");
            var result = AggregateRecordsTool.PerformAggregation(records, "count", "*", false, new(), null, null, "desc", alias);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("count", alias);
            Assert.AreEqual(77.0, result[0]["count"]);
        }

        /// <summary>
        /// Spec Example 2: "What is the average price of products under $10?"
        /// AVG(unitPrice) WHERE unitPrice &lt; 10 → 6.74
        /// Filter is applied at DB level; we supply pre-filtered records.
        /// </summary>
        [TestMethod]
        public void SpecExample02_AvgWithFilter_ReturnsFilteredAverage()
        {
            // Pre-filtered records (unitPrice < 10) that average to 6.74
            // 4.50 + 6.00 + 9.72 = 20.22 / 3 = 6.74
            JsonElement records = ParseArray("[{\"unitPrice\":4.5},{\"unitPrice\":6.0},{\"unitPrice\":9.72}]");
            string alias = AggregateRecordsTool.ComputeAlias("avg", "unitPrice");
            var result = AggregateRecordsTool.PerformAggregation(records, "avg", "unitPrice", false, new(), null, null, "desc", alias);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("avg_unitPrice", alias);
            Assert.AreEqual(6.74, result[0]["avg_unitPrice"]);
        }

        /// <summary>
        /// Spec Example 3: "Which categories have more than 20 products?"
        /// COUNT(*) GROUP BY categoryName HAVING COUNT(*) &gt; 20
        /// Expected: Beverages=24, Condiments=22
        /// </summary>
        [TestMethod]
        public void SpecExample03_CountGroupByHavingGt_FiltersGroups()
        {
            List<string> items = new();
            for (int i = 0; i < 24; i++)
            {
                items.Add("{\"categoryName\":\"Beverages\"}");
            }

            for (int i = 0; i < 22; i++)
            {
                items.Add("{\"categoryName\":\"Condiments\"}");
            }

            for (int i = 0; i < 12; i++)
            {
                items.Add("{\"categoryName\":\"Seafood\"}");
            }

            JsonElement records = ParseArray($"[{string.Join(",", items)}]");
            string alias = AggregateRecordsTool.ComputeAlias("count", "*");
            var having = new Dictionary<string, double> { ["gt"] = 20 };
            var result = AggregateRecordsTool.PerformAggregation(records, "count", "*", false, new() { "categoryName" }, having, null, "desc", alias);

            Assert.AreEqual(2, result.Count);
            // Desc order: Beverages(24), Condiments(22)
            Assert.AreEqual("Beverages", result[0]["categoryName"]?.ToString());
            Assert.AreEqual(24.0, result[0]["count"]);
            Assert.AreEqual("Condiments", result[1]["categoryName"]?.ToString());
            Assert.AreEqual(22.0, result[1]["count"]);
        }

        /// <summary>
        /// Spec Example 4: "For discontinued products, which categories have a total revenue between $500 and $10,000?"
        /// SUM(unitPrice) WHERE discontinued=1 GROUP BY categoryName HAVING SUM &gt;= 500 AND &lt;= 10000
        /// Expected: Seafood=1834.50, Produce=742.00
        /// </summary>
        [TestMethod]
        public void SpecExample04_SumFilterGroupByHavingRange_ReturnsMatchingGroups()
        {
            // Pre-filtered (discontinued) records with prices summing per category
            JsonElement records = ParseArray(
                "[" +
                "{\"categoryName\":\"Seafood\",\"unitPrice\":900}," +
                "{\"categoryName\":\"Seafood\",\"unitPrice\":934.5}," +
                "{\"categoryName\":\"Produce\",\"unitPrice\":400}," +
                "{\"categoryName\":\"Produce\",\"unitPrice\":342}," +
                "{\"categoryName\":\"Dairy\",\"unitPrice\":50}" +  // Sum 50, below 500
                "]");
            string alias = AggregateRecordsTool.ComputeAlias("sum", "unitPrice");
            var having = new Dictionary<string, double> { ["gte"] = 500, ["lte"] = 10000 };
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "unitPrice", false, new() { "categoryName" }, having, null, "desc", alias);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("sum_unitPrice", alias);
            // Desc order: Seafood(1834.5), Produce(742)
            Assert.AreEqual("Seafood", result[0]["categoryName"]?.ToString());
            Assert.AreEqual(1834.5, result[0]["sum_unitPrice"]);
            Assert.AreEqual("Produce", result[1]["categoryName"]?.ToString());
            Assert.AreEqual(742.0, result[1]["sum_unitPrice"]);
        }

        /// <summary>
        /// Spec Example 5: "How many distinct suppliers do we have?"
        /// COUNT(DISTINCT supplierId) → 29
        /// </summary>
        [TestMethod]
        public void SpecExample05_CountDistinct_ReturnsDistinctCount()
        {
            // Build records with 29 distinct supplierIds plus duplicates
            List<string> items = new();
            for (int i = 1; i <= 29; i++)
            {
                items.Add($"{{\"supplierId\":{i}}}");
            }

            // Add duplicates
            items.Add("{\"supplierId\":1}");
            items.Add("{\"supplierId\":5}");
            items.Add("{\"supplierId\":10}");

            JsonElement records = ParseArray($"[{string.Join(",", items)}]");
            string alias = AggregateRecordsTool.ComputeAlias("count", "supplierId");
            var result = AggregateRecordsTool.PerformAggregation(records, "count", "supplierId", true, new(), null, null, "desc", alias);

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("count_supplierId", alias);
            Assert.AreEqual(29.0, result[0]["count_supplierId"]);
        }

        /// <summary>
        /// Spec Example 6: "Which categories have exactly 5 or 10 products?"
        /// COUNT(*) GROUP BY categoryName HAVING COUNT(*) IN (5, 10)
        /// Expected: Grains=5, Produce=5
        /// </summary>
        [TestMethod]
        public void SpecExample06_CountGroupByHavingIn_FiltersExactCounts()
        {
            List<string> items = new();
            for (int i = 0; i < 5; i++)
            {
                items.Add("{\"categoryName\":\"Grains\"}");
            }

            for (int i = 0; i < 5; i++)
            {
                items.Add("{\"categoryName\":\"Produce\"}");
            }

            for (int i = 0; i < 12; i++)
            {
                items.Add("{\"categoryName\":\"Beverages\"}");
            }

            JsonElement records = ParseArray($"[{string.Join(",", items)}]");
            string alias = AggregateRecordsTool.ComputeAlias("count", "*");
            var havingIn = new List<double> { 5, 10 };
            var result = AggregateRecordsTool.PerformAggregation(records, "count", "*", false, new() { "categoryName" }, null, havingIn, "desc", alias);

            Assert.AreEqual(2, result.Count);
            // Both have count=5, same order as grouped
            Assert.AreEqual(5.0, result[0]["count"]);
            Assert.AreEqual(5.0, result[1]["count"]);
        }

        /// <summary>
        /// Spec Example 7: "What is the average distinct unit price per category, for categories averaging over $25?"
        /// AVG(DISTINCT unitPrice) GROUP BY categoryName HAVING AVG(DISTINCT unitPrice) &gt; 25
        /// Expected: Meat/Poultry=54.01, Beverages=32.50
        /// </summary>
        [TestMethod]
        public void SpecExample07_AvgDistinctGroupByHavingGt_FiltersAboveThreshold()
        {
            // Meat/Poultry: distinct prices {40.00, 68.02} → avg = 54.01
            // Beverages: distinct prices {25.00, 40.00} → avg = 32.50
            // Condiments: distinct prices {10.00, 15.00} → avg = 12.50 (below threshold)
            JsonElement records = ParseArray(
                "[" +
                "{\"categoryName\":\"Meat/Poultry\",\"unitPrice\":40.00}," +
                "{\"categoryName\":\"Meat/Poultry\",\"unitPrice\":68.02}," +
                "{\"categoryName\":\"Meat/Poultry\",\"unitPrice\":40.00}," +  // duplicate
                "{\"categoryName\":\"Beverages\",\"unitPrice\":25.00}," +
                "{\"categoryName\":\"Beverages\",\"unitPrice\":40.00}," +
                "{\"categoryName\":\"Beverages\",\"unitPrice\":25.00}," +  // duplicate
                "{\"categoryName\":\"Condiments\",\"unitPrice\":10.00}," +
                "{\"categoryName\":\"Condiments\",\"unitPrice\":15.00}" +
                "]");
            string alias = AggregateRecordsTool.ComputeAlias("avg", "unitPrice");
            var having = new Dictionary<string, double> { ["gt"] = 25 };
            var result = AggregateRecordsTool.PerformAggregation(records, "avg", "unitPrice", true, new() { "categoryName" }, having, null, "desc", alias);

            Assert.AreEqual(2, result.Count);
            Assert.AreEqual("avg_unitPrice", alias);
            // Desc order: Meat/Poultry(54.01), Beverages(32.5)
            Assert.AreEqual("Meat/Poultry", result[0]["categoryName"]?.ToString());
            Assert.AreEqual(54.01, result[0]["avg_unitPrice"]);
            Assert.AreEqual("Beverages", result[1]["categoryName"]?.ToString());
            Assert.AreEqual(32.5, result[1]["avg_unitPrice"]);
        }

        /// <summary>
        /// Spec Example 8: "Which categories have the most products?"
        /// COUNT(*) GROUP BY categoryName ORDER BY DESC
        /// Expected: Confections=13, Beverages=12, Condiments=12, Seafood=12
        /// </summary>
        [TestMethod]
        public void SpecExample08_CountGroupByOrderByDesc_ReturnsSortedDesc()
        {
            List<string> items = new();
            for (int i = 0; i < 13; i++)
            {
                items.Add("{\"categoryName\":\"Confections\"}");
            }

            for (int i = 0; i < 12; i++)
            {
                items.Add("{\"categoryName\":\"Beverages\"}");
            }

            for (int i = 0; i < 12; i++)
            {
                items.Add("{\"categoryName\":\"Condiments\"}");
            }

            for (int i = 0; i < 12; i++)
            {
                items.Add("{\"categoryName\":\"Seafood\"}");
            }

            JsonElement records = ParseArray($"[{string.Join(",", items)}]");
            string alias = AggregateRecordsTool.ComputeAlias("count", "*");
            var result = AggregateRecordsTool.PerformAggregation(records, "count", "*", false, new() { "categoryName" }, null, null, "desc", alias);

            Assert.AreEqual(4, result.Count);
            Assert.AreEqual("Confections", result[0]["categoryName"]?.ToString());
            Assert.AreEqual(13.0, result[0]["count"]);
            // Remaining 3 all have count=12
            Assert.AreEqual(12.0, result[1]["count"]);
            Assert.AreEqual(12.0, result[2]["count"]);
            Assert.AreEqual(12.0, result[3]["count"]);
        }

        /// <summary>
        /// Spec Example 9: "What are the cheapest categories by average price?"
        /// AVG(unitPrice) GROUP BY categoryName ORDER BY ASC
        /// Expected: Grains/Cereals=20.25, Condiments=23.06, Produce=32.37
        /// </summary>
        [TestMethod]
        public void SpecExample09_AvgGroupByOrderByAsc_ReturnsSortedAsc()
        {
            // Grains/Cereals: {15.50, 25.00} → avg = 20.25
            // Condiments: {20.12, 26.00} → avg = 23.06
            // Produce: {28.74, 36.00} → avg = 32.37
            JsonElement records = ParseArray(
                "[" +
                "{\"categoryName\":\"Grains/Cereals\",\"unitPrice\":15.50}," +
                "{\"categoryName\":\"Grains/Cereals\",\"unitPrice\":25.00}," +
                "{\"categoryName\":\"Condiments\",\"unitPrice\":20.12}," +
                "{\"categoryName\":\"Condiments\",\"unitPrice\":26.00}," +
                "{\"categoryName\":\"Produce\",\"unitPrice\":28.74}," +
                "{\"categoryName\":\"Produce\",\"unitPrice\":36.00}" +
                "]");
            string alias = AggregateRecordsTool.ComputeAlias("avg", "unitPrice");
            var result = AggregateRecordsTool.PerformAggregation(records, "avg", "unitPrice", false, new() { "categoryName" }, null, null, "asc", alias);

            Assert.AreEqual(3, result.Count);
            // Asc order: Grains/Cereals(20.25), Condiments(23.06), Produce(32.37)
            Assert.AreEqual("Grains/Cereals", result[0]["categoryName"]?.ToString());
            Assert.AreEqual(20.25, result[0]["avg_unitPrice"]);
            Assert.AreEqual("Condiments", result[1]["categoryName"]?.ToString());
            Assert.AreEqual(23.06, result[1]["avg_unitPrice"]);
            Assert.AreEqual("Produce", result[2]["categoryName"]?.ToString());
            Assert.AreEqual(32.37, result[2]["avg_unitPrice"]);
        }

        /// <summary>
        /// Spec Example 10: "For categories with over $500 revenue from discontinued products, which has the highest total?"
        /// SUM(unitPrice) WHERE discontinued=1 GROUP BY categoryName HAVING SUM &gt; 500 ORDER BY DESC
        /// Expected: Seafood=1834.50, Meat/Poultry=1062.50, Produce=742.00
        /// </summary>
        [TestMethod]
        public void SpecExample10_SumFilterGroupByHavingGtOrderByDesc_ReturnsSortedFiltered()
        {
            // Pre-filtered (discontinued) records
            JsonElement records = ParseArray(
                "[" +
                "{\"categoryName\":\"Seafood\",\"unitPrice\":900}," +
                "{\"categoryName\":\"Seafood\",\"unitPrice\":934.5}," +
                "{\"categoryName\":\"Meat/Poultry\",\"unitPrice\":500}," +
                "{\"categoryName\":\"Meat/Poultry\",\"unitPrice\":562.5}," +
                "{\"categoryName\":\"Produce\",\"unitPrice\":400}," +
                "{\"categoryName\":\"Produce\",\"unitPrice\":342}," +
                "{\"categoryName\":\"Dairy\",\"unitPrice\":50}" +  // Sum 50, below 500
                "]");
            string alias = AggregateRecordsTool.ComputeAlias("sum", "unitPrice");
            var having = new Dictionary<string, double> { ["gt"] = 500 };
            var result = AggregateRecordsTool.PerformAggregation(records, "sum", "unitPrice", false, new() { "categoryName" }, having, null, "desc", alias);

            Assert.AreEqual(3, result.Count);
            // Desc order: Seafood(1834.5), Meat/Poultry(1062.5), Produce(742)
            Assert.AreEqual("Seafood", result[0]["categoryName"]?.ToString());
            Assert.AreEqual(1834.5, result[0]["sum_unitPrice"]);
            Assert.AreEqual("Meat/Poultry", result[1]["categoryName"]?.ToString());
            Assert.AreEqual(1062.5, result[1]["sum_unitPrice"]);
            Assert.AreEqual("Produce", result[2]["categoryName"]?.ToString());
            Assert.AreEqual(742.0, result[2]["sum_unitPrice"]);
        }

        /// <summary>
        /// Spec Example 11: "Show me the first 5 categories by product count"
        /// COUNT(*) GROUP BY categoryName ORDER BY DESC FIRST 5
        /// Expected: 5 items with hasNextPage=true, endCursor set
        /// </summary>
        [TestMethod]
        public void SpecExample11_CountGroupByOrderByDescFirst5_ReturnsPaginatedResults()
        {
            List<string> items = new();
            string[] categories = { "Confections", "Beverages", "Condiments", "Seafood", "Dairy", "Grains/Cereals", "Meat/Poultry", "Produce" };
            int[] counts = { 13, 12, 12, 12, 10, 7, 6, 5 };
            for (int c = 0; c < categories.Length; c++)
            {
                for (int i = 0; i < counts[c]; i++)
                {
                    items.Add($"{{\"categoryName\":\"{categories[c]}\"}}");
                }
            }

            JsonElement records = ParseArray($"[{string.Join(",", items)}]");
            string alias = AggregateRecordsTool.ComputeAlias("count", "*");
            var allResults = AggregateRecordsTool.PerformAggregation(records, "count", "*", false, new() { "categoryName" }, null, null, "desc", alias);

            Assert.AreEqual(8, allResults.Count);

            // Apply pagination: first=5
            AggregateRecordsTool.PaginationResult page1 = AggregateRecordsTool.ApplyPagination(allResults, 5, null);

            Assert.AreEqual(5, page1.Items.Count);
            Assert.AreEqual("Confections", page1.Items[0]["categoryName"]?.ToString());
            Assert.AreEqual(13.0, page1.Items[0]["count"]);
            Assert.AreEqual("Dairy", page1.Items[4]["categoryName"]?.ToString());
            Assert.AreEqual(10.0, page1.Items[4]["count"]);
            Assert.IsTrue(page1.HasNextPage);
            Assert.IsNotNull(page1.EndCursor);
        }

        /// <summary>
        /// Spec Example 12: "Show me the next 5 categories" (continuation of Example 11)
        /// COUNT(*) GROUP BY categoryName ORDER BY DESC FIRST 5 AFTER cursor
        /// Expected: 3 items (remaining), hasNextPage=false
        /// </summary>
        [TestMethod]
        public void SpecExample12_CountGroupByOrderByDescFirst5After_ReturnsNextPage()
        {
            List<string> items = new();
            string[] categories = { "Confections", "Beverages", "Condiments", "Seafood", "Dairy", "Grains/Cereals", "Meat/Poultry", "Produce" };
            int[] counts = { 13, 12, 12, 12, 10, 7, 6, 5 };
            for (int c = 0; c < categories.Length; c++)
            {
                for (int i = 0; i < counts[c]; i++)
                {
                    items.Add($"{{\"categoryName\":\"{categories[c]}\"}}");
                }
            }

            JsonElement records = ParseArray($"[{string.Join(",", items)}]");
            string alias = AggregateRecordsTool.ComputeAlias("count", "*");
            var allResults = AggregateRecordsTool.PerformAggregation(records, "count", "*", false, new() { "categoryName" }, null, null, "desc", alias);

            // Page 1
            AggregateRecordsTool.PaginationResult page1 = AggregateRecordsTool.ApplyPagination(allResults, 5, null);
            Assert.IsTrue(page1.HasNextPage);

            // Page 2 (continuation)
            AggregateRecordsTool.PaginationResult page2 = AggregateRecordsTool.ApplyPagination(allResults, 5, page1.EndCursor);

            Assert.AreEqual(3, page2.Items.Count);
            Assert.AreEqual("Grains/Cereals", page2.Items[0]["categoryName"]?.ToString());
            Assert.AreEqual(7.0, page2.Items[0]["count"]);
            Assert.AreEqual("Meat/Poultry", page2.Items[1]["categoryName"]?.ToString());
            Assert.AreEqual(6.0, page2.Items[1]["count"]);
            Assert.AreEqual("Produce", page2.Items[2]["categoryName"]?.ToString());
            Assert.AreEqual(5.0, page2.Items[2]["count"]);
            Assert.IsFalse(page2.HasNextPage);
        }

        /// <summary>
        /// Spec Example 13: "Show me the top 3 most expensive categories by average price"
        /// AVG(unitPrice) GROUP BY categoryName ORDER BY DESC FIRST 3
        /// Expected: Meat/Poultry=54.01, Beverages=37.98, Seafood=37.08
        /// </summary>
        [TestMethod]
        public void SpecExample13_AvgGroupByOrderByDescFirst3_ReturnsTop3()
        {
            // Meat/Poultry: {40.00, 68.02} → avg = 54.01
            // Beverages: {30.96, 45.00} → avg = 37.98
            // Seafood: {25.16, 49.00} → avg = 37.08
            // Condiments: {10.00, 15.00} → avg = 12.50
            JsonElement records = ParseArray(
                "[" +
                "{\"categoryName\":\"Meat/Poultry\",\"unitPrice\":40.00}," +
                "{\"categoryName\":\"Meat/Poultry\",\"unitPrice\":68.02}," +
                "{\"categoryName\":\"Beverages\",\"unitPrice\":30.96}," +
                "{\"categoryName\":\"Beverages\",\"unitPrice\":45.00}," +
                "{\"categoryName\":\"Seafood\",\"unitPrice\":25.16}," +
                "{\"categoryName\":\"Seafood\",\"unitPrice\":49.00}," +
                "{\"categoryName\":\"Condiments\",\"unitPrice\":10.00}," +
                "{\"categoryName\":\"Condiments\",\"unitPrice\":15.00}" +
                "]");
            string alias = AggregateRecordsTool.ComputeAlias("avg", "unitPrice");
            var allResults = AggregateRecordsTool.PerformAggregation(records, "avg", "unitPrice", false, new() { "categoryName" }, null, null, "desc", alias);

            Assert.AreEqual(4, allResults.Count);

            // Apply pagination: first=3
            AggregateRecordsTool.PaginationResult page = AggregateRecordsTool.ApplyPagination(allResults, 3, null);

            Assert.AreEqual(3, page.Items.Count);
            Assert.AreEqual("Meat/Poultry", page.Items[0]["categoryName"]?.ToString());
            Assert.AreEqual(54.01, page.Items[0]["avg_unitPrice"]);
            Assert.AreEqual("Beverages", page.Items[1]["categoryName"]?.ToString());
            Assert.AreEqual(37.98, page.Items[1]["avg_unitPrice"]);
            Assert.AreEqual("Seafood", page.Items[2]["categoryName"]?.ToString());
            Assert.AreEqual(37.08, page.Items[2]["avg_unitPrice"]);
            Assert.IsTrue(page.HasNextPage);
        }

        #endregion

        #region Helper Methods

        private static JsonElement ParseArray(string json)
        {
            return JsonDocument.Parse(json).RootElement;
        }

        private static JsonElement ParseContent(CallToolResult result)
        {
            TextContentBlock firstContent = (TextContentBlock)result.Content[0];
            return JsonDocument.Parse(firstContent.Text).RootElement;
        }

        private static void AssertToolDisabledError(JsonElement content)
        {
            Assert.IsTrue(content.TryGetProperty("error", out JsonElement error));
            Assert.IsTrue(error.TryGetProperty("type", out JsonElement errorType));
            Assert.AreEqual("ToolDisabled", errorType.GetString());
        }

        private static RuntimeConfig CreateConfig(bool aggregateRecordsEnabled = true)
        {
            Dictionary<string, Entity> entities = new()
            {
                ["Book"] = new Entity(
                    Source: new("books", EntitySourceType.Table, null, null),
                    GraphQL: new("Book", "Books"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] {
                        new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null)
                    }) },
                    Mappings: null,
                    Relationships: null,
                    Mcp: null
                )
            };

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(
                        Enabled: true,
                        Path: "/mcp",
                        DmlTools: new(
                            describeEntities: true,
                            readRecords: true,
                            createRecord: true,
                            updateRecord: true,
                            deleteRecord: true,
                            executeEntity: true,
                            aggregateRecords: aggregateRecordsEnabled
                        )
                    ),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(entities)
            );
        }

        private static RuntimeConfig CreateConfigWithEntityDmlDisabled()
        {
            Dictionary<string, Entity> entities = new()
            {
                ["Book"] = new Entity(
                    Source: new("books", EntitySourceType.Table, null, null),
                    GraphQL: new("Book", "Books"),
                    Fields: null,
                    Rest: new(Enabled: true),
                    Permissions: new[] { new EntityPermission(Role: "anonymous", Actions: new[] {
                        new EntityAction(Action: EntityActionOperation.Read, Fields: null, Policy: null)
                    }) },
                    Mappings: null,
                    Relationships: null,
                    Mcp: new EntityMcpOptions(customToolEnabled: false, dmlToolsEnabled: false)
                )
            };

            return new RuntimeConfig(
                Schema: "test-schema",
                DataSource: new DataSource(DatabaseType: DatabaseType.MSSQL, ConnectionString: "", Options: null),
                Runtime: new(
                    Rest: new(),
                    GraphQL: new(),
                    Mcp: new(
                        Enabled: true,
                        Path: "/mcp",
                        DmlTools: new(
                            describeEntities: true,
                            readRecords: true,
                            createRecord: true,
                            updateRecord: true,
                            deleteRecord: true,
                            executeEntity: true,
                            aggregateRecords: true
                        )
                    ),
                    Host: new(Cors: null, Authentication: null, Mode: HostMode.Development)
                ),
                Entities: new(entities)
            );
        }

        private static IServiceProvider CreateServiceProvider(RuntimeConfig config)
        {
            ServiceCollection services = new();

            RuntimeConfigProvider configProvider = TestHelper.GenerateInMemoryRuntimeConfigProvider(config);
            services.AddSingleton(configProvider);

            Mock<IAuthorizationResolver> mockAuthResolver = new();
            mockAuthResolver.Setup(x => x.IsValidRoleContext(It.IsAny<HttpContext>())).Returns(true);
            services.AddSingleton(mockAuthResolver.Object);

            Mock<HttpContext> mockHttpContext = new();
            Mock<HttpRequest> mockRequest = new();
            mockRequest.Setup(x => x.Headers[AuthorizationResolver.CLIENT_ROLE_HEADER]).Returns("anonymous");
            mockHttpContext.Setup(x => x.Request).Returns(mockRequest.Object);

            Mock<IHttpContextAccessor> mockHttpContextAccessor = new();
            mockHttpContextAccessor.Setup(x => x.HttpContext).Returns(mockHttpContext.Object);
            services.AddSingleton(mockHttpContextAccessor.Object);

            services.AddLogging();

            return services.BuildServiceProvider();
        }

        #endregion
    }
}
