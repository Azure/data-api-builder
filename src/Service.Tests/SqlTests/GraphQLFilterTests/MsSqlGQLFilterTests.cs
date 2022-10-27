using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLFilterTests
{

    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGQLFilterTests : GraphQLFilterTestBase
    {
        protected static string DEFAULT_SCHEMA = "dbo";

        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task Setup(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture(context);
        }

        /// <summary>
        /// Gets the default schema for
        /// MsSql.
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

            string schemaAndTable = $"[{schema}].[{table}]";
            string orderBy = string.Join(", ", pkColumns.Select(c => $"[table0].[{c}]"));

            return @"
                SELECT TOP 100 " + string.Join(", ", queriedColumns) + @"
                FROM " + schemaAndTable + @" AS [table0]
                WHERE " + predicate + @"
                ORDER BY " + orderBy + @" asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES
            ";
        }
    }
}
