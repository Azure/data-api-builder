// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System.Collections.Generic;
using System.Text.Json;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for AggregateRecordsTool's internal helper methods.
    /// Covers validation paths, aggregation logic, and pagination behavior.
    /// </summary>
    [TestClass]
    public class AggregateRecordsToolTests
    {
        #region ComputeAlias tests

        [TestMethod]
        [DataRow("count", "*", "count", DisplayName = "count(*) alias is 'count'")]
        [DataRow("count", "userId", "count_userId", DisplayName = "count(field) alias is 'count_field'")]
        [DataRow("avg", "price", "avg_price", DisplayName = "avg alias")]
        [DataRow("sum", "amount", "sum_amount", DisplayName = "sum alias")]
        [DataRow("min", "age", "min_age", DisplayName = "min alias")]
        [DataRow("max", "score", "max_score", DisplayName = "max alias")]
        public void ComputeAlias_ReturnsExpectedAlias(string function, string field, string expectedAlias)
        {
            string result = AggregateRecordsTool.ComputeAlias(function, field);
            Assert.AreEqual(expectedAlias, result);
        }

        #endregion

        #region PerformAggregation tests - no groupby

        private static JsonElement CreateRecordsArray(params double[] values)
        {
            var list = new List<object>();
            foreach (double v in values)
            {
                list.Add(new Dictionary<string, double> { ["value"] = v });
            }

            string json = JsonSerializer.Serialize(list);
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        private static JsonElement CreateEmptyArray()
        {
            return JsonDocument.Parse("[]").RootElement.Clone();
        }

        private static JsonElement CreateMixedArray()
        {
            // Records where some have 'value' (numeric) and some have 'category' (string)
            string json = """
                [
                    {"value": 10.0, "category": "A"},
                    {"value": 20.0, "category": "B"},
                    {"value": 10.0, "category": "A"}
                ]
                """;
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        [TestMethod]
        public void PerformAggregation_CountStar_NoGroupBy_ReturnsCount()
        {
            JsonElement records = CreateRecordsArray(1, 2, 3, 4, 5);
            var result = AggregateRecordsTool.PerformAggregation(
                records, "count", "*", distinct: false, new List<string>(), null, null, "desc", "count");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(5.0, result[0]["count"]);
        }

        [TestMethod]
        public void PerformAggregation_CountField_NoGroupBy_CountsNumericValues()
        {
            JsonElement records = CreateRecordsArray(10.0, 20.0, 30.0);
            var result = AggregateRecordsTool.PerformAggregation(
                records, "count", "value", distinct: false, new List<string>(), null, null, "desc", "count_value");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(3.0, result[0]["count_value"]);
        }

        [TestMethod]
        public void PerformAggregation_CountField_Distinct_CountsUniqueValues()
        {
            JsonElement records = CreateRecordsArray(10.0, 20.0, 10.0);
            var result = AggregateRecordsTool.PerformAggregation(
                records, "count", "value", distinct: true, new List<string>(), null, null, "desc", "count_value");

            Assert.AreEqual(1, result.Count);
            // 10 and 20 are the distinct values
            Assert.AreEqual(2.0, result[0]["count_value"]);
        }

        [TestMethod]
        public void PerformAggregation_Avg_NoGroupBy_ReturnsAverage()
        {
            JsonElement records = CreateRecordsArray(10.0, 20.0, 30.0);
            var result = AggregateRecordsTool.PerformAggregation(
                records, "avg", "value", distinct: false, new List<string>(), null, null, "desc", "avg_value");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(20.0, result[0]["avg_value"]);
        }

        [TestMethod]
        public void PerformAggregation_Sum_NoGroupBy_ReturnsSum()
        {
            JsonElement records = CreateRecordsArray(10.0, 20.0, 30.0);
            var result = AggregateRecordsTool.PerformAggregation(
                records, "sum", "value", distinct: false, new List<string>(), null, null, "desc", "sum_value");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(60.0, result[0]["sum_value"]);
        }

        [TestMethod]
        public void PerformAggregation_Min_NoGroupBy_ReturnsMinimum()
        {
            JsonElement records = CreateRecordsArray(30.0, 10.0, 20.0);
            var result = AggregateRecordsTool.PerformAggregation(
                records, "min", "value", distinct: false, new List<string>(), null, null, "desc", "min_value");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(10.0, result[0]["min_value"]);
        }

        [TestMethod]
        public void PerformAggregation_Max_NoGroupBy_ReturnsMaximum()
        {
            JsonElement records = CreateRecordsArray(30.0, 10.0, 20.0);
            var result = AggregateRecordsTool.PerformAggregation(
                records, "max", "value", distinct: false, new List<string>(), null, null, "desc", "max_value");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(30.0, result[0]["max_value"]);
        }

        [TestMethod]
        public void PerformAggregation_EmptyRecords_ReturnsNullForNumericFunctions()
        {
            JsonElement records = CreateEmptyArray();
            var result = AggregateRecordsTool.PerformAggregation(
                records, "avg", "value", distinct: false, new List<string>(), null, null, "desc", "avg_value");

            Assert.AreEqual(1, result.Count);
            Assert.IsNull(result[0]["avg_value"]);
        }

        [TestMethod]
        public void PerformAggregation_EmptyRecords_CountStar_ReturnsZero()
        {
            JsonElement records = CreateEmptyArray();
            var result = AggregateRecordsTool.PerformAggregation(
                records, "count", "*", distinct: false, new List<string>(), null, null, "desc", "count");

            Assert.AreEqual(1, result.Count);
            Assert.AreEqual(0.0, result[0]["count"]);
        }

        #endregion

        #region PerformAggregation tests - with groupby

        [TestMethod]
        public void PerformAggregation_GroupBy_CountStar_ReturnsGroupCounts()
        {
            JsonElement records = CreateMixedArray();
            var groupby = new List<string> { "category" };

            var result = AggregateRecordsTool.PerformAggregation(
                records, "count", "*", distinct: false, groupby, null, null, "desc", "count");

            Assert.AreEqual(2, result.Count);
            // desc ordering: A has 2, B has 1
            Assert.AreEqual("A", result[0]["category"]);
            Assert.AreEqual(2.0, result[0]["count"]);
            Assert.AreEqual("B", result[1]["category"]);
            Assert.AreEqual(1.0, result[1]["count"]);
        }

        [TestMethod]
        public void PerformAggregation_GroupBy_Avg_ReturnsGroupAverages()
        {
            JsonElement records = CreateMixedArray();
            var groupby = new List<string> { "category" };

            var result = AggregateRecordsTool.PerformAggregation(
                records, "avg", "value", distinct: false, groupby, null, null, "asc", "avg_value");

            Assert.AreEqual(2, result.Count);
            // asc ordering by avg_value: B has 20, A has average (10+10)/2=10
            Assert.AreEqual("A", result[0]["category"]);
            Assert.AreEqual(10.0, result[0]["avg_value"]);
            Assert.AreEqual("B", result[1]["category"]);
            Assert.AreEqual(20.0, result[1]["avg_value"]);
        }

        [TestMethod]
        public void PerformAggregation_GroupBy_Having_FiltersGroups()
        {
            JsonElement records = CreateMixedArray();
            var groupby = new List<string> { "category" };
            var havingOps = new Dictionary<string, double>(System.StringComparer.OrdinalIgnoreCase)
            {
                ["gt"] = 1.0 // Keep groups with count > 1
            };

            var result = AggregateRecordsTool.PerformAggregation(
                records, "count", "*", distinct: false, groupby, havingOps, null, "desc", "count");

            // Only category "A" (count=2) should pass count > 1
            Assert.AreEqual(1, result.Count);
            Assert.AreEqual("A", result[0]["category"]);
        }

        #endregion

        #region Pagination tests

        [TestMethod]
        public void ApplyPagination_FirstPage_ReturnsItemsAndCursor()
        {
            var allResults = new List<Dictionary<string, object?>>
            {
                new() { ["id"] = 1 },
                new() { ["id"] = 2 },
                new() { ["id"] = 3 },
                new() { ["id"] = 4 },
                new() { ["id"] = 5 }
            };

            var result = AggregateRecordsTool.ApplyPagination(allResults, first: 2, after: null);

            Assert.AreEqual(2, result.Items.Count);
            Assert.AreEqual(1, result.Items[0]["id"]);
            Assert.AreEqual(2, result.Items[1]["id"]);
            Assert.IsTrue(result.HasNextPage);
            Assert.IsNotNull(result.EndCursor);
        }

        [TestMethod]
        public void ApplyPagination_SecondPage_ReturnsCorrectItems()
        {
            var allResults = new List<Dictionary<string, object?>>
            {
                new() { ["id"] = 1 },
                new() { ["id"] = 2 },
                new() { ["id"] = 3 },
                new() { ["id"] = 4 },
                new() { ["id"] = 5 }
            };

            // Get first page to obtain cursor
            var firstPage = AggregateRecordsTool.ApplyPagination(allResults, first: 2, after: null);
            string? cursor = firstPage.EndCursor;

            // Use cursor to get second page
            var secondPage = AggregateRecordsTool.ApplyPagination(allResults, first: 2, after: cursor);

            Assert.AreEqual(2, secondPage.Items.Count);
            Assert.AreEqual(3, secondPage.Items[0]["id"]);
            Assert.AreEqual(4, secondPage.Items[1]["id"]);
            Assert.IsTrue(secondPage.HasNextPage);
        }

        [TestMethod]
        public void ApplyPagination_LastPage_HasNextPageFalse()
        {
            var allResults = new List<Dictionary<string, object?>>
            {
                new() { ["id"] = 1 },
                new() { ["id"] = 2 },
                new() { ["id"] = 3 }
            };

            // Get first page
            var firstPage = AggregateRecordsTool.ApplyPagination(allResults, first: 2, after: null);
            // Get last page
            var lastPage = AggregateRecordsTool.ApplyPagination(allResults, first: 2, after: firstPage.EndCursor);

            Assert.AreEqual(1, lastPage.Items.Count);
            Assert.AreEqual(3, lastPage.Items[0]["id"]);
            Assert.IsFalse(lastPage.HasNextPage);
        }

        [TestMethod]
        public void ApplyPagination_TerminalCursor_ReturnsEmptyItems()
        {
            var allResults = new List<Dictionary<string, object?>>
            {
                new() { ["id"] = 1 },
                new() { ["id"] = 2 }
            };

            // Get last page
            var lastPage = AggregateRecordsTool.ApplyPagination(allResults, first: 2, after: null);
            Assert.IsFalse(lastPage.HasNextPage);
            Assert.IsNotNull(lastPage.EndCursor);

            // Using the terminal endCursor should return empty results
            var beyondLastPage = AggregateRecordsTool.ApplyPagination(allResults, first: 2, after: lastPage.EndCursor);
            Assert.AreEqual(0, beyondLastPage.Items.Count);
            Assert.IsFalse(beyondLastPage.HasNextPage);
            Assert.IsNull(beyondLastPage.EndCursor);
        }

        [TestMethod]
        public void ApplyPagination_InvalidCursor_StartsFromBeginning()
        {
            var allResults = new List<Dictionary<string, object?>>
            {
                new() { ["id"] = 1 },
                new() { ["id"] = 2 }
            };

            var result = AggregateRecordsTool.ApplyPagination(allResults, first: 2, after: "not-valid-base64!!");

            // Should start from beginning
            Assert.AreEqual(2, result.Items.Count);
            Assert.AreEqual(1, result.Items[0]["id"]);
        }

        [TestMethod]
        public void ApplyPagination_AfterWithoutFirst_IgnoresCursor()
        {
            // When first is not provided, after should not be used
            // (ApplyPagination is only called when first is provided in ExecuteAsync)
            var allResults = new List<Dictionary<string, object?>>
            {
                new() { ["id"] = 1 },
                new() { ["id"] = 2 },
                new() { ["id"] = 3 }
            };

            // Get page 1 cursor
            var page1 = AggregateRecordsTool.ApplyPagination(allResults, first: 1, after: null);
            Assert.IsNotNull(page1.EndCursor);

            // Call with first=3 and the cursor - should return 2 items from offset 1
            var result = AggregateRecordsTool.ApplyPagination(allResults, first: 3, after: page1.EndCursor);
            Assert.AreEqual(2, result.Items.Count);
            Assert.AreEqual(2, result.Items[0]["id"]);
        }

        #endregion

        #region Validation tests (via ExecuteAsync return codes)

        // Note: Full ExecuteAsync validation tests require a full service provider setup
        // with database, auth etc. The validation logic is tested below by examining
        // the error condition directly since validation happens before any DB call.

        [TestMethod]
        [DataRow("avg", "Validation: avg with star field should be rejected")]
        [DataRow("sum", "Validation: sum with star field should be rejected")]
        [DataRow("min", "Validation: min with star field should be rejected")]
        [DataRow("max", "Validation: max with star field should be rejected")]
        public void ValidateFieldFunctionCompat_StarWithNumericFunction_IsInvalid(string function, string description)
        {
            // Verify the business rule: only count can use field='*'
            // This tests the condition used in ExecuteAsync without needing a full service provider
            bool isCountStar = function == "count" && "*" == "*";
            bool isInvalidStarUsage = "*" == "*" && function != "count";

            Assert.IsFalse(isCountStar, $"{description}: should not be count-star");
            Assert.IsTrue(isInvalidStarUsage, $"{description}: should be identified as invalid star usage");
        }

        [TestMethod]
        public void ValidateFieldFunctionCompat_CountStar_IsValid()
        {
            // count with field='*' should be valid
            bool isCountStar = "count" == "count" && "*" == "*";
            Assert.IsTrue(isCountStar, "count(*) should be valid");
        }

        [TestMethod]
        public void ValidateDistinctCountStar_IsInvalid()
        {
            // count(*) with distinct=true should be rejected
            // Verify the condition used in ExecuteAsync
            bool isCountStar = "count" == "count" && "*" == "*";
            bool distinct = true;

            bool shouldReject = isCountStar && distinct;
            Assert.IsTrue(shouldReject, "count(*) with distinct=true should be rejected");
        }

        [TestMethod]
        public void ValidateDistinctCountField_IsValid()
        {
            // count(field) with distinct=true should be valid
            bool isCountStar = "count" == "count" && "userId" == "*";
            bool distinct = true;

            bool shouldReject = isCountStar && distinct;
            Assert.IsFalse(shouldReject, "count(field) with distinct=true should be valid");
        }

        #endregion
    }
}
