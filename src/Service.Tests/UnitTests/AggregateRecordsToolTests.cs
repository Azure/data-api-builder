// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Text;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for AggregateRecordsTool helper methods.
    /// Validates alias computation, cursor decoding, and input validation logic.
    /// SQL generation is delegated to the engine's query builder (GroupByMetadata/AggregationColumn).
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

        #region DecodeCursorOffset tests

        [TestMethod]
        public void DecodeCursorOffset_NullCursor_ReturnsZero()
        {
            Assert.AreEqual(0, AggregateRecordsTool.DecodeCursorOffset(null));
        }

        [TestMethod]
        public void DecodeCursorOffset_EmptyCursor_ReturnsZero()
        {
            Assert.AreEqual(0, AggregateRecordsTool.DecodeCursorOffset(""));
        }

        [TestMethod]
        public void DecodeCursorOffset_ValidBase64_ReturnsOffset()
        {
            string cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes("5"));
            Assert.AreEqual(5, AggregateRecordsTool.DecodeCursorOffset(cursor));
        }

        [TestMethod]
        public void DecodeCursorOffset_InvalidBase64_ReturnsZero()
        {
            Assert.AreEqual(0, AggregateRecordsTool.DecodeCursorOffset("not-valid-base64!!"));
        }

        [TestMethod]
        public void DecodeCursorOffset_NonNumericBase64_ReturnsZero()
        {
            string cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes("abc"));
            Assert.AreEqual(0, AggregateRecordsTool.DecodeCursorOffset(cursor));
        }

        [TestMethod]
        public void DecodeCursorOffset_RoundTrip_FirstPage()
        {
            int offset = 3;
            string cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes(offset.ToString()));
            Assert.AreEqual(offset, AggregateRecordsTool.DecodeCursorOffset(cursor));
        }

        [TestMethod]
        public void DecodeCursorOffset_NegativeValue_ReturnsZero()
        {
            string cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes("-5"));
            Assert.AreEqual(0, AggregateRecordsTool.DecodeCursorOffset(cursor));
        }

        #endregion

        #region Blog scenario tests - alias and type validation

        /// <summary>
        /// Blog Example 1: Strategic customer importance
        /// "Who is our most important customer based on total revenue?"
        /// SUM(totalRevenue) grouped by customerId, customerName, ORDER BY DESC, FIRST 1
        /// </summary>
        [TestMethod]
        public void BlogScenario_StrategicCustomerImportance_AliasAndTypeCorrect()
        {
            string alias = AggregateRecordsTool.ComputeAlias("sum", "totalRevenue");
            Assert.AreEqual("sum_totalRevenue", alias);
        }

        /// <summary>
        /// Blog Example 2: Product discontinuation candidate
        /// Lowest totalRevenue with orderby=asc, first=1
        /// </summary>
        [TestMethod]
        public void BlogScenario_ProductDiscontinuation_AliasAndTypeCorrect()
        {
            string alias = AggregateRecordsTool.ComputeAlias("sum", "totalRevenue");
            Assert.AreEqual("sum_totalRevenue", alias);
        }

        /// <summary>
        /// Blog Example 3: Forward-looking performance expectation
        /// AVG quarterlyRevenue with HAVING gt 2000000
        /// </summary>
        [TestMethod]
        public void BlogScenario_QuarterlyPerformance_AliasAndTypeCorrect()
        {
            string alias = AggregateRecordsTool.ComputeAlias("avg", "quarterlyRevenue");
            Assert.AreEqual("avg_quarterlyRevenue", alias);
        }

        /// <summary>
        /// Blog Example 4: Revenue concentration across regions
        /// SUM totalRevenue grouped by region and customerTier, HAVING gt 5000000
        /// </summary>
        [TestMethod]
        public void BlogScenario_RevenueConcentration_AliasAndTypeCorrect()
        {
            string alias = AggregateRecordsTool.ComputeAlias("sum", "totalRevenue");
            Assert.AreEqual("sum_totalRevenue", alias);
        }

        /// <summary>
        /// Blog Example 5: Risk exposure by product line
        /// SUM onHandValue grouped by productLine and warehouseRegion, HAVING gt 2500000
        /// </summary>
        [TestMethod]
        public void BlogScenario_RiskExposure_AliasAndTypeCorrect()
        {
            string alias = AggregateRecordsTool.ComputeAlias("sum", "onHandValue");
            Assert.AreEqual("sum_onHandValue", alias);
        }

        #endregion
    }
}
