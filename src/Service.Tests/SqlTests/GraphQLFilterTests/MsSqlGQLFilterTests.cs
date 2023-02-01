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
        /// Test Nested Filter for Many-One relationship.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterManyOne()
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}[series] AS [table1]
                        WHERE [table1].[name] = 'Foundation'
                        AND [table0].[series_id] = [table1].[id] )";

            await TestNestedFilterManyOne(existsPredicate);
        }

        /// <summary>
        /// Test Nested Filter for One-Many relationship
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterOneMany()
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}[comics] AS [table1]
                        WHERE [table1].[title] = 'Cinderella'
                        AND [table1].[series_id] = [table0].[id] )";

            await TestNestedFilterOneMany(existsPredicate);
        }

        /// <summary>
        /// Test Nested Filter for Many-Many relationship
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterManyMany()
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}[authors] AS [table1]
                        INNER JOIN {GetPreIndentDefaultSchema()}[book_author_link] AS [table3]
                        ON [table3].[book_id] = [table0].[id]
                        WHERE [table1].[name] = 'Aaron'
                        AND [table3].[author_id] = [table1].[id])";

            await TestNestedFilterManyMany(existsPredicate);
        }

        /// <summary>
        /// Test a field of the nested filter is null.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterFieldIsNull()
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}[stocks_price] AS [table1]
                        WHERE [table1].[price] IS NULL
                        AND [table1].[categoryid] = [table0].[categoryid]
                        AND [table1].[pieceid] = [table0].[pieceid])";

            await TestNestedFilterFieldIsNull(existsPredicate);
        }

        /// <summary>
        /// Tests nested filter having another nested filter.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterWithinNestedFilter()
        {
            string defaultSchema = GetPreIndentDefaultSchema();

            // Table aliases and param names are created using the same
            // Counter hence the following intermixed naming:
            // [table0]:books
            // [table1]: authors
            // [table2]: books
            // [param3]: 'Awesome'
            // [table4]: book_author_link
            // [param5]: 'Aaron'
            // [table6]: book_author_link
            string existsPredicate = $@"
                EXISTS (SELECT 1 FROM {defaultSchema}[authors] AS [table1]
                        INNER JOIN {defaultSchema}[book_author_link] AS [table6]
                        ON [table6].[book_id] = [table0].[id]
                        WHERE (EXISTS (SELECT 1 FROM {defaultSchema}[books] AS [table2]
                                       INNER JOIN {defaultSchema}[book_author_link] AS [table4]
                                       ON [table4].[author_id] = [table1].[id]
                                       WHERE [table2].[title] LIKE 'Awesome'
                                       AND [table4].[book_id] = [table2].[id])
                                       AND [table1].[name] = 'Aaron') AND [table6].[author_id] = [table1].[id])";

            await TestNestedFilterWithinNestedFilter(existsPredicate);
        }

        /// <summary>
        /// Tests nested filter and an AND clause.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterWithAnd()
        {
            string defaultSchema = GetPreIndentDefaultSchema();

            string existsPredicate = $@"
                EXISTS (SELECT 1 FROM {defaultSchema}[authors] AS [table1]
                        INNER JOIN {defaultSchema}[book_author_link] AS [table3]
                        ON [table3].[book_id] = [table0].[id]
                        WHERE [table1].[name] = 'Aniruddh'
                        AND [table3].[author_id] = [table1].[id])
                        AND EXISTS (SELECT 1 FROM {defaultSchema}[publishers] AS [table4]
                                    WHERE [table4].[name] = 'Small Town Publisher'
                                    AND [table0].[publisher_id] = [table4].[id])";

            await TestNestedFilterWithAnd(existsPredicate);
        }

        /// <summary>
        /// Tests nested filter alongwith an OR clause.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterWithOr()
        {
            string defaultSchema = GetPreIndentDefaultSchema();

            string existsPredicate = $@"
                EXISTS( SELECT 1 FROM {defaultSchema}[publishers] AS [table1]
                    WHERE [table1].[name] = 'TBD Publishing One'
                    AND [table0].[publisher_id] = [table1].[id])
                OR EXISTS( SELECT 1 FROM {defaultSchema}[authors] AS [table3]
                           INNER JOIN {defaultSchema}[book_author_link] AS [table5]
                           ON [table5].[book_id] = [table0].[id]
                           WHERE [table3].[name] = 'Aniruddh'
                           AND [table5].[author_id] = [table3].[id])";

            await TestNestedFilterWithOr(existsPredicate);
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

        protected override string MakeQueryOn(
            string table,
            List<string> queriedColumns,
            string predicate,
            string schema,
            List<string> pkColumns = null)
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
