// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLFilterTests
{

    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlGQLFilterTests : GraphQLFilterTestBase
    {
        protected static string DEFAULT_SCHEMA = string.Empty;

        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task Setup(TestContext context)
        {
            DatabaseEngine = TestCategory.MYSQL;
            await InitializeTestFixture();
        }

        [Ignore]
        [TestMethod]
        public void TestNestedFilterManyOne()
        {
            throw new System.NotImplementedException("Nested Filtering for MySQL is not yet implemented.");
        }

        [Ignore]
        [TestMethod]
        public void TestNestedFilterOneMany()
        {
            throw new System.NotImplementedException("Nested Filtering for MySQL is not yet implemented.");
        }

        [Ignore]
        [TestMethod]
        public void TestNestedFilterManyMany()
        {
            throw new System.NotImplementedException("Nested Filtering for MySQL is not yet implemented.");
        }

        [Ignore]
        [TestMethod]
        public void TestNestedFilterFieldIsNull()
        {
            throw new System.NotImplementedException("Nested Filtering for MySQL is not yet implemented.");
        }

        [Ignore]
        [TestMethod]
        public void TestNestedFilterWithinNestedFilter()
        {
            throw new System.NotImplementedException("Nested Filtering for MySQL is not yet implemented.");
        }

        [Ignore]
        [TestMethod]
        public void TestNestedFilterWithAnd()
        {
            throw new System.NotImplementedException("Nested Filtering for MySQL is not yet implemented.");
        }

        [Ignore]
        [TestMethod]
        public void TestNestedFilterWithOr()
        {
            throw new System.NotImplementedException("Nested Filtering for MySQL is not yet implemented.");
        }

        [TestMethod]
        public async Task TestStringFiltersEqWithMappings()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('column1', `subq1`.`column1`, 'column2', `subq1`.`column2`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`__column1` AS `column1`,
                          `table0`.`__column2` AS `column2`
                   FROM `GQLmappings` AS `table0`
                   WHERE `table0`.`__column2` = 'Filtered Record'
                   ORDER BY `table0`.`__column1` asc
                   LIMIT 100) AS `subq1`";

            await TestStringFiltersEqWithMappings(mySqlQuery);
        }

        /// <summary>
        /// Gets the default schema for
        /// MySql.
        /// </summary>
        /// <returns></returns>
        protected override string GetDefaultSchema()
        {
            return DEFAULT_SCHEMA;
        }

        protected override string MakeQueryOn(string table, List<string> queriedColumns, string predicate, string schema, List<string> pkColumns)
        {
            if (pkColumns == null)
            {
                pkColumns = new() { "id" };
            }

            string orderBy = string.Join(", ", pkColumns.Select(c => $"`table0`.`{c}`"));

            return @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(" + string.Join(", ", queriedColumns.Select(c => $"\"{c}\", {c}")) + @")), JSON_ARRAY()) AS `data`
                FROM (
                    SELECT " + string.Join(", ", queriedColumns) + @"
                    FROM `" + table + @"` AS `table0`
                    WHERE " + predicate + @"
                    ORDER BY " + orderBy + @" asc LIMIT 100
                    ) AS `subq3`
            ";
        }
    }
}
