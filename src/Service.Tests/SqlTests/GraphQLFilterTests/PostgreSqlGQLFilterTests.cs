using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLFilterTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGQLFilterTests : GraphQLFilterTestBase
    {
        protected static string DEFAULT_SCHEMA = "public";

        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            await InitializeTestFixture(context);
        }

        /// <summary>
        /// Gets the default schema for
        /// PostgreSql.
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

            string schemaAndTable = $"{schema}.{table}";
            string orderBy = string.Join(", ", pkColumns.Select(c => $"\"table0\".\"{c}\""));

            return @"
                SELECT COALESCE(jsonb_agg(to_jsonb(subq3)), '[]') AS DATA
                FROM
                  (SELECT " + string.Join(", ", queriedColumns.Select(c => $"\"{c}\"")) + @"
                   FROM " + schemaAndTable + @" AS table0
                   WHERE " + predicate + @"
                   ORDER BY " + orderBy + @" asc
                   LIMIT 100) AS subq3
            ";
        }
    }
}
