// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using Azure.DataApiBuilder.Core.Resolvers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SemanticModelTests
{
    [TestClass]
    public class DaxQueryBuilderTests
    {
        /// <summary>
        /// A DaxQueryStructure with just TableName should produce EVALUATE\n'TableName'.
        /// </summary>
        [TestMethod]
        public void Build_SimpleTableQuery_GeneratesEvaluateStatement()
        {
            DaxQueryStructure structure = new()
            {
                TableName = "Sales"
            };

            string result = DaxQueryBuilder.Build(structure);

            Assert.AreEqual("EVALUATE\r\n'Sales'", result);
        }

        /// <summary>
        /// With SelectedColumns, should produce SELECTCOLUMNS.
        /// </summary>
        [TestMethod]
        public void Build_WithColumnSelection_GeneratesSelectColumns()
        {
            DaxQueryStructure structure = new()
            {
                TableName = "Sales",
                SelectedColumns = new Dictionary<string, string> { { "Region", "Region" }, { "Amount", "Amount" } }
            };

            string result = DaxQueryBuilder.Build(structure);

            Assert.IsTrue(result.Contains("SELECTCOLUMNS("), "Expected SELECTCOLUMNS in the DAX query.");
            Assert.IsTrue(result.Contains("\"Region\""), "Expected column 'Region' in the DAX query.");
            Assert.IsTrue(result.Contains("\"Amount\""), "Expected column 'Amount' in the DAX query.");
            Assert.IsTrue(result.Contains("'Sales'[Region]"), "Expected qualified column reference for Region.");
            Assert.IsTrue(result.Contains("'Sales'[Amount]"), "Expected qualified column reference for Amount.");
        }

        /// <summary>
        /// With IncludedMeasures, should produce ADDCOLUMNS.
        /// </summary>
        [TestMethod]
        public void Build_WithMeasures_GeneratesAddColumns()
        {
            DaxQueryStructure structure = new()
            {
                TableName = "Sales",
                IncludedMeasures = new Dictionary<string, string>
                {
                    { "TotalSales", "SUM('Sales'[Amount])" }
                }
            };

            string result = DaxQueryBuilder.Build(structure);

            Assert.IsTrue(result.Contains("ADDCOLUMNS("), "Expected ADDCOLUMNS in the DAX query.");
            Assert.IsTrue(result.Contains("\"TotalSales\""), "Expected measure alias 'TotalSales' in the DAX query.");
            Assert.IsTrue(result.Contains("SUM('Sales'[Amount])"), "Expected measure expression in the DAX query.");
        }

        /// <summary>
        /// With FilterPredicates, should produce CALCULATETABLE.
        /// </summary>
        [TestMethod]
        public void Build_WithFilters_GeneratesCalculateTable()
        {
            DaxQueryStructure structure = new()
            {
                TableName = "Sales",
                FilterPredicates = new List<string>
                {
                    "'Sales'[Region] = \"West\""
                }
            };

            string result = DaxQueryBuilder.Build(structure);

            Assert.IsTrue(result.Contains("CALCULATETABLE("), "Expected CALCULATETABLE in the DAX query.");
            Assert.IsTrue(result.Contains("'Sales'[Region] = \"West\""), "Expected filter predicate in the DAX query.");
        }

        /// <summary>
        /// With TopCount, should produce TOPN.
        /// </summary>
        [TestMethod]
        public void Build_WithTopCount_GeneratesTopN()
        {
            DaxQueryStructure structure = new()
            {
                TableName = "Sales",
                TopCount = 10
            };

            string result = DaxQueryBuilder.Build(structure);

            Assert.IsTrue(result.Contains("TOPN("), "Expected TOPN in the DAX query.");
            Assert.IsTrue(result.Contains("10"), "Expected top count value 10 in the DAX query.");
        }

        /// <summary>
        /// With OrderByColumns, should produce ORDER BY clause.
        /// </summary>
        [TestMethod]
        public void Build_WithOrderBy_GeneratesOrderByClause()
        {
            DaxQueryStructure structure = new()
            {
                TableName = "Sales",
                OrderByColumns = new List<(string Column, bool IsAscending)>
                {
                    ("Amount", false)
                }
            };

            string result = DaxQueryBuilder.Build(structure);

            Assert.IsTrue(result.Contains("ORDER BY"), "Expected ORDER BY in the DAX query.");
            Assert.IsTrue(result.Contains("'Sales'[Amount] DESC"), "Expected descending order on Amount.");
        }

        /// <summary>
        /// QuoteTableName should escape single quotes by doubling them.
        /// </summary>
        [TestMethod]
        public void QuoteTableName_EscapesSingleQuotes()
        {
            string result = DaxQueryBuilder.QuoteTableName("Mike's Sales");

            Assert.AreEqual("'Mike''s Sales'", result);
        }

        /// <summary>
        /// QuoteColumnName should escape closing brackets by doubling them.
        /// </summary>
        [TestMethod]
        public void QuoteColumnName_EscapesBrackets()
        {
            string result = DaxQueryBuilder.QuoteColumnName("Column]Name");

            Assert.AreEqual("[Column]]Name]", result);
        }

        /// <summary>
        /// BuildEqualityFilter should generate a correct equality filter expression.
        /// </summary>
        [TestMethod]
        public void BuildEqualityFilter_GeneratesCorrectFilter()
        {
            string result = DaxQueryBuilder.BuildEqualityFilter("Sales", "Region", "\"West\"");

            Assert.AreEqual("'Sales'[Region] = \"West\"", result);
        }

        /// <summary>
        /// A groupBy query with a single group-by column and one aggregation
        /// should produce SUMMARIZECOLUMNS.
        /// </summary>
        [TestMethod]
        public void Build_GroupBy_SingleColumn_SingleAggregation()
        {
            DaxQueryStructure structure = new()
            {
                TableName = "Sales",
                IsGroupByQuery = true,
                GroupByColumns = new Dictionary<string, string> { { "Region", "Region" } },
                AggregationExpressions = new Dictionary<string, string>
                {
                    { "sum", "SUMX('Sales', 'Sales'[Amount])" }
                }
            };

            string result = DaxQueryBuilder.Build(structure);

            Assert.IsTrue(result.Contains("SUMMARIZECOLUMNS("), "Expected SUMMARIZECOLUMNS.");
            Assert.IsTrue(result.Contains("'Sales'[Region]"), "Expected group-by column.");
            Assert.IsTrue(result.Contains("\"sum\", SUMX('Sales', 'Sales'[Amount])"), "Expected aggregation expression.");
            Assert.IsFalse(result.Contains("SELECTCOLUMNS("), "Should NOT contain SELECTCOLUMNS.");
            Assert.IsFalse(result.Contains("ADDCOLUMNS("), "Should NOT contain ADDCOLUMNS.");
        }

        /// <summary>
        /// A groupBy query with multiple group-by columns, ad-hoc aggregations, and measures.
        /// </summary>
        [TestMethod]
        public void Build_GroupBy_MultipleColumns_MixedAggregations()
        {
            DaxQueryStructure structure = new()
            {
                TableName = "Sales",
                IsGroupByQuery = true,
                GroupByColumns = new Dictionary<string, string>
                {
                    { "Region", "Region" },
                    { "Category", "Category" }
                },
                AggregationExpressions = new Dictionary<string, string>
                {
                    { "max", "MAXX('Sales', 'Sales'[Amount])" },
                    { "count", "COUNTAX('Sales', 'Sales'[Amount])" }
                },
                GroupByMeasures = new Dictionary<string, string>
                {
                    { "Total_Sales", "[Sales]" }
                }
            };

            string result = DaxQueryBuilder.Build(structure);

            Assert.IsTrue(result.Contains("SUMMARIZECOLUMNS("), "Expected SUMMARIZECOLUMNS.");
            Assert.IsTrue(result.Contains("'Sales'[Region]"), "Expected first group-by column.");
            Assert.IsTrue(result.Contains("'Sales'[Category]"), "Expected second group-by column.");
            Assert.IsTrue(result.Contains("\"max\", MAXX('Sales', 'Sales'[Amount])"), "Expected MAX aggregation.");
            Assert.IsTrue(result.Contains("\"count\", COUNTAX('Sales', 'Sales'[Amount])"), "Expected COUNT aggregation.");
            Assert.IsTrue(result.Contains("\"Total_Sales\", [Sales]"), "Expected measure reference.");
        }

        /// <summary>
        /// A groupBy query with filter predicates should use KEEPFILTERS(FILTER(ALL(...), ...)).
        /// </summary>
        [TestMethod]
        public void Build_GroupBy_WithFilter()
        {
            DaxQueryStructure structure = new()
            {
                TableName = "Sales",
                IsGroupByQuery = true,
                GroupByColumns = new Dictionary<string, string> { { "Region", "Region" } },
                AggregationExpressions = new Dictionary<string, string>
                {
                    { "sum", "SUMX('Sales', 'Sales'[Amount])" }
                },
                FilterPredicates = new List<string> { "'Sales'[Year] = 2024" }
            };

            string result = DaxQueryBuilder.Build(structure);

            Assert.IsTrue(result.Contains("KEEPFILTERS(FILTER(ALL('Sales'), 'Sales'[Year] = 2024))"),
                "Expected KEEPFILTERS/FILTER/ALL wrapping for filter predicate in SUMMARIZECOLUMNS.");
        }

        /// <summary>
        /// A groupBy query with TOPN should wrap SUMMARIZECOLUMNS in TOPN.
        /// </summary>
        [TestMethod]
        public void Build_GroupBy_WithTopN()
        {
            DaxQueryStructure structure = new()
            {
                TableName = "Sales",
                IsGroupByQuery = true,
                TopCount = 5,
                GroupByColumns = new Dictionary<string, string> { { "Region", "Region" } },
                AggregationExpressions = new Dictionary<string, string>
                {
                    { "sum", "SUMX('Sales', 'Sales'[Amount])" }
                }
            };

            string result = DaxQueryBuilder.Build(structure);

            Assert.IsTrue(result.Contains("TOPN("), "Expected TOPN wrapping.");
            Assert.IsTrue(result.Contains("SUMMARIZECOLUMNS("), "Expected SUMMARIZECOLUMNS inside TOPN.");
            Assert.IsTrue(result.Contains("5"), "Expected top count 5.");
        }

        /// <summary>
        /// A groupBy query with ORDER BY should append ORDER BY after SUMMARIZECOLUMNS.
        /// </summary>
        [TestMethod]
        public void Build_GroupBy_WithOrderBy()
        {
            DaxQueryStructure structure = new()
            {
                TableName = "Sales",
                IsGroupByQuery = true,
                GroupByColumns = new Dictionary<string, string> { { "Region", "Region" } },
                AggregationExpressions = new Dictionary<string, string>
                {
                    { "sum", "SUMX('Sales', 'Sales'[Amount])" }
                },
                OrderByColumns = new List<(string, bool)> { ("Region", true) }
            };

            string result = DaxQueryBuilder.Build(structure);

            Assert.IsTrue(result.Contains("ORDER BY"), "Expected ORDER BY clause.");
        }

        /// <summary>
        /// BuildAggregationExpression should generate correct DAX for each aggregation type.
        /// </summary>
        [TestMethod]
        public void BuildAggregationExpression_AllTypes()
        {
            Assert.AreEqual("SUMX('Sales', 'Sales'[Amount])", DaxQueryBuilder.BuildAggregationExpression("sum", "Sales", "Amount"));
            Assert.AreEqual("AVERAGEX('Sales', 'Sales'[Amount])", DaxQueryBuilder.BuildAggregationExpression("avg", "Sales", "Amount"));
            Assert.AreEqual("MINX('Sales', 'Sales'[Amount])", DaxQueryBuilder.BuildAggregationExpression("min", "Sales", "Amount"));
            Assert.AreEqual("MAXX('Sales', 'Sales'[Amount])", DaxQueryBuilder.BuildAggregationExpression("max", "Sales", "Amount"));
            Assert.AreEqual("COUNTAX('Sales', 'Sales'[Amount])", DaxQueryBuilder.BuildAggregationExpression("count", "Sales", "Amount"));
            Assert.AreEqual("DISTINCTCOUNT('Sales'[Amount])", DaxQueryBuilder.BuildAggregationExpression("count", "Sales", "Amount", distinct: true));
        }

        /// <summary>
        /// A groupBy query with only measures (no ad-hoc aggregations) should work.
        /// </summary>
        [TestMethod]
        public void Build_GroupBy_MeasuresOnly()
        {
            DaxQueryStructure structure = new()
            {
                TableName = "customer",
                IsGroupByQuery = true,
                GroupByColumns = new Dictionary<string, string> { { "State", "State" } },
                GroupByMeasures = new Dictionary<string, string>
                {
                    { "Sales", "[Sales]" },
                    { "Margin_pct", "[Margin %]" }
                }
            };

            string result = DaxQueryBuilder.Build(structure);

            Assert.IsTrue(result.Contains("SUMMARIZECOLUMNS("), "Expected SUMMARIZECOLUMNS.");
            Assert.IsTrue(result.Contains("'customer'[State]"), "Expected group-by column.");
            Assert.IsTrue(result.Contains("\"Sales\", [Sales]"), "Expected Sales measure.");
            Assert.IsTrue(result.Contains("\"Margin_pct\", [Margin %]"), "Expected Margin % measure.");
        }
    }
}
