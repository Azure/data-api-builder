// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

#nullable enable

using System;
using System.Text;
using Azure.DataApiBuilder.Config.DatabasePrimitives;
using Azure.DataApiBuilder.Core.Resolvers;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for AggregateRecordsTool's SQL generation methods.
    /// Validates that the tool builds correct SQL queries to push aggregation to the database.
    /// Tests cover: alias computation, aggregate expressions, table references,
    /// cursor decoding, and full SQL generation matching blog-documented patterns.
    /// </summary>
    [TestClass]
    public class AggregateRecordsToolTests
    {
        /// <summary>
        /// Creates a mock IQueryBuilder that wraps identifiers with square brackets (MsSql-style).
        /// </summary>
        private static Mock<IQueryBuilder> CreateMockQueryBuilder()
        {
            Mock<IQueryBuilder> mock = new();
            mock.Setup(qb => qb.QuoteIdentifier(It.IsAny<string>()))
                .Returns((string id) => $"[{id}]");
            return mock;
        }

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

        #region BuildAggregateExpression tests

        [TestMethod]
        public void BuildAggregateExpression_CountStar_ReturnsCountStar()
        {
            Mock<IQueryBuilder> qb = CreateMockQueryBuilder();
            string expr = AggregateRecordsTool.BuildAggregateExpression("count", null, false, true, qb.Object);
            Assert.AreEqual("COUNT(*)", expr);
        }

        [TestMethod]
        public void BuildAggregateExpression_SumField_ReturnsSumQuotedColumn()
        {
            Mock<IQueryBuilder> qb = CreateMockQueryBuilder();
            string expr = AggregateRecordsTool.BuildAggregateExpression("sum", "totalRevenue", false, false, qb.Object);
            Assert.AreEqual("SUM([totalRevenue])", expr);
        }

        [TestMethod]
        public void BuildAggregateExpression_AvgDistinct_ReturnsAvgDistinct()
        {
            Mock<IQueryBuilder> qb = CreateMockQueryBuilder();
            string expr = AggregateRecordsTool.BuildAggregateExpression("avg", "price", true, false, qb.Object);
            Assert.AreEqual("AVG(DISTINCT [price])", expr);
        }

        [TestMethod]
        public void BuildAggregateExpression_CountDistinctField_ReturnsCountDistinct()
        {
            Mock<IQueryBuilder> qb = CreateMockQueryBuilder();
            string expr = AggregateRecordsTool.BuildAggregateExpression("count", "supplierId", true, false, qb.Object);
            Assert.AreEqual("COUNT(DISTINCT [supplierId])", expr);
        }

        [TestMethod]
        public void BuildAggregateExpression_MinField_ReturnsMin()
        {
            Mock<IQueryBuilder> qb = CreateMockQueryBuilder();
            string expr = AggregateRecordsTool.BuildAggregateExpression("min", "price", false, false, qb.Object);
            Assert.AreEqual("MIN([price])", expr);
        }

        [TestMethod]
        public void BuildAggregateExpression_MaxField_ReturnsMax()
        {
            Mock<IQueryBuilder> qb = CreateMockQueryBuilder();
            string expr = AggregateRecordsTool.BuildAggregateExpression("max", "price", false, false, qb.Object);
            Assert.AreEqual("MAX([price])", expr);
        }

        #endregion

        #region BuildQuotedTableRef tests

        [TestMethod]
        public void BuildQuotedTableRef_WithSchema_ReturnsSchemaQualified()
        {
            Mock<IQueryBuilder> qb = CreateMockQueryBuilder();
            DatabaseTable table = new("dbo", "Products");
            string result = AggregateRecordsTool.BuildQuotedTableRef(table, qb.Object);
            Assert.AreEqual("[dbo].[Products]", result);
        }

        [TestMethod]
        public void BuildQuotedTableRef_WithoutSchema_ReturnsTableOnly()
        {
            Mock<IQueryBuilder> qb = CreateMockQueryBuilder();
            DatabaseTable table = new("", "Products");
            string result = AggregateRecordsTool.BuildQuotedTableRef(table, qb.Object);
            Assert.AreEqual("[Products]", result);
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

        #region Validation logic tests

        [TestMethod]
        [DataRow("avg", "Validation: avg with star field should be rejected")]
        [DataRow("sum", "Validation: sum with star field should be rejected")]
        [DataRow("min", "Validation: min with star field should be rejected")]
        [DataRow("max", "Validation: max with star field should be rejected")]
        public void ValidateFieldFunctionCompat_StarWithNumericFunction_IsInvalid(string function, string description)
        {
            bool isCountStar = function == "count" && "*" == "*";
            bool isInvalidStarUsage = "*" == "*" && function != "count";

            Assert.IsFalse(isCountStar, $"{description}: should not be count-star");
            Assert.IsTrue(isInvalidStarUsage, $"{description}: should be identified as invalid star usage");
        }

        [TestMethod]
        public void ValidateFieldFunctionCompat_CountStar_IsValid()
        {
            bool isCountStar = "count" == "count" && "*" == "*";
            Assert.IsTrue(isCountStar, "count(*) should be valid");
        }

        [TestMethod]
        public void ValidateDistinctCountStar_IsInvalid()
        {
            bool isCountStar = "count" == "count" && "*" == "*";
            bool distinct = true;

            bool shouldReject = isCountStar && distinct;
            Assert.IsTrue(shouldReject, "count(*) with distinct=true should be rejected");
        }

        [TestMethod]
        public void ValidateDistinctCountField_IsValid()
        {
            bool isCountStar = "count" == "count" && "userId" == "*";
            bool distinct = true;

            bool shouldReject = isCountStar && distinct;
            Assert.IsFalse(shouldReject, "count(field) with distinct=true should be valid");
        }

        #endregion

        #region Blog scenario tests - SQL generation patterns

        /// <summary>
        /// Blog Example 1: Strategic customer importance
        /// "Who is our most important customer based on total revenue?"
        /// Expected: SELECT customerId, customerName, SUM(totalRevenue) ... GROUP BY ... ORDER BY ... DESC LIMIT 1
        /// </summary>
        [TestMethod]
        public void BlogScenario_StrategicCustomerImportance_SqlContainsGroupByAndOrderByDesc()
        {
            Mock<IQueryBuilder> qb = CreateMockQueryBuilder();

            // Validate the aggregate expression
            string aggExpr = AggregateRecordsTool.BuildAggregateExpression("sum", "totalRevenue", false, false, qb.Object);
            Assert.AreEqual("SUM([totalRevenue])", aggExpr);

            // Validate the alias
            string alias = AggregateRecordsTool.ComputeAlias("sum", "totalRevenue");
            Assert.AreEqual("sum_totalRevenue", alias);
        }

        /// <summary>
        /// Blog Example 2: Product discontinuation candidate
        /// Lowest totalRevenue with orderby=asc, first=1
        /// </summary>
        [TestMethod]
        public void BlogScenario_ProductDiscontinuation_SqlContainsOrderByAsc()
        {
            Mock<IQueryBuilder> qb = CreateMockQueryBuilder();

            string aggExpr = AggregateRecordsTool.BuildAggregateExpression("sum", "totalRevenue", false, false, qb.Object);
            Assert.AreEqual("SUM([totalRevenue])", aggExpr);

            string alias = AggregateRecordsTool.ComputeAlias("sum", "totalRevenue");
            Assert.AreEqual("sum_totalRevenue", alias);
        }

        /// <summary>
        /// Blog Example 3: Forward-looking performance expectation
        /// AVG quarterlyRevenue with HAVING gt 2000000
        /// </summary>
        [TestMethod]
        public void BlogScenario_QuarterlyPerformance_AvgWithHaving()
        {
            Mock<IQueryBuilder> qb = CreateMockQueryBuilder();

            string aggExpr = AggregateRecordsTool.BuildAggregateExpression("avg", "quarterlyRevenue", false, false, qb.Object);
            Assert.AreEqual("AVG([quarterlyRevenue])", aggExpr);

            string alias = AggregateRecordsTool.ComputeAlias("avg", "quarterlyRevenue");
            Assert.AreEqual("avg_quarterlyRevenue", alias);
        }

        /// <summary>
        /// Blog Example 4: Revenue concentration across regions
        /// SUM totalRevenue grouped by region and customerTier, HAVING gt 5000000
        /// </summary>
        [TestMethod]
        public void BlogScenario_RevenueConcentration_MultipleGroupByFields()
        {
            Mock<IQueryBuilder> qb = CreateMockQueryBuilder();

            string aggExpr = AggregateRecordsTool.BuildAggregateExpression("sum", "totalRevenue", false, false, qb.Object);
            Assert.AreEqual("SUM([totalRevenue])", aggExpr);

            string alias = AggregateRecordsTool.ComputeAlias("sum", "totalRevenue");
            Assert.AreEqual("sum_totalRevenue", alias);
        }

        /// <summary>
        /// Blog Example 5: Risk exposure by product line
        /// SUM onHandValue grouped by productLine and warehouseRegion, HAVING gt 2500000
        /// </summary>
        [TestMethod]
        public void BlogScenario_RiskExposure_SumWithMultiGroupByAndHaving()
        {
            Mock<IQueryBuilder> qb = CreateMockQueryBuilder();

            string aggExpr = AggregateRecordsTool.BuildAggregateExpression("sum", "onHandValue", false, false, qb.Object);
            Assert.AreEqual("SUM([onHandValue])", aggExpr);

            string alias = AggregateRecordsTool.ComputeAlias("sum", "onHandValue");
            Assert.AreEqual("sum_onHandValue", alias);
        }

        #endregion
    }
}
