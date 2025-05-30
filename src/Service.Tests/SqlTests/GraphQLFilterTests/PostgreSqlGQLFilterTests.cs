// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

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
            await InitializeTestFixture();
        }

        /// <summary>
        /// Test Nested Filter for Many-One relationship.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterManyOne()
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}series AS table1
                        WHERE table1.name = 'Foundation'
                        AND table0.series_id = table1.id )";

            await TestNestedFilterManyOne(existsPredicate, roleName: "authenticated");
        }

        /// <summary>
        /// Test Nested Filter for One-Many relationship
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterOneMany()
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}comics AS table1
                        WHERE table1.title = 'Cinderella'
                        AND table1.series_id = table0.id )";

            await TestNestedFilterOneMany(existsPredicate, roleName: "authenticated");
        }

        /// <summary>
        /// Test Nested Filter for Many-Many relationship
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterManyMany()
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}authors AS table1
                        INNER JOIN {GetPreIndentDefaultSchema()}book_author_link AS table3
                        ON table3.book_id = table0.id
                        WHERE table1.name = 'Aaron'
                        AND table3.author_id = table1.id)";

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
                        FROM {GetPreIndentDefaultSchema()}stocks_price AS table1
                        WHERE table1.price IS NULL
                        AND table1.categoryid = table0.categoryid
                        AND table1.pieceid = table0.pieceid)";

            await TestNestedFilterFieldIsNull(existsPredicate, roleName: "authenticated");
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
            // [table0]: books
            // [table1]: authors
            // [table2]: books
            // [param3]: 'Awesome'
            // [table4]: book_author_link
            // [param5]: 'Aaron'
            // [table6]: book_author_link
            string existsPredicate = $@"
                EXISTS (SELECT 1 FROM {defaultSchema}authors AS table1
                        INNER JOIN {defaultSchema}book_author_link AS table6
                        ON table6.book_id = table0.id
                        WHERE (EXISTS (SELECT 1 FROM {defaultSchema}books AS table2
                                       INNER JOIN {defaultSchema}book_author_link AS table4
                                       ON table4.author_id = table1.id
                                       WHERE table2.title LIKE 'Awesome'
                                       AND table4.book_id = table2.id)
                                       AND table1.name = 'Aaron') AND table6.author_id = table1.id)";

            await TestNestedFilterWithinNestedFilter(existsPredicate, roleName: "authenticated");
        }

        /// <summary>
        /// Tests nested filter and an AND clause.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterWithAnd()
        {
            string defaultSchema = GetPreIndentDefaultSchema();

            string existsPredicate = $@"
                EXISTS (SELECT 1 FROM {defaultSchema}authors AS table1
                        INNER JOIN {defaultSchema}book_author_link AS table3
                        ON table3.book_id = table0.id
                        WHERE table1.name = 'Aniruddh'
                        AND table3.author_id = table1.id)
                        AND EXISTS (SELECT 1 FROM {defaultSchema}publishers AS table4
                                    WHERE table4.name = 'Small Town Publisher'
                                    AND table0.publisher_id = table4.id)";

            await TestNestedFilterWithAnd(existsPredicate, roleName: "authenticated");
        }

        /// <summary>
        /// Tests nested filter alongwith an OR clause.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterWithOr()
        {
            string defaultSchema = GetPreIndentDefaultSchema();

            string existsPredicate = $@"
                EXISTS( SELECT 1 FROM {defaultSchema}publishers AS table1
                    WHERE table1.name = 'TBD Publishing One'
                    AND table0.publisher_id = table1.id)
                OR EXISTS( SELECT 1 FROM {defaultSchema}authors AS table3
                           INNER JOIN {defaultSchema}book_author_link AS table5
                           ON table5.book_id = table0.id
                           WHERE table3.name = 'Aniruddh'
                           AND table5.author_id = table3.id)";

            await TestNestedFilterWithOr(existsPredicate, roleName: "authenticated");
        }

        /// <summary>
        /// Tests nested filter with an IN and OR clause.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterWithOrAndIN()
        {
            string defaultSchema = GetPreIndentDefaultSchema();

            string existsPredicate = $@"
                EXISTS( SELECT 1 FROM {defaultSchema}publishers AS table1
                    WHERE table1.name IN ('TBD Publishing One')
                    AND table0.publisher_id = table1.id)
                OR EXISTS( SELECT 1 FROM {defaultSchema}authors AS table3
                           INNER JOIN {defaultSchema}book_author_link AS table5
                           ON table5.book_id = table0.id
                           WHERE table3.name IN ('Aniruddh')
                           AND table5.author_id = table3.id)";

            await TestNestedFilterWithOrAndIN(existsPredicate, roleName: "authenticated");
        }

        [TestMethod]
        public async Task TestStringFiltersEqWithMappings()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT __column1 AS column1, __column2 AS column2 FROM GQLMappings WHERE __column2 = 'Filtered Record' ORDER BY __column1 asc LIMIT 100) as table0";

            await TestStringFiltersEqWithMappings(postgresQuery);
        }

        /// <summary>
        /// Test IN operator when mappings are configured for GraphQL entity.
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersINWithMappings()
        {
            string postgresQuery = $"SELECT json_agg(to_jsonb(table0)) FROM (SELECT __column1 AS column1, __column2 AS column2 FROM GQLMappings WHERE __column2 = 'Filtered Record' ORDER BY __column1 asc LIMIT 100) as table0";

            await TestStringFiltersINWithMappings(postgresQuery);
        }

        /// <summary>
        /// Tests various string filters with special characters in SQL queries.
        /// </summary>
        [DataTestMethod]
        [DataRow(
            "{ title: { endsWith: \"_CONN\" } }",
            "%\\_CONN",
            DisplayName = "EndsWith: '_CONN'"
        )]
        [DataRow(
            "{ title: { contains: \"%_\" } }",
            "%\\%\\_%",
            DisplayName = "Contains: '%_'"
        )]
        [DataRow(
            "{ title: { endsWith: \"%_CONN\" } }",
            "%\\%\\_CONN",
            DisplayName = "endsWith: '%CONN'"
        )]
        [DataRow(
            "{ title: { startsWith: \"CONN%\" } }",
            "CONN\\%%",
            DisplayName = "startsWith: 'CONN%'"
        )]
        [DataRow(
            "{ title: { startsWith: \"[\" } }",
            "\\[%",
            DisplayName = "startsWith: '['"
        )]
        [DataRow(
            "{ title: { endsWith: \"]\" } }",
            "%\\]",
            DisplayName = "endsWith: ']'"
        )]
        [DataRow(
            "{ title: { contains: \"\\\\\" } }",
            "%\\\\%",
            DisplayName = "Contains single backslash: '\\' "
        )]
        [DataRow(
            "{ title: { contains: \"\\\\\\\\\" } }",
            "%\\\\\\\\%",
            DisplayName = "Contains double backslash: '\\\\'"
        )]
        public new async Task TestStringFiltersWithSpecialCharacters(string dynamicFilter, string dbFilterInput)
        {
            string postgresQuery = @$"
                SELECT json_agg(to_jsonb(table0)) 
                FROM (
                    SELECT title 
                    FROM books 
                    WHERE title LIKE '{dbFilterInput}'
                    ORDER BY title ASC
                ) as table0";

            await base.TestStringFiltersWithSpecialCharacters(dynamicFilter, postgresQuery);
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
