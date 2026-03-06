// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedDateTimeTypes;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedHotChocolateTypes;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLSupportedTypesTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGQLSupportedTypesTests : GraphQLSupportedTypesTestBase
    {
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
        /// Postgres Single Type Test.
        /// Postgres requires conversion of a float value, ex: 0.33 to 'real' type otherwise precision is lost.
        /// </summary>
        /// <param name="type">GraphQL Type</param>
        /// <param name="filterOperator">Comparison operator: gt, lt, gte, lte, etc.</param>
        /// <param name="sqlValue">Value to be set in "expected value" sql query.</param>
        /// <param name="gqlValue">GraphQL input value supplied.</param>
        /// <param name="queryOperator">Query operator for "expected value" sql query.</param>
        [DataRow(SINGLE_TYPE, "gt", "real '-9.3'", "-9.3", ">")]
        [DataRow(SINGLE_TYPE, "gte", "real '-9.2'", "-9.2", ">=")]
        [DataRow(SINGLE_TYPE, "lt", "real '.33'", "0.33", "<")]
        [DataRow(SINGLE_TYPE, "lte", "real '.33'", "0.33", "<=")]
        [DataRow(SINGLE_TYPE, "neq", "real '9.2'", "9.2", "!=")]
        [DataRow(SINGLE_TYPE, "eq", "real '0.33'", "0.33", "=")]
        [DataTestMethod]
        public async Task PG_real_graphql_single_filter_expectedValues(
            string type,
            string filterOperator,
            string sqlValue,
            string gqlValue,
            string queryOperator)
        {
            await QueryTypeColumnFilterAndOrderBy(type, filterOperator, sqlValue, gqlValue, queryOperator);
        }

        /// <summary>
        /// PostgreSQL filter Type with IN operator Tests.
        /// </summary>
        /// <param name="type">GraphQL Type</param>
        /// <param name="filterOperator">Comparison operator: IN</param>
        /// <param name="sqlValue">Value to be set in "expected value" sql query.</param>
        /// <param name="gqlValue">GraphQL input value supplied.</param>
        /// <param name="queryOperator">Query operator for "expected value" sql query.</param>
        [DataRow(SHORT_TYPE, "-1", "-1")]
        [DataRow(INT_TYPE, "-1", "-1")]
        [DataRow(LONG_TYPE, "-1", "-1")]
        [DataRow(FLOAT_TYPE, "-9.2", "-9.2")]
        [DataRow(DECIMAL_TYPE, "-9.292929", "-9.292929")]
        [DataRow(UUID_TYPE, "'D1D021A8-47B4-4AE4-B718-98E89C41A161'", "\"D1D021A8-47B4-4AE4-B718-98E89C41A161\"")]
        [DataRow(BOOLEAN_TYPE, "'false'", "false")]
        [DataRow(STRING_TYPE, "lksa;jdflasdf;alsdflksdfkldj", "\"lksa;jdflasdf;alsdflksdfkldj\"")]
        [DataTestMethod]
        public async Task PGSQL_real_graphql_in_filter_expectedValues(
            string type,
            string sqlValue,
            string gqlValue)
        {
            if (type == STRING_TYPE)
            {
                sqlValue = $"('{sqlValue}')";
                gqlValue = $"[{gqlValue}]";
            }
            else
            {
                sqlValue = $"({sqlValue})";
                gqlValue = $"[{gqlValue}]";
            }

            await QueryTypeColumnFilterAndOrderBy(type, "in", sqlValue, gqlValue, "IN");
        }

        protected override string MakeQueryOnTypeTable(List<DabField> queryFields, int id)
        {
            return MakeQueryOnTypeTable(queryFields, filterValue: id.ToString(), filterField: "id");
        }

        protected override string MakeQueryOnTypeTable(
            List<DabField> queryFields,
            string filterValue = "1",
            string filterOperator = "=",
            string filterField = "1",
            string orderBy = "id",
            string limit = "1")
        {
            string formattedSelect = limit.Equals("1") ? "SELECT to_jsonb(subq3) AS DATA" : "SELECT json_agg(to_jsonb(subq3)) AS DATA";

            return @"
                " + formattedSelect + @"
                FROM
                  (SELECT " + string.Join(", ", queryFields.Select(field => ProperlyFormatTypeTableColumn(field.BackingColumnName) + $" AS {field.Alias}")) + @"
                   FROM public.type_table AS table0
                   WHERE " + filterField + " " + filterOperator + " " + filterValue + @"
                   ORDER BY " + orderBy + @" asc
                   LIMIT " + limit + @") AS subq3
            ";
        }

        protected override bool IsSupportedType(string type)
        {
            return type switch
            {
                BYTE_TYPE => false,
                SMALLDATETIME_TYPE => false,
                DATETIME2_TYPE => false,
                DATETIMEOFFSET_TYPE => false,
                TIME_TYPE => false,
                LOCALTIME_TYPE => false,
                _ => true
            };
        }

        /// <summary>
        /// Appends parsing logic to some columns which need it
        /// </summary>
        private static string ProperlyFormatTypeTableColumn(string columnName)
        {
            if (columnName.Contains(BYTEARRAY_TYPE.ToLowerInvariant()))
            {
                return $"encode({columnName}, 'base64')";
            }
            else
            {
                return columnName;
            }
        }

        /// <summary>
        /// Tests DATE type filtering with various comparison operators.
        /// This validates the fix for issue #3094 where DATE filtering failed with
        /// "operator does not exist: date >= text" error.
        /// The fix ensures DbType.Date is set on parameters to prevent ::text casting.
        /// </summary>
        [DataTestMethod]
        [DataRow(DATE_TYPE, "eq", "'1999-01-08'", "\"1999-01-08\"", "=",
            DisplayName = "DATE filter with eq operator")]
        [DataRow(DATE_TYPE, "gt", "'1753-01-01'", "\"1753-01-01\"", ">",
            DisplayName = "DATE filter with gt operator")]
        [DataRow(DATE_TYPE, "gte", "'1999-01-08'", "\"1999-01-08\"", ">=",
            DisplayName = "DATE filter with gte operator")]
        [DataRow(DATE_TYPE, "lt", "'9999-12-31'", "\"9999-12-31\"", "<",
            DisplayName = "DATE filter with lt operator")]
        [DataRow(DATE_TYPE, "lte", "'2000-06-15'", "\"2000-06-15\"", "<=",
            DisplayName = "DATE filter with lte operator")]
        [DataRow(DATE_TYPE, "neq", "'1753-01-01'", "\"1753-01-01\"", "!=",
            DisplayName = "DATE filter with neq operator")]
        public async Task PostgreSQL_DateTypeFilterAndOrderBy(
            string type,
            string filterOperator,
            string sqlValue,
            string gqlValue,
            string queryOperator)
        {
            await QueryTypeColumnFilterAndOrderBy(type, filterOperator, sqlValue, gqlValue, queryOperator);
        }

        /// <summary>
        /// Tests TIMESTAMP type filtering with various comparison operators.
        /// This validates the fix for issue #3094 where TIMESTAMP filtering failed with
        /// "operator does not exist: timestamp >= text" error.
        /// The fix ensures DbType.DateTime is set on parameters to prevent ::text casting.
        /// </summary>
        [DataTestMethod]
        [DataRow(DATETIME_TYPE, "eq", "'1999-01-08 10:23:54'", "\"1999-01-08 10:23:54\"", "=",
            DisplayName = "TIMESTAMP filter with eq operator")]
        [DataRow(DATETIME_TYPE, "gt", "'1753-01-01 00:00:00'", "\"1753-01-01 00:00:00\"", ">",
            DisplayName = "TIMESTAMP filter with gt operator")]
        [DataRow(DATETIME_TYPE, "gte", "'1999-01-08 10:23:00'", "\"1999-01-08 10:23:00\"", ">=",
            DisplayName = "TIMESTAMP filter with gte operator")]
        [DataRow(DATETIME_TYPE, "lt", "'9999-12-31 23:59:59'", "\"9999-12-31 23:59:59\"", "<",
            DisplayName = "TIMESTAMP filter with lt operator")]
        [DataRow(DATETIME_TYPE, "lte", "'1999-01-08 10:23:54'", "\"1999-01-08 10:23:54\"", "<=",
            DisplayName = "TIMESTAMP filter with lte operator")]
        [DataRow(DATETIME_TYPE, "neq", "'1753-01-01 00:00:00'", "\"1753-01-01 00:00:00\"", "!=",
            DisplayName = "TIMESTAMP filter with neq operator")]
        public async Task PostgreSQL_TimestampTypeFilterAndOrderBy(
            string type,
            string filterOperator,
            string sqlValue,
            string gqlValue,
            string queryOperator)
        {
            await QueryTypeColumnFilterAndOrderBy(type, filterOperator, sqlValue, gqlValue, queryOperator);
        }

        /// <summary>
        /// Tests the specific scenario from issue #3094:
        /// GraphQL query with gte filter on a date field should work without
        /// "operator does not exist: date >= text" error.
        /// 
        /// Example query that should work:
        /// query {
        ///   supportedTypes(filter: { date_types: { gte: "2024-01-01" } }) {
        ///     items { date_types }
        ///   }
        /// }
        /// </summary>
        [TestMethod]
        public async Task PostgreSQL_DateGteFilter_NoTextCastingError()
        {
            if (!IsSupportedType(DATE_TYPE))
            {
                Assert.Inconclusive("DATE type not supported");
            }

            string field = "date_types";
            string graphQLQueryName = "supportedTypes";
            string gqlQuery = @"{
                supportedTypes(first: 100 orderBy: { typeid: ASC } filter: { " + field + @": { gte: ""1999-01-08"" } }) {
                    items {
                        typeid, " + field + @"
                    }
                }
            }";

            // Execute the query - this should not throw "operator does not exist: date >= text"
            System.Text.Json.JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);

            // Verify we got results (not an error)
            Assert.IsTrue(actual.TryGetProperty("items", out System.Text.Json.JsonElement items), "Expected 'items' property in response");
            Assert.IsTrue(items.GetArrayLength() > 0, "Expected at least one result for gte filter on date");
        }

        /// <summary>
        /// Tests the specific scenario from issue #3094:
        /// GraphQL query with gte filter on a timestamp field should work without
        /// "operator does not exist: timestamp >= text" error.
        /// </summary>
        [TestMethod]
        public async Task PostgreSQL_TimestampGteFilter_NoTextCastingError()
        {
            if (!IsSupportedType(DATETIME_TYPE))
            {
                Assert.Inconclusive("DATETIME type not supported");
            }

            string field = "datetime_types";
            string graphQLQueryName = "supportedTypes";
            string gqlQuery = @"{
                supportedTypes(first: 100 orderBy: { typeid: ASC } filter: { " + field + @": { gte: ""1999-01-08T00:00:00.000Z"" } }) {
                    items {
                        typeid, " + field + @"
                    }
                }
            }";

            // Execute the query - this should not throw "operator does not exist: timestamp >= text"
            System.Text.Json.JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);

            // Verify we got results (not an error)
            Assert.IsTrue(actual.TryGetProperty("items", out System.Text.Json.JsonElement items), "Expected 'items' property in response");
            Assert.IsTrue(items.GetArrayLength() > 0, "Expected at least one result for gte filter on timestamp");
        }

        /// <summary>
        /// Tests that other type conversions (int, string, boolean, etc.) are not broken
        /// by the date/time DbType fix.
        /// </summary>
        [DataTestMethod]
        [DataRow(INT_TYPE, "eq", "1", "1", "=", DisplayName = "Int filter still works")]
        [DataRow(STRING_TYPE, "eq", "'lksa;jdflasdf;alsdflksdfkldj'", "\"lksa;jdflasdf;alsdflksdfkldj\"", "=", DisplayName = "String filter still works")]
        [DataRow(BOOLEAN_TYPE, "eq", "'true'", "true", "=", DisplayName = "Boolean filter still works")]
        [DataRow(DECIMAL_TYPE, "eq", "0.333333", "0.333333", "=", DisplayName = "Decimal filter still works")]
        public async Task PostgreSQL_OtherTypeFilters_NotBroken(
            string type,
            string filterOperator,
            string sqlValue,
            string gqlValue,
            string queryOperator)
        {
            await QueryTypeColumnFilterAndOrderBy(type, filterOperator, sqlValue, gqlValue, queryOperator);
        }

        /// <summary>
        /// Bypass DateTime GQL tests for PostreSql
        /// </summary>
        [DataTestMethod]
        [Ignore]
        public new void QueryTypeColumnFilterAndOrderByDateTime(string type, string filterOperator, string sqlValue, string gqlValue, string queryOperator)
        {
            Assert.Inconclusive("Test skipped for PostgreSql.");
        }
    }
}
