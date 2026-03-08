// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for AggregateRecordsTool helper methods.
    /// Validates alias computation, cursor decoding, and error message builders.
    /// SQL generation is delegated to the engine's query builder (GroupByMetadata/AggregationColumn).
    /// </summary>
    [TestClass]
    public class AggregateRecordsToolTests
    {
        #region ComputeAlias tests

        [DataTestMethod]
        [DataRow("count", "*", "count", DisplayName = "count(*) → 'count'")]
        [DataRow("count", "userId", "count_userId", DisplayName = "count(userId) → 'count_userId'")]
        [DataRow("avg", "price", "avg_price", DisplayName = "avg(price) → 'avg_price'")]
        [DataRow("sum", "amount", "sum_amount", DisplayName = "sum(amount) → 'sum_amount'")]
        [DataRow("min", "age", "min_age", DisplayName = "min(age) → 'min_age'")]
        [DataRow("max", "score", "max_score", DisplayName = "max(score) → 'max_score'")]
        // Blog scenario aliases
        [DataRow("sum", "totalRevenue", "sum_totalRevenue", DisplayName = "Blog: sum(totalRevenue) → 'sum_totalRevenue'")]
        [DataRow("avg", "quarterlyRevenue", "avg_quarterlyRevenue", DisplayName = "Blog: avg(quarterlyRevenue) → 'avg_quarterlyRevenue'")]
        [DataRow("sum", "onHandValue", "sum_onHandValue", DisplayName = "Blog: sum(onHandValue) → 'sum_onHandValue'")]
        public void ComputeAlias_ReturnsExpectedAlias(string function, string field, string expectedAlias)
        {
            Assert.AreEqual(expectedAlias, AggregateRecordsTool.ComputeAlias(function, field));
        }

        #endregion

        #region DecodeCursorOffset tests

        [DataTestMethod]
        [DataRow(null, 0, DisplayName = "null cursor → 0")]
        [DataRow("", 0, DisplayName = "empty cursor → 0")]
        [DataRow("not-valid-base64!!", 0, DisplayName = "invalid base64 → 0")]
        public void DecodeCursorOffset_InvalidInput_ReturnsZero(string? cursor, int expected)
        {
            Assert.AreEqual(expected, AggregateRecordsTool.DecodeCursorOffset(cursor));
        }

        [DataTestMethod]
        [DataRow("abc", 0, DisplayName = "non-numeric base64 → 0")]
        [DataRow("-5", 0, DisplayName = "negative offset → 0")]
        [DataRow("0", 0, DisplayName = "zero offset → 0")]
        [DataRow("3", 3, DisplayName = "offset 3 round-trip")]
        [DataRow("5", 5, DisplayName = "offset 5 round-trip")]
        [DataRow("1000", 1000, DisplayName = "large offset round-trip")]
        public void DecodeCursorOffset_Base64EncodedValue_ReturnsExpectedOffset(string rawValue, int expectedOffset)
        {
            string cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(rawValue));
            Assert.AreEqual(expectedOffset, AggregateRecordsTool.DecodeCursorOffset(cursor));
        }

        #endregion

        #region Error message builder tests

        [DataTestMethod]
        [DataRow("Product", DisplayName = "Product entity")]
        [DataRow("LargeProductCatalog", DisplayName = "LargeProductCatalog entity")]
        public void BuildTimeoutErrorMessage_ContainsExpectedContent(string entityName)
        {
            string message = AggregateRecordsTool.BuildTimeoutErrorMessage(entityName);
            AssertErrorMessageContains(message, entityName, "NOT a tool error", "filter", "groupby", "first");
        }

        [DataTestMethod]
        [DataRow("Product", DisplayName = "Product entity")]
        public void BuildTaskCanceledErrorMessage_ContainsExpectedContent(string entityName)
        {
            string message = AggregateRecordsTool.BuildTaskCanceledErrorMessage(entityName);
            AssertErrorMessageContains(message, entityName, "NOT a tool error", "timeout", "filter", "first");
        }

        [DataTestMethod]
        [DataRow("LargeProductCatalog", DisplayName = "LargeProductCatalog entity")]
        public void BuildOperationCanceledErrorMessage_ContainsExpectedContent(string entityName)
        {
            string message = AggregateRecordsTool.BuildOperationCanceledErrorMessage(entityName);
            AssertErrorMessageContains(message, entityName, "NOT a tool error", "No results were returned");
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Asserts that the error message contains all expected substrings.
        /// </summary>
        private static void AssertErrorMessageContains(string message, params string[] expectedSubstrings)
        {
            Assert.IsNotNull(message);
            foreach (string expected in expectedSubstrings)
            {
                Assert.IsTrue(message.Contains(expected),
                    $"Error message must contain '{expected}'. Actual: '{message}'");
            }
        }

        #endregion
    }
}
