// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.GraphQLBuilder.Queries;
using Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLSupportedTypesTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedTypes;

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
        /// Test to validate that queries involing filters on datetime types that are specific to MsSql work fine.
        /// </summary>
        /// <param name="fieldType">Type of datetime field.</param>
        /// <param name="fieldValue">Value of the field.</param>
        /// <param name="filterOperator">Filter operator.</param>
        /// <param name="filterOperatorName">Name of filter operator.</param>
        [DataTestMethod]
        [DataRow(DATE_TYPE, "1999-01-08", "=", "eq", DisplayName = "date type filter test with eq operator")]
        [DataRow(DATE_TYPE, "1999-01-08", ">=", "gte", DisplayName = "date type filter test with gte operator")]
        [DataRow(DATE_TYPE, "9998-12-31", "!=", "neq", DisplayName = "date type filter test with ne operator")]
        [DataRow(SMALLDATETIME_TYPE, "1999-01-08 10:24:00", "=", "eq", DisplayName = "smalldatetime type filter test with eq operator")]
        [DataRow(SMALLDATETIME_TYPE, "1999-01-08 10:24:00", ">=", "gte", DisplayName = "smalldatetime type filter test with gte operator")]
        [DataRow(SMALLDATETIME_TYPE, "1999-01-08 10:24:00", "!=", "neq", DisplayName = "smalldatetime type filter test with neq operator")]
        [DataRow(DATETIME2_TYPE, "1999-01-08 10:23:00.9999999", "=", "eq", DisplayName = "datetime2 type filter test with eq operator")]
        [DataRow(DATETIME2_TYPE, "1999-01-08 10:23:00.9999999", ">=", "gte", DisplayName = "datetime2 type filter test with gte operator")]
        [DataRow(DATETIME2_TYPE, "1999-01-08 10:23:00.9999999", "!=", "neq", DisplayName = "datetime2 type filter test with neq operator")]
        public async Task TestDateTimeFilters(
            string fieldType,
            string fieldValue,
            string filterOperator,
            string filterOperatorName
            )
        {
            string graphQLQueryName = "supportedTypes";
            string fieldName = $"{fieldType.ToLower()}_types";
            string gqlQuery = @"{
                supportedTypes( " + QueryBuilder.FILTER_FIELD_NAME + " : { " + $"{fieldName}" + @": {" + $"{filterOperatorName}" + @": """ + $"{fieldValue}" + @"""}})
                {
                    items { " +
                        $"{fieldName}" +
                        @"
                    }
                }
            }";

            string dbQuery = MakeQueryOn(
                "type_table",
                new List<string> { fieldName },
                $"{fieldName} {filterOperator} '{fieldValue}'",
                GetDefaultSchema());

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);
            GraphQLSupportedTypesTestBase.PerformTestEqualsForExtendedTypes(fieldType, expected, actual.ToString());
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
