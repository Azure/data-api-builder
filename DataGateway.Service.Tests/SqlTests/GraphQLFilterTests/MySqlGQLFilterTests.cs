using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests.GraphQLFilterTests
{

    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlGQLFilterTests : GraphQLFilterTestBase
    {
        protected static string DEFAULT_SCHEMA = string.Empty;

        protected override string DatabaseEngine => TestCategory.MYSQL;

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
                    ORDER BY " + orderBy + @" LIMIT 100
                    ) AS `subq3`
            ";
        }
    }
}
