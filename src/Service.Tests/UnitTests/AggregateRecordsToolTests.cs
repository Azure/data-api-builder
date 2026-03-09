// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text;
using Azure.DataApiBuilder.Mcp.BuiltInTools;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.UnitTests
{
    /// <summary>
    /// Unit tests for AggregateRecordsTool helper methods that supplement
    /// the integration tests in Service.Tests/Mcp/AggregateRecordsToolTests.cs.
    /// Covers edge cases (blog aliases, negative offsets) not present in the Mcp test suite.
    /// </summary>
    [TestClass]
    public class AggregateRecordsToolTests
    {
        #region ComputeAlias - Blog scenario aliases (not covered in Mcp tests)

        [DataTestMethod]
        [DataRow("sum", "totalRevenue", "sum_totalRevenue", DisplayName = "Blog: sum(totalRevenue)")]
        [DataRow("avg", "quarterlyRevenue", "avg_quarterlyRevenue", DisplayName = "Blog: avg(quarterlyRevenue)")]
        [DataRow("sum", "onHandValue", "sum_onHandValue", DisplayName = "Blog: sum(onHandValue)")]
        public void ComputeAlias_BlogScenarios_ReturnsExpectedAlias(string function, string field, string expectedAlias)
        {
            Assert.AreEqual(expectedAlias, AggregateRecordsTool.ComputeAlias(function, field));
        }

        #endregion

        #region DecodeCursorOffset - Negative offset edge case (not covered in Mcp tests)

        [TestMethod]
        public void DecodeCursorOffset_NegativeOffset_ReturnsZero()
        {
            string cursor = Convert.ToBase64String(Encoding.UTF8.GetBytes("-5"));
            Assert.AreEqual(0, AggregateRecordsTool.DecodeCursorOffset(cursor));
        }

        #endregion
    }
}
