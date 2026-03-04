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
                SelectedColumns = new List<string> { "Region", "Amount" }
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
    }
}
