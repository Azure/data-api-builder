// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
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
            await InitializeTestFixture();
        }

        /// <summary>
        /// Test Nested Filter for Many-One relationship.
        /// Also validates authorization failure behavior when the referenced nested entity
        /// or referenced nested entity column has permission restrictions for the given role.
        /// </summary>
        [DataTestMethod]
        [DataRow("Authenticated", false, "", DisplayName = "No nested filter AuthZ error")]
        [DataRow("TestNestedFilterManyOne_ColumnForbidden", true, DataApiBuilderException.GRAPHQL_FILTER_FIELD_AUTHZ_FAILURE, DisplayName = "Excluded column in nested filter entity, AuthZ failure")]
        [DataRow("TestNestedFilterManyOne_EntityReadForbidden", true, DataApiBuilderException.GRAPHQL_FILTER_ENTITY_AUTHZ_FAILURE, DisplayName = "Nested filter entity read access forbidden, AuthZ failure")]
        public async Task TestNestedFilterManyOne(string roleName, bool expectsError, string errorMessageFragment)
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}[series] AS [table1]
                        WHERE [table1].[name] = 'Foundation'
                        AND [table0].[series_id] = [table1].[id] )";

            await TestNestedFilterManyOne(existsPredicate, roleName, expectsError, errorMsgFragment: errorMessageFragment);
        }

        /// <summary>
        /// Test Nested Filter with IN operator for Many-One relationship.
        /// </summary>
        [DataTestMethod]
        [DataRow("Authenticated", false, "", DisplayName = "No nested filter AuthZ error")]
        public async Task TestNestedFilterWithInForManyOne(string roleName, bool expectsError, string errorMessageFragment)
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}[series] AS [table1]
                        WHERE [table1].[name] IN ('Foundation')
                        AND [table0].[series_id] = [table1].[id] )";

            string graphQLQueryName = "comics";
            // Gets all the comics that have their series name = 'Foundation'
            string gqlQuery = @"{
                comics (" + QueryBuilder.FILTER_FIELD_NAME + ": {" +
                    @"myseries: { name: { in: [""Foundation""] }}})
                    {
                      items {
                        id
                        title
                      }
                    }
                }";

            string dbQuery = MakeQueryOn(
                table: "comics",
                queriedColumns: new List<string> { "id", "title" },
                existsPredicate,
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                gqlQuery,
                graphQLQueryName,
                isAuthenticated: true,
                clientRoleHeader: roleName,
                expectsError: expectsError);

            if (expectsError)
            {
                SqlTestHelper.TestForErrorInGraphQLResponse(actual.ToString(), message: errorMessageFragment);
            }
            else
            {
                string expected = await GetDatabaseResultAsync(dbQuery);
                SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
            }
        }

        [TestMethod]
        public async Task TestStringFiltersEqWithMappings()
        {
            string msSqlQuery = @"
                SELECT [__column1] AS [column1], [__column2] AS [column2]
                FROM GQLMappings
                WHERE [__column2] = 'Filtered Record'
                ORDER BY [__column1] asc
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            await TestStringFiltersEqWithMappings(msSqlQuery);
        }

        /// <summary>
        /// Test IN operator when mappings are configured for GraphQL entity.
        /// </summary>
        [TestMethod]
        public async Task TestStringFiltersINWithMappings()
        {
            string msSqlQuery = @"
                SELECT [__column1] AS [column1], [__column2] AS [column2]
                FROM GQLMappings
                WHERE [__column2] IN ('Filtered Record')
                ORDER BY [__column1] asc
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            await TestStringFiltersINWithMappings(msSqlQuery);
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
            string msSqlQuery = @$"
                SELECT [title]
                FROM [dbo].[books]
                WHERE [title] LIKE '{dbFilterInput}' ESCAPE '\'
                ORDER BY [title] asc
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            await base.TestStringFiltersWithSpecialCharacters(dynamicFilter, msSqlQuery);
        }

        /// <summary>
        /// Test Nested Filter for One-Many relationship
        /// </summary>
        [DataTestMethod]
        [DataRow("Authenticated", false, "", DisplayName = "No nested filter AuthZ error")]
        [DataRow("TestNestedFilterOneMany_ColumnForbidden", true, DataApiBuilderException.GRAPHQL_FILTER_FIELD_AUTHZ_FAILURE, DisplayName = "Excluded column in nested filter entity, AuthZ failure")]
        [DataRow("TestNestedFilterOneMany_EntityReadForbidden", true, DataApiBuilderException.GRAPHQL_FILTER_ENTITY_AUTHZ_FAILURE, DisplayName = "Nested filter entity read access forbidden, AuthZ failure")]
        public async Task TestNestedFilterOneMany(string roleName, bool expectsError, string errorMessageFragment)
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}[comics] AS [table1]
                        WHERE [table1].[title] = 'Cinderella'
                        AND [table1].[series_id] = [table0].[id] )";

            await TestNestedFilterOneMany(existsPredicate, roleName, expectsError, errorMsgFragment: errorMessageFragment);
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
        [DataTestMethod]
        [DataRow("Authenticated", false, "", DisplayName = "No nested filter AuthZ error")]
        [DataRow("TestNestedFilterFieldIsNull_ColumnForbidden", true, DataApiBuilderException.GRAPHQL_FILTER_FIELD_AUTHZ_FAILURE, DisplayName = "Excluded column in nested filter entity, AuthZ failure")]
        [DataRow("TestNestedFilterFieldIsNull_EntityReadForbidden", true, DataApiBuilderException.GRAPHQL_FILTER_ENTITY_AUTHZ_FAILURE, DisplayName = "Nested filter entity read access forbidden, AuthZ failure")]
        public async Task TestNestedFilterFieldIsNull(string roleName, bool expectsError, string errorMessageFragment)
        {
            string existsPredicate = $@"
                EXISTS( SELECT 1
                        FROM {GetPreIndentDefaultSchema()}[stocks_price] AS [table1]
                        WHERE [table1].[price] IS NULL
                        AND [table1].[categoryid] = [table0].[categoryid]
                        AND [table1].[pieceid] = [table0].[pieceid])";

            await TestNestedFilterFieldIsNull(existsPredicate, roleName, expectsError, errorMsgFragment: errorMessageFragment);
        }

        /// <summary>
        /// Tests nested filter having another nested filter.
        /// </summary>
        [DataTestMethod]
        [DataRow("Authenticated", false, "", DisplayName = "No nested filter AuthZ error")]
        [DataRow("TestNestedFilter_ColumnForbidden", true, DataApiBuilderException.GRAPHQL_FILTER_FIELD_AUTHZ_FAILURE, DisplayName = "Excluded column in nested filter entity, AuthZ failure")]
        [DataRow("TestNestedFilter_EntityReadForbidden", true, DataApiBuilderException.GRAPHQL_FILTER_ENTITY_AUTHZ_FAILURE, DisplayName = "Nested filter entity read access forbidden, AuthZ failure")]
        public async Task TestNestedFilterWithinNestedFilter(string roleName, bool expectsError, string errorMessageFragment)
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

            await TestNestedFilterWithinNestedFilter(existsPredicate, roleName, expectsError, errorMsgFragment: errorMessageFragment);
        }

        /// <summary>
        /// Tests nested filter and an AND clause.
        /// and: [
        /// {entityOne: { fieldName: { eq: "Value" }}}
        /// {entityTwo: { fieldName: { eq: "Value" }}}
        /// ]
        /// - The TestNestedFilter_* roles demonstrate how permissions can be specifically applied to the first listed entity in the and operator, e.g. entityOne
        /// - The TestNestedFilterChained_* roles demonstrate how permissions can be specifically applied to the second listed entity in the and operator, e.g. entityTwo
        /// </summary>
        [DataTestMethod]
        [DataRow("Authenticated", false, "", DisplayName = "No nested filter AuthZ error")]
        [DataRow("TestNestedFilter_ColumnForbidden", true, DataApiBuilderException.GRAPHQL_FILTER_FIELD_AUTHZ_FAILURE, DisplayName = "Excluded column in nested filter entity, AuthZ failure")]
        [DataRow("TestNestedFilter_EntityReadForbidden", true, DataApiBuilderException.GRAPHQL_FILTER_ENTITY_AUTHZ_FAILURE, DisplayName = "Nested filter entity read access forbidden, AuthZ failure")]
        [DataRow("TestNestedFilterChained_ColumnForbidden", true, DataApiBuilderException.GRAPHQL_FILTER_FIELD_AUTHZ_FAILURE, DisplayName = "Excluded column in nested filter chained entity, AuthZ failure")]
        [DataRow("TestNestedFilterChained_EntityReadForbidden", true, DataApiBuilderException.GRAPHQL_FILTER_ENTITY_AUTHZ_FAILURE, DisplayName = "Nested filter chained entity read access forbidden, AuthZ failure")]
        public async Task TestNestedFilterWithAnd(string roleName, bool expectsError, string errorMessageFragment)
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

            await TestNestedFilterWithAnd(existsPredicate, roleName, expectsError, errorMsgFragment: errorMessageFragment);
        }

        /// <summary>
        /// Tests nested filter alongwith an OR clause.
        /// or: [
        /// {entityOne: { fieldName: { eq: "Value" }}}
        /// {entityTwo: { fieldName: { eq: "Value" }}}
        /// ]
        /// - The TestNestedFilter_* roles demonstrate how permissions can be specifically applied to the first listed entity in the or operator, e.g. entityOne
        /// - The TestNestedFilterChained_* roles demonstrate how permissions can be specifically applied to the second listed entity in the or operator, e.g. entityTwo
        /// </summary>
        [DataTestMethod]
        [DataRow("Authenticated", false, "", DisplayName = "No nested filter AuthZ error")]
        [DataRow("TestNestedFilter_ColumnForbidden", true, DataApiBuilderException.GRAPHQL_FILTER_FIELD_AUTHZ_FAILURE, DisplayName = "Excluded column in nested filter entity, AuthZ failure")]
        [DataRow("TestNestedFilter_EntityReadForbidden", true, DataApiBuilderException.GRAPHQL_FILTER_ENTITY_AUTHZ_FAILURE, DisplayName = "Nested filter entity read access forbidden, AuthZ failure")]
        [DataRow("TestNestedFilterChained_ColumnForbidden", true, DataApiBuilderException.GRAPHQL_FILTER_FIELD_AUTHZ_FAILURE, DisplayName = "Excluded column in nested filter chained entity, AuthZ failure")]
        [DataRow("TestNestedFilterChained_EntityReadForbidden", true, DataApiBuilderException.GRAPHQL_FILTER_ENTITY_AUTHZ_FAILURE, DisplayName = "Nested filter chained entity read access forbidden, AuthZ failure")]
        public async Task TestNestedFilterWithOr(string roleName, bool expectsError, string errorMessageFragment)
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

            await TestNestedFilterWithOr(existsPredicate, roleName, expectsError, errorMsgFragment: errorMessageFragment);
        }

        /// <summary>
        /// Tests nested filter with an IN and OR clause.
        /// </summary>
        [TestMethod]
        public async Task TestNestedFilterWithOrAndIN()
        {
            string defaultSchema = GetPreIndentDefaultSchema();

            string existsPredicate = $@"
                EXISTS( SELECT 1 FROM {defaultSchema}[publishers] AS [table1]
                    WHERE [table1].[name] IN ('TBD Publishing One')
                    AND [table0].[publisher_id] = [table1].[id])
                OR EXISTS( SELECT 1 FROM {defaultSchema}[authors] AS [table3]
                           INNER JOIN {defaultSchema}[book_author_link] AS [table5]
                           ON [table5].[book_id] = [table0].[id]
                           WHERE [table3].[name] IN ('Aniruddh')
                           AND [table5].[author_id] = [table3].[id])";

            await TestNestedFilterWithOrAndIN(existsPredicate, roleName: "authenticated");
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
