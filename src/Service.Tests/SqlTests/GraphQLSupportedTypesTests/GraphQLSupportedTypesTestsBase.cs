// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedDateTimeTypes;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedHotChocolateTypes;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLSupportedTypesTests
{

    [TestClass]
    public abstract class GraphQLSupportedTypesTestBase : SqlTestBase
    {
        #region Tests

        [DataTestMethod]
        [DataRow(BYTE_TYPE, 1, DisplayName = "Query by PK test selecting only byte_types with typeid = 1.")]
        [DataRow(BYTE_TYPE, 2, DisplayName = "Query by PK test selecting only byte_types with typeid = 2.")]
        [DataRow(BYTE_TYPE, 3, DisplayName = "Query by PK test selecting only byte_types with typeid = 3.")]
        [DataRow(BYTE_TYPE, 4, DisplayName = "Query by PK test selecting only byte_types with typeid = 4.")]
        [DataRow(SHORT_TYPE, 1, DisplayName = "Query by PK test selecting only short_types with typeid = 1.")]
        [DataRow(SHORT_TYPE, 2, DisplayName = "Query by PK test selecting only short_types with typeid = 2.")]
        [DataRow(SHORT_TYPE, 3, DisplayName = "Query by PK test selecting only short_types with typeid = 3.")]
        [DataRow(SHORT_TYPE, 4, DisplayName = "Query by PK test selecting only short_types with typeid = 4.")]
        [DataRow(INT_TYPE, 1, DisplayName = "Query by PK test selecting only int_types with typeid = 1.")]
        [DataRow(INT_TYPE, 2, DisplayName = "Query by PK test selecting only int_types with typeid = 2.")]
        [DataRow(INT_TYPE, 3, DisplayName = "Query by PK test selecting only int_types with typeid = 3.")]
        [DataRow(INT_TYPE, 4, DisplayName = "Query by PK test selecting only int_types with typeid = 4.")]
        [DataRow(LONG_TYPE, 1, DisplayName = "Query by PK test selecting only long_types with typeid = 1.")]
        [DataRow(LONG_TYPE, 2, DisplayName = "Query by PK test selecting only long_types with typeid = 2.")]
        [DataRow(LONG_TYPE, 3, DisplayName = "Query by PK test selecting only long_types with typeid = 3.")]
        [DataRow(LONG_TYPE, 4, DisplayName = "Query by PK test selecting only long_types with typeid = 4.")]
        [DataRow(SINGLE_TYPE, 1, DisplayName = "Query by PK test selecting only single_types with typeid = 1.")]
        [DataRow(SINGLE_TYPE, 2, DisplayName = "Query by PK test selecting only single_types with typeid = 2.")]
        [DataRow(SINGLE_TYPE, 3, DisplayName = "Query by PK test selecting only single_types with typeid = 3.")]
        [DataRow(SINGLE_TYPE, 4, DisplayName = "Query by PK test selecting only single_types with typeid = 4.")]
        [DataRow(FLOAT_TYPE, 1, DisplayName = "Query by PK test selecting only float_types with typeid = 1.")]
        [DataRow(FLOAT_TYPE, 2, DisplayName = "Query by PK test selecting only float_types with typeid = 2.")]
        [DataRow(FLOAT_TYPE, 3, DisplayName = "Query by PK test selecting only float_types with typeid = 3.")]
        [DataRow(FLOAT_TYPE, 4, DisplayName = "Query by PK test selecting only float_types with typeid = 4.")]
        [DataRow(DECIMAL_TYPE, 1, DisplayName = "Query by PK test selecting only decimal_types with typeid = 1.")]
        [DataRow(DECIMAL_TYPE, 2, DisplayName = "Query by PK test selecting only decimal_types with typeid = 2.")]
        [DataRow(DECIMAL_TYPE, 3, DisplayName = "Query by PK test selecting only decimal_types with typeid = 3.")]
        [DataRow(DECIMAL_TYPE, 4, DisplayName = "Query by PK test selecting only decimal_types with typeid = 4.")]
        [DataRow(STRING_TYPE, 1, DisplayName = "Query by PK test selecting only string_types with typeid = 1.")]
        [DataRow(STRING_TYPE, 2, DisplayName = "Query by PK test selecting only string_types with typeid = 2.")]
        [DataRow(STRING_TYPE, 3, DisplayName = "Query by PK test selecting only string_types with typeid = 3.")]
        [DataRow(STRING_TYPE, 4, DisplayName = "Query by PK test selecting only string_types with typeid = 4.")]
        [DataRow(BOOLEAN_TYPE, 1, DisplayName = "Query by PK test selecting only boolean_types with typeid = 1.")]
        [DataRow(BOOLEAN_TYPE, 2, DisplayName = "Query by PK test selecting only boolean_types with typeid = 2.")]
        [DataRow(BOOLEAN_TYPE, 3, DisplayName = "Query by PK test selecting only boolean_types with typeid = 3.")]
        [DataRow(BOOLEAN_TYPE, 4, DisplayName = "Query by PK test selecting only boolean_types with typeid = 4.")]
        [DataRow(DATETIME_TYPE, 1, DisplayName = "Query by PK test selecting only datetime_types with typeid = 1.")]
        [DataRow(DATETIME_TYPE, 2, DisplayName = "Query by PK test selecting only datetime_types with typeid = 2.")]
        [DataRow(DATETIME_TYPE, 3, DisplayName = "Query by PK test selecting only datetime_types with typeid = 3.")]
        [DataRow(DATETIME_TYPE, 4, DisplayName = "Query by PK test selecting only datetime_types with typeid = 4.")]
        [DataRow(DATETIME2_TYPE, 1, DisplayName = "Query by PK test selecting only datetime2_types with typeid = 1.")]
        [DataRow(DATETIME2_TYPE, 2, DisplayName = "Query by PK test selecting only datetime2_types with typeid = 2.")]
        [DataRow(DATETIME2_TYPE, 3, DisplayName = "Query by PK test selecting only datetime2_types with typeid = 3.")]
        [DataRow(DATETIME2_TYPE, 4, DisplayName = "Query by PK test selecting only datetime2_types with typeid = 4.")]
        [DataRow(DATETIMEOFFSET_TYPE, 1, DisplayName = "Query by PK test selecting only datetimeoffset_types with typeid = 1.")]
        [DataRow(DATETIMEOFFSET_TYPE, 2, DisplayName = "Query by PK test selecting only datetimeoffset_types with typeid = 2.")]
        [DataRow(DATETIMEOFFSET_TYPE, 3, DisplayName = "Query by PK test selecting only datetimeoffset_types with typeid = 3.")]
        [DataRow(DATETIMEOFFSET_TYPE, 4, DisplayName = "Query by PK test selecting only datetimeoffset_types with typeid = 4.")]
        [DataRow(SMALLDATETIME_TYPE, 1, DisplayName = "Query by PK test selecting only smalldatetime_types with typeid = 1.")]
        [DataRow(SMALLDATETIME_TYPE, 2, DisplayName = "Query by PK test selecting only smalldatetime_types with typeid = 2.")]
        [DataRow(SMALLDATETIME_TYPE, 3, DisplayName = "Query by PK test selecting only smalldatetime_types with typeid = 3.")]
        [DataRow(SMALLDATETIME_TYPE, 4, DisplayName = "Query by PK test selecting only smalldatetime_types with typeid = 4.")]
        [DataRow(DATE_TYPE, 1, DisplayName = "Query by PK test selecting only date_types with typeid = 1.")]
        [DataRow(DATE_TYPE, 2, DisplayName = "Query by PK test selecting only date_types with typeid = 2.")]
        [DataRow(DATE_TYPE, 3, DisplayName = "Query by PK test selecting only date_types with typeid = 3.")]
        [DataRow(DATE_TYPE, 4, DisplayName = "Query by PK test selecting only date_types with typeid = 4.")]
        [DataRow(TIME_TYPE, 1, DisplayName = "Query by PK test selecting only time_types with typeid = 1.")]
        [DataRow(TIME_TYPE, 2, DisplayName = "Query by PK test selecting only time_types with typeid = 2.")]
        [DataRow(TIME_TYPE, 3, DisplayName = "Query by PK test selecting only time_types with typeid = 3.")]
        [DataRow(TIME_TYPE, 4, DisplayName = "Query by PK test selecting only time_types with typeid = 4.")]
        [DataRow(BYTEARRAY_TYPE, 1, DisplayName = "Query by PK test selecting only bytearray_types with typeid = 1.")]
        [DataRow(BYTEARRAY_TYPE, 2, DisplayName = "Query by PK test selecting only bytearray_types with typeid = 2.")]
        [DataRow(BYTEARRAY_TYPE, 3, DisplayName = "Query by PK test selecting only bytearray_types with typeid = 3.")]
        [DataRow(BYTEARRAY_TYPE, 4, DisplayName = "Query by PK test selecting only bytearray_types with typeid = 4.")]
        [DataRow(UUID_TYPE, 1, DisplayName = "Query by PK test selecting only uuid_types with typeid = 1.")]
        [DataRow(UUID_TYPE, 2, DisplayName = "Query by PK test selecting only uuid_types with typeid = 2.")]
        [DataRow(UUID_TYPE, 3, DisplayName = "Query by PK test selecting only uuid_types with typeid = 3.")]
        [DataRow(UUID_TYPE, 4, DisplayName = "Query by PK test selecting only uuid_types with typeid = 4.")]
        public async Task QueryTypeColumn(string type, int id)
        {
            if (!IsSupportedType(type))
            {
                Assert.Inconclusive("Type not supported");
            }

            string field = $"{type.ToLowerInvariant()}_types";
            string graphQLQueryName = "supportedType_by_pk";
            string gqlQuery = "{ supportedType_by_pk(typeid: " + id + ") { " + field + " } }";

            string dbQuery = MakeQueryOnTypeTable(new List<string> { field }, id);

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            PerformTestEqualsForExtendedTypes(type, expected, actual.ToString());
        }

        [DataTestMethod]
        [DataRow(BYTE_TYPE, "gt", "0", "0", ">")]
        [DataRow(BYTE_TYPE, "gte", "0", "0", ">=")]
        [DataRow(BYTE_TYPE, "lt", "1", "1", "<")]
        [DataRow(BYTE_TYPE, "lte", "1", "1", "<=")]
        [DataRow(BYTE_TYPE, "neq", "0", "0", "!=")]
        [DataRow(BYTE_TYPE, "eq", "1", "1", "=")]
        [DataRow(SHORT_TYPE, "gt", "-1", "-1", ">")]
        [DataRow(SHORT_TYPE, "gte", "-1", "-1", ">=")]
        [DataRow(SHORT_TYPE, "lt", "1", "1", "<")]
        [DataRow(SHORT_TYPE, "lte", "1", "1", "<=")]
        [DataRow(SHORT_TYPE, "neq", "1", "1", "!=")]
        [DataRow(SHORT_TYPE, "eq", "-1", "-1", "=")]
        [DataRow(INT_TYPE, "gt", "-1", "-1", ">")]
        [DataRow(INT_TYPE, "gte", "2147483647", "2147483647", " >= ")]
        [DataRow(INT_TYPE, "lt", "1", "1", "<")]
        [DataRow(INT_TYPE, "lte", "-2147483648", "-2147483648", " <= ")]
        [DataRow(INT_TYPE, "neq", "1", "1", "!=")]
        [DataRow(INT_TYPE, "eq", "-1", "-1", "=")]
        [DataRow(LONG_TYPE, "gt", "-1", "-1", ">")]
        [DataRow(LONG_TYPE, "gte", "9223372036854775807", "9223372036854775807", " >= ")]
        [DataRow(LONG_TYPE, "lt", "1", "1", "<")]
        [DataRow(LONG_TYPE, "lte", "-9223372036854775808", "-9223372036854775808", " <= ")]
        [DataRow(LONG_TYPE, "neq", "1", "1", "!=")]
        [DataRow(LONG_TYPE, "eq", "-1", "-1", "=")]
        [DataRow(STRING_TYPE, "neq", "'foo'", "\"foo\"", "!=")]
        [DataRow(STRING_TYPE, "eq", "'lksa;jdflasdf;alsdflksdfkldj'", "\"lksa;jdflasdf;alsdflksdfkldj\"", "=")]
        [DataRow(SINGLE_TYPE, "gt", "-9.3", "-9.3", ">")]
        [DataRow(SINGLE_TYPE, "gte", "-9.2", "-9.2", ">=")]
        [DataRow(SINGLE_TYPE, "lt", ".33", "0.33", "<")]
        [DataRow(SINGLE_TYPE, "lte", ".33", "0.33", "<=")]
        [DataRow(SINGLE_TYPE, "neq", "9.2", "9.2", "!=")]
        [DataRow(SINGLE_TYPE, "eq", "'0.33'", "0.33", "=")]
        [DataRow(FLOAT_TYPE, "gt", "-9.2", "-9.2", ">")]
        [DataRow(FLOAT_TYPE, "gte", "-9.2", "-9.2", ">=")]
        [DataRow(FLOAT_TYPE, "lt", ".33", "0.33", "<")]
        [DataRow(FLOAT_TYPE, "lte", ".33", "0.33", "<=")]
        [DataRow(FLOAT_TYPE, "neq", "-9.2", "-9.2", "!=")]
        [DataRow(FLOAT_TYPE, "eq", "-9.2", "-9.2", "=")]
        [DataRow(DECIMAL_TYPE, "gt", "-9.292929", "-9.292929", " > ")]
        [DataRow(DECIMAL_TYPE, "gte", "-9.292929", "-9.292929", " >= ")]
        [DataRow(DECIMAL_TYPE, "lt", "0.333333", "0.333333", "<")]
        [DataRow(DECIMAL_TYPE, "lte", "0.333333", "0.333333", " <= ")]
        [DataRow(DECIMAL_TYPE, "neq", "0.0", "0.0", "!=")]
        [DataRow(DECIMAL_TYPE, "eq", "-9.292929", "-9.292929", "=")]
        [DataRow(UUID_TYPE, "eq", "'D1D021A8-47B4-4AE4-B718-98E89C41A161'", "\"D1D021A8-47B4-4AE4-B718-98E89C41A161\"", "=")]
        [DataRow(UUID_TYPE, "neq", "'D1D021A8-47B4-4AE4-B718-98E89C41A161'", "\"D1D021A8-47B4-4AE4-B718-98E89C41A161\"", "!=")]
        [DataRow(BOOLEAN_TYPE, "neq", "'false'", "false", "!=")]
        [DataRow(BOOLEAN_TYPE, "eq", "'false'", "false", "=")]
        public async Task QueryTypeColumnFilterAndOrderBy(string type, string filterOperator, string sqlValue, string gqlValue, string queryOperator)
        {
            if (!IsSupportedType(type))
            {
                Assert.Inconclusive("Type not supported");
            }

            string field = $"{type.ToLowerInvariant()}_types";
            string graphQLQueryName = "supportedTypes";
            string gqlQuery = @"{
                supportedTypes(first: 100 orderBy: { " + field + ": ASC } filter: { " + field + ": {" + filterOperator + ": " + gqlValue + @"} }) {
                    items {
                        " + field + @"
                    }
                }
            }";

            string dbQuery = MakeQueryOnTypeTable(new List<string> { field }, filterValue: sqlValue, filterOperator: queryOperator, filterField: field, orderBy: field, limit: "100");

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(dbQuery);

            PerformTestEqualsForExtendedTypes(type, expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Separate test case for DateTime to allow overwrite for postgreSql.
        /// The method constructs a GraphQL query to filter and order the datetime column based on the given parameters.
        /// The test checks various datetime data types such as datetime, datetimeoffset, and time.
        /// </summary>
        [DataTestMethod]
        [DataRow(DATETIME_TYPE, "eq", "'1999-01-08'", "\"1999-01-08\"", " = ",
            DisplayName = "datetime type filter and orderby test with eq operator and specific value '1999-01-08'.")]
        [DataRow(DATETIME_TYPE, "lt", "'1753-01-01'", "\"1753-01-01\"", " < ",
            DisplayName = "datetime type filter and orderby test with lt operator and specific value '1753-01-01'.")]
        [DataRow(DATETIME_TYPE, "lte", "'1753-01-01'", "\"1753-01-01\"", " <= ",
            DisplayName = "datetime type filter and orderby test with lte operator and specific value '1753-01-01'.")]
        [DataRow(DATETIME_TYPE, "neq", "'1753-01-01'", "\"1753-01-01\"", "!=",
            DisplayName = "datetime type filter and orderby test with neq operator and specific value '1753-01-01'.")]
        [DataRow(DATETIME_TYPE, "eq", "'1753-01-01'", "\"1753-01-01\"", "=",
            DisplayName = "datetime type filter and orderby test with eq operator and specific value '1753-01-01'.")]
        [DataRow(DATETIME_TYPE, "gt", "'1999-01-08 10:23:00'", "\"1999-01-08 10:23:00\"", " > ",
            DisplayName = "datetime type filter and orderby test with gt operator and specific value '1999-01-08 10:23:00'.")]
        [DataRow(DATETIME_TYPE, "gte", "'1999-01-08 10:23:00'", "\"1999-01-08 10:23:00\"", " >= ",
            DisplayName = "datetime type filter and orderby test with gte operator and specific value '1999-01-08 10:23:00'.")]
        [DataRow(DATETIME_TYPE, "lt", "'9999-12-31 23:59:59'", "\"9999-12-31 23:59:59\"", " < ",
            DisplayName = "datetime type filter and orderby test with lt operator and specific value '9999-12-31 23:59:59'.")]
        [DataRow(DATETIME_TYPE, "lte", "'9999-12-31 23:59:59'", "\"9999-12-31 23:59:59\"", " <= ",
            DisplayName = "datetime type filter and orderby test with lte operator and specific value '9999-12-31 23:59:59'.")]
        [DataRow(DATETIME_TYPE, "neq", "'1999-01-08 10:23:00'", "\"1999-01-08 10:23:00\"", "!=",
            DisplayName = "datetime type filter and orderby test with neq operator and specific value '1999-01-08 10:23:00'.")]
        [DataRow(DATETIME_TYPE, "eq", "'1999-01-08 10:23:00'", "\"1999-01-08 10:23:00\"", "=",
            DisplayName = "datetime type filter and orderby test with eq operator and specific value '1999-01-08 10:23:00'.")]
        [DataRow(DATETIME_TYPE, "lte", "'2079-06-06'", "\"2079-06-06\"", " <= ",
            DisplayName = "datetime type filter and orderby test with lte operator and specific value '2079-06-06'.")]
        [DataRow(DATETIME_TYPE, "neq", "'1999-01-08 10:23:54'", "\"1999-01-08 10:23:54\"", "!=",
            DisplayName = "datetime type filter and orderby test with neq operator and specific value '1999-01-08 10:23:54'.")]
        [DataRow(DATETIMEOFFSET_TYPE, "neq", "'1999-01-08 10:23:54.9999999-14:00'", "\"1999-01-08 10:23:54.9999999-14:00\"", "!=",
            DisplayName = "datetimeoffset type filter and orderby test with neq operator")]
        [DataRow(DATETIMEOFFSET_TYPE, "lt", "'9999-12-31 23:59:59.9999999'", "\"9999-12-31 23:59:59.9999999\"", "<",
            DisplayName = "datetimeoffset type filter and orderby test with lt operator and max value for datetimeoffset.")]
        [DataRow(DATETIMEOFFSET_TYPE, "eq", "'1999-01-08 10:23:54.9999999-14:00'", "\"1999-01-08 10:23:54.9999999-14:00\"", "=",
            DisplayName = "datetimeoffset type filter and orderby test with eq operator")]
        [DataRow(DATE_TYPE, "eq", "'1999-01-08'", "\"1999-01-08\"", "=",
            DisplayName = "date type filter and orderby test with eq operator")]
        [DataRow(DATE_TYPE, "gte", "'1999-01-08'", "\"1999-01-08\"", ">=",
            DisplayName = "date type filter test and orderby  with gte operator")]
        [DataRow(DATE_TYPE, "neq", "'9998-12-31'", "\"9998-12-31\"", "!=",
            DisplayName = "date type filter test and orderby  with ne operator")]
        [DataRow(SMALLDATETIME_TYPE, "eq", "'1999-01-08 10:24:00'", "\"1999-01-08 10:24:00\"", "=",
            DisplayName = "smalldatetime type filter and orderby test with eq operator")]
        [DataRow(SMALLDATETIME_TYPE, "gte", "'1999-01-08 10:24:00'", "\"1999-01-08 10:24:00\"", ">=",
            DisplayName = "smalldatetime type filter and orderby test with gte operator")]
        [DataRow(SMALLDATETIME_TYPE, "neq", "'1999-01-08 10:24:00'", "\"1999-01-08 10:24:00\"", "!=",
            DisplayName = "smalldatetime type filter and orderby test with neq operator")]
        [DataRow(DATETIME2_TYPE, "eq", "'1999-01-08 10:23:00.9999999'", "\"1999-01-08 10:23:00.9999999\"", "=",
            DisplayName = "datetime2 type filter and orderby test with eq operator")]
        [DataRow(DATETIME2_TYPE, "gt", "'0001-01-08 10:23:00.9999999'", "\"0001-01-08 10:23:00.9999999\"", " > ",
            DisplayName = "datetime2 type filter and orderby test with gt operator")]
        [DataRow(DATETIME2_TYPE, "gte", "'0001-01-08 10:23:00.9999999'", "\"0001-01-08 10:23:00.9999999\"", " >= ",
            DisplayName = "datetime2 type filter and orderby test with gte operator")]
        [DataRow(DATETIME2_TYPE, "lt", "'0002-06-06'", "\"0002-06-06\"", " < ",
            DisplayName = "datetime2 type filter and orderby test with lt operator")]
        [DataRow(DATETIME2_TYPE, "lte", "'9999-12-31'", "\"9999-12-31\"", " <= ",
            DisplayName = "datetime2 type filter and orderby test with lte operator")]
        [DataRow(DATETIME2_TYPE, "neq", "'9999-12-31 23:59:59'", "\"9999-12-31 23:59:59\"", "!=",
            DisplayName = "datetime2 type filter and orderby test with neq operator")]
        public async Task QueryTypeColumnFilterAndOrderByDateTime(string type, string filterOperator, string sqlValue, string gqlValue, string queryOperator)
        {
            // In MySQL, the DATETIME data type supports a range from '1000-01-01 00:00:00.0000000' to '9999-12-31 23:59:59.0000000'
            if (DatabaseEngine is TestCategory.MYSQL && sqlValue is "'9999-12-31 23:59:59.9999999'")
            {
                sqlValue = "'9999-12-31 23:59:59.0000000'";
                gqlValue = "\"9999-12-31 23:59:59.0000000\"";
            }

            await QueryTypeColumnFilterAndOrderBy(type, filterOperator, sqlValue, gqlValue, queryOperator);
        }

        /// <summary>
        /// Validates that usage of LocalTime values with comparison operators in GraphQL filters results in the expected filtered result set.
        /// </summary>
        [DataTestMethod]
        [DataRow(TIME_TYPE, "gt", "'00:00:00.000'", "\"00:00:00.000\"", " > ")]
        [DataRow(TIME_TYPE, "gte", "'10:13:14.123'", "\"10:13:14.123\"", " >= ")]
        [DataRow(TIME_TYPE, "lt", "'23:59:59.999'", "\"23:59:59.999\"", " < ")]
        [DataRow(TIME_TYPE, "lte", "'23:59:59.999'", "\"23:59:59.999\"", " <= ")]
        [DataRow(TIME_TYPE, "neq", "'10:23:54.9999999'", "\"10:23:54.9999999\"", "!=")]
        [DataRow(TIME_TYPE, "eq", "'10:23:54.9999999'", "\"10:23:54.9999999\"", "=")]
        public async Task QueryTypeColumnFilterAndOrderByLocalTime(string type, string filterOperator, string sqlValue, string gqlValue, string queryOperator)
        {
            await QueryTypeColumnFilterAndOrderBy(type, filterOperator, sqlValue, gqlValue, queryOperator);
        }

        /// <summary>
        /// Validates that LocalTime values with X precision are handled correctly: precision of 7 decimal places used with eq (=) will 
        /// not return result with only 3 decimal places i.e. 10:23:54.999 != 10:23:54.9999999
        /// In the Database only one row exist with value 23:59:59.9999999
        /// </summary>
        [DataTestMethod]
        [DataRow("\"23:59:59.9999999\"", 1, DisplayName = "TimeType Precision Check with 7 decimal places")]
        [DataRow("\"23:59:59.999\"", 0, DisplayName = "TimeType Precision Check with 3 decimal places")]
        public async Task TestTimeTypePrecisionCheck(string gqlValue, int count)
        {
            if (!IsSupportedType(TIME_TYPE))
            {
                Assert.Inconclusive("Type not supported");
            }

            string graphQLQueryName = "supportedTypes";
            string gqlQuery = @"{
                supportedTypes(first: 100 orderBy: { " + "time_types" + ": ASC } filter: { " + "time_types" + ": {" + "eq" + ": " + gqlValue + @"} }) {
                    items {
                        " + "time_types" + @"
                    }
                }
            }";

            JsonElement gqlResponse = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: false);
            Assert.AreEqual(count, gqlResponse.GetProperty("items").GetArrayLength());
        }

        /// <summary>
        /// The method constructs a GraphQL query to insert the value into the database table
        /// and then executes the query and compares the expected result with the actual result to verify if different types are supported.
        /// </summary>
        [DataTestMethod]
        [DataRow(BYTE_TYPE, "255")]
        [DataRow(BYTE_TYPE, "0")]
        [DataRow(BYTE_TYPE, "null")]
        [DataRow(SHORT_TYPE, "0")]
        [DataRow(SHORT_TYPE, "30000")]
        [DataRow(SHORT_TYPE, "-30000")]
        [DataRow(SHORT_TYPE, "null")]
        [DataRow(INT_TYPE, "9999")]
        [DataRow(INT_TYPE, "0")]
        [DataRow(INT_TYPE, "-9999")]
        [DataRow(INT_TYPE, "null")]
        [DataRow(UUID_TYPE, "3a1483a5-9ac2-4998-bcf3-78a28078c6ac")]
        [DataRow(UUID_TYPE, "null")]
        [DataRow(LONG_TYPE, "0")]
        [DataRow(LONG_TYPE, "9000000000000000000")]
        [DataRow(LONG_TYPE, "-9000000000000000000")]
        [DataRow(LONG_TYPE, "null")]
        [DataRow(STRING_TYPE, "\"aaaaaaaaaa\"")]
        [DataRow(STRING_TYPE, "\"\"")]
        [DataRow(STRING_TYPE, "null")]
        [DataRow(SINGLE_TYPE, "-3.33")]
        [DataRow(SINGLE_TYPE, "2E35")]
        [DataRow(SINGLE_TYPE, "123")]
        [DataRow(SINGLE_TYPE, "null")]
        [DataRow(FLOAT_TYPE, "-3.33")]
        [DataRow(FLOAT_TYPE, "2E150")]
        [DataRow(FLOAT_TYPE, "null")]
        [DataRow(DECIMAL_TYPE, "-3.333333")]
        [DataRow(DECIMAL_TYPE, "1222222.00000929292")]
        [DataRow(DECIMAL_TYPE, "null")]
        [DataRow(BOOLEAN_TYPE, "true")]
        [DataRow(BOOLEAN_TYPE, "false")]
        [DataRow(BOOLEAN_TYPE, "null")]
        [DataRow(BYTEARRAY_TYPE, "\"U3RyaW5neQ==\"")]
        [DataRow(BYTEARRAY_TYPE, "\"V2hhdGNodSBkb2luZyBkZWNvZGluZyBvdXIgdGVzdCBiYXNlNjQgc3RyaW5ncz8=\"")]
        [DataRow(BYTEARRAY_TYPE, "null")]
        [DataRow(TIME_TYPE, "\"23:59:59.9999999\"")]
        [DataRow(TIME_TYPE, "\"23:59:59\"")]
        [DataRow(TIME_TYPE, "\"23:59:59.9\"")]
        [DataRow(TIME_TYPE, "\"23:59\"")]
        [DataRow(TIME_TYPE, "null")]
        [DataRow(DATETIME_TYPE, "\"1753-01-01 00:00:00.000\"")]
        [DataRow(DATETIME_TYPE, "\"9999-12-31 23:59:59.997\"")]
        [DataRow(DATETIME_TYPE, "\"9999-12-31T23:59:59.997\"")]
        [DataRow(DATETIME_TYPE, "\"9999-12-31 23:59:59.997Z\"")]
        [DataRow(DATETIME_TYPE, "null")]
        [DataRow(SMALLDATETIME_TYPE, "\"1900-01-01\"")]
        [DataRow(SMALLDATETIME_TYPE, "\"2079-06-06\"")]
        [DataRow(DATETIME2_TYPE, "\"0001-01-01 00:00:00.0000000\"")]
        [DataRow(DATETIME2_TYPE, "\"9999-12-31 23:59:59.9999999\"")]
        [DataRow(DATETIME2_TYPE, "\"9999-12-31 23:59:59.999Z\"")]
        [DataRow(DATETIME2_TYPE, "\"9999-12-31T23:59:59.9999999\"")]
        [DataRow(DATE_TYPE, "\"0001-01-01\"")]
        [DataRow(DATE_TYPE, "\"9999-12-31\"")]
        [DataRow(DATETIMEOFFSET_TYPE, "\"0001-01-01 00:00:00.0000000\"")]
        [DataRow(DATETIMEOFFSET_TYPE, "\"9999-12-31 23:59:59.9999999\"")]
        [DataRow(DATETIMEOFFSET_TYPE, "\"9999-12-31T23:59:59.9999999\"")]
        [DataRow(DATETIMEOFFSET_TYPE, "\"9999-12-31 23:59:59.9999999Z\"")]
        public async Task InsertIntoTypeColumn(string type, string value)
        {
            if (!IsSupportedType(type))
            {
                Assert.Inconclusive("Type not supported");
            }

            string field = $"{type.ToLowerInvariant()}_types";
            string graphQLQueryName = "createSupportedType";
            string gqlQuery = "mutation{ createSupportedType (item: {" + field + ": " + value + " }){ " + field + " } }";

            string dbQuery = MakeQueryOnTypeTable(new List<string> { field }, id: 5001);

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            PerformTestEqualsForExtendedTypes(type, expected, actual.ToString());

            await ResetDbStateAsync();
        }

        /// <summary>
        /// Test case for invalid time, such as negative values or hours>24 or minutes/seconds>60.
        /// </summary>
        [DataTestMethod]
        [DataRow(TIME_TYPE, "\"32:59:59.9999999\"")]
        [DataRow(TIME_TYPE, "\"22:67:59.9999999\"")]
        [DataRow(TIME_TYPE, "\"14:12:99.9999999\"")]
        [DataRow(TIME_TYPE, "\"-22:67:59.9999999\"")]
        [DataRow(TIME_TYPE, "\"22:-67:59.9999999\"")]
        [DataRow(TIME_TYPE, "\"22:67:59.-9999999\"")]
        public async Task InsertInvalidTimeIntoTimeTypeColumn(string type, string value)
        {
            if (!IsSupportedType(type))
            {
                Assert.Inconclusive("Type not supported");
            }

            string field = $"{type.ToLowerInvariant()}_types";
            string graphQLQueryName = "createSupportedType";
            string gqlQuery = "mutation{ createSupportedType (item: {" + field + ": " + value + " }){ " + field + " } }";

            JsonElement response = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: true);
            string responseMessage = Regex.Unescape(JsonSerializer.Serialize(response));
            Assert.IsTrue(responseMessage.Contains($"{value} cannot be resolved as column \"{field}\" with type \"TimeSpan\"."));
        }

        [DataTestMethod]
        [DataRow(BYTE_TYPE, 255)]
        [DataRow(SHORT_TYPE, 30000)]
        [DataRow(INT_TYPE, 9999)]
        [DataRow(LONG_TYPE, 9000000000000000000)]
        [DataRow(STRING_TYPE, "aaaaaaaaaa")]
        [DataRow(SINGLE_TYPE, 123.1)]
        [DataRow(SINGLE_TYPE, 123)]
        [DataRow(SINGLE_TYPE, null)]
        [DataRow(FLOAT_TYPE, -3.33)]
        [DataRow(FLOAT_TYPE, 123)]
        [DataRow(FLOAT_TYPE, null)]
        [DataRow(DECIMAL_TYPE, 1222222.00000929292)]
        [DataRow(DECIMAL_TYPE, 123)]
        [DataRow(DECIMAL_TYPE, null)]
        [DataRow(BOOLEAN_TYPE, true)]
        [DataRow(DATETIMEOFFSET_TYPE, "1999-01-08 10:23:54+8:00")]
        [DataRow(DATETIME_TYPE, "1999-01-08 10:23:54")]
        [DataRow(TIME_TYPE, "\"23:59:59.9999999\"")]
        [DataRow(TIME_TYPE, "null")]
        [DataRow(BYTEARRAY_TYPE, "V2hhdGNodSBkb2luZyBkZWNvZGluZyBvdXIgdGVzdCBiYXNlNjQgc3RyaW5ncz8=")]
        [DataRow(UUID_TYPE, "3a1483a5-9ac2-4998-bcf3-78a28078c6ac")]
        public async Task InsertIntoTypeColumnWithArgument(string type, object value)
        {
            if (!IsSupportedType(type))
            {
                Assert.Inconclusive("Type not supported");
            }

            string field = $"{type.ToLowerInvariant()}_types";
            string graphQLQueryName = "createSupportedType";
            string gqlQuery = "mutation($param: " + TypeNameToGraphQLType(type) + "){ createSupportedType (item: {" + field + ": $param }){ " + field + " } }";

            string dbQuery = MakeQueryOnTypeTable(new List<string> { field }, id: 5001);

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: true, new() { { "param", value } });
            string expected = await GetDatabaseResultAsync(dbQuery);

            PerformTestEqualsForExtendedTypes(type, expected, actual.ToString());

            await ResetDbStateAsync();
        }

        [DataTestMethod]
        [DataRow(BYTE_TYPE, "255")]
        [DataRow(BYTE_TYPE, "0")]
        [DataRow(BYTE_TYPE, "null")]
        [DataRow(SHORT_TYPE, "0")]
        [DataRow(SHORT_TYPE, "30000")]
        [DataRow(SHORT_TYPE, "-30000")]
        [DataRow(SHORT_TYPE, "null")]
        [DataRow(INT_TYPE, "9999")]
        [DataRow(INT_TYPE, "0")]
        [DataRow(INT_TYPE, "-9999")]
        [DataRow(INT_TYPE, "null")]
        [DataRow(LONG_TYPE, "0")]
        [DataRow(LONG_TYPE, "9000000000000000000")]
        [DataRow(LONG_TYPE, "-9000000000000000000")]
        [DataRow(LONG_TYPE, "null")]
        [DataRow(STRING_TYPE, "\"aaaaaaaaaa\"")]
        [DataRow(STRING_TYPE, "\"\"")]
        [DataRow(STRING_TYPE, "null")]
        [DataRow(SINGLE_TYPE, "-3.33")]
        [DataRow(SINGLE_TYPE, "2E35")]
        [DataRow(SINGLE_TYPE, "123")]
        [DataRow(SINGLE_TYPE, "null")]
        [DataRow(FLOAT_TYPE, "-3.33")]
        [DataRow(FLOAT_TYPE, "2E150")]
        [DataRow(FLOAT_TYPE, "null")]
        [DataRow(FLOAT_TYPE, "123")]
        [DataRow(DECIMAL_TYPE, "-3.333333")]
        [DataRow(DECIMAL_TYPE, "1222222.00000929292")]
        [DataRow(DECIMAL_TYPE, "null")]
        [DataRow(DECIMAL_TYPE, "123")]
        [DataRow(BOOLEAN_TYPE, "true")]
        [DataRow(BOOLEAN_TYPE, "false")]
        [DataRow(BOOLEAN_TYPE, "null")]
        [DataRow(DATETIMEOFFSET_TYPE, "\"1999-01-08 10:23:54+8:00\"")]
        [DataRow(DATETIME_TYPE, "\"1999-01-08 09:20:00\"")]
        [DataRow(DATETIME_TYPE, "\"1999-01-08\"")]
        [DataRow(DATETIME_TYPE, "null")]
        [DataRow(TIME_TYPE, "\"23:59:59.9999999\"")]
        [DataRow(TIME_TYPE, "null")]
        [DataRow(BYTEARRAY_TYPE, "\"U3RyaW5neQ==\"")]
        [DataRow(BYTEARRAY_TYPE, "\"V2hhdGNodSBkb2luZyBkZWNvZGluZyBvdXIgdGVzdCBiYXNlNjQgc3RyaW5ncz8=\"")]
        [DataRow(BYTEARRAY_TYPE, "null")]
        [DataRow(UUID_TYPE, "\"3a1483a5-9ac2-4998-bcf3-78a28078c6ac\"")]
        [DataRow(UUID_TYPE, "null")]
        public async Task UpdateTypeColumn(string type, string value)
        {
            if (!IsSupportedType(type))
            {
                Assert.Inconclusive("Type not supported");
            }

            string field = $"{type.ToLowerInvariant()}_types";
            string graphQLQueryName = "updateSupportedType";
            string gqlQuery = "mutation{ updateSupportedType (typeid: 1, item: {" + field + ": " + value + " }){ " + field + " } }";

            string dbQuery = MakeQueryOnTypeTable(new List<string> { field }, id: 1);

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            PerformTestEqualsForExtendedTypes(type, expected, actual.ToString());

            await ResetDbStateAsync();
        }

        [DataTestMethod]
        [DataRow(BYTE_TYPE, 255)]
        [DataRow(SHORT_TYPE, 30000)]
        [DataRow(INT_TYPE, 9999)]
        [DataRow(LONG_TYPE, 9000000000000000000)]
        [DataRow(STRING_TYPE, "aaaaaaaaaa")]
        [DataRow(SINGLE_TYPE, 2E35)]
        [DataRow(SINGLE_TYPE, 123)]
        [DataRow(SINGLE_TYPE, null)]
        [DataRow(FLOAT_TYPE, -3.33)]
        [DataRow(FLOAT_TYPE, 123)]
        [DataRow(FLOAT_TYPE, null)]
        [DataRow(DECIMAL_TYPE, 1222222.00000929292)]
        [DataRow(DECIMAL_TYPE, 123)]
        [DataRow(DECIMAL_TYPE, null)]
        [DataRow(BOOLEAN_TYPE, true)]
        [DataRow(DATETIME_TYPE, "1999-01-08 10:23:54")]
        [DataRow(DATETIMEOFFSET_TYPE, "1999-01-08 10:23:54+8:00")]
        [DataRow(BYTEARRAY_TYPE, "V2hhdGNodSBkb2luZyBkZWNvZGluZyBvdXIgdGVzdCBiYXNlNjQgc3RyaW5ncz8=")]
        [DataRow(UUID_TYPE, "3a1483a5-9ac2-4998-bcf3-78a28078c6ac")]
        [DataRow(UUID_TYPE, null)]
        public async Task UpdateTypeColumnWithArgument(string type, object value)
        {
            if (!IsSupportedType(type))
            {
                Assert.Inconclusive("Type not supported");
            }

            string field = $"{type.ToLowerInvariant()}_types";
            string graphQLQueryName = "updateSupportedType";
            string gqlQuery = "mutation($param: " + TypeNameToGraphQLType(type) + "){ updateSupportedType (typeid: 1, item: {" + field + ": $param }){ " + field + " } }";

            string dbQuery = MakeQueryOnTypeTable(new List<string> { field }, id: 1);

            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlQuery, graphQLQueryName, isAuthenticated: true, new() { { "param", value } });
            string expected = await GetDatabaseResultAsync(dbQuery);

            PerformTestEqualsForExtendedTypes(type, expected, actual.ToString());

            await ResetDbStateAsync();
        }

        #endregion

        /// <summary>
        /// Utility function to do special comparisons for some of the extended types
        /// if json compare doesn't suffice
        /// </summary>
        private static void PerformTestEqualsForExtendedTypes(string type, string expected, string actual)
        {
            switch (type)
            {
                case UUID_TYPE:
                    CompareUuidResults(actual.ToString(), expected);
                    break;
                case SINGLE_TYPE:
                case FLOAT_TYPE:
                case DECIMAL_TYPE:
                    CompareFloatResults(type, actual.ToString(), expected);
                    break;
                case TIME_TYPE:
                    CompareTimeResults(actual.ToString(), expected);
                    break;
                case DATE_TYPE:
                case SMALLDATETIME_TYPE:
                case DATETIME_TYPE:
                case DATETIME2_TYPE:
                    CompareDateTimeResults(actual.ToString(), expected, type);
                    break;
                case DATETIMEOFFSET_TYPE:
                    CompareDateTimeOffsetResults(actual.ToString(), expected);
                    break;
                default:
                    SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
                    break;
            }
        }

        private static void CompareUuidResults(string actual, string expected)
        {
            string fieldName = "uuid_types";

            using JsonDocument actualJsonDoc = JsonDocument.Parse(actual);
            using JsonDocument expectedJsonDoc = JsonDocument.Parse(expected);

            if (actualJsonDoc.RootElement.ValueKind is JsonValueKind.Array)
            {
                ValidateArrayResults(actualJsonDoc, expectedJsonDoc, fieldName);
                return;
            }

            string actualUuidString = actualJsonDoc.RootElement.GetProperty(fieldName).ToString();
            string expectedUuidString = expectedJsonDoc.RootElement.GetProperty(fieldName).ToString();

            // handles cases when one of the values is null
            if (string.IsNullOrEmpty(actualUuidString) || string.IsNullOrEmpty(expectedUuidString))
            {
                Assert.AreEqual(expectedUuidString, actualUuidString);
            }
            else
            {
                AssertOnFields(fieldName, actualUuidString, expectedUuidString);
            }
        }

        /// <summary>
        /// HotChocolate will parse large floats to exponential notation
        /// while the db will return the number fully printed out. Because
        /// the json deep compare function we are using does not account for such scenario
        /// a special comparison is needed to test floats
        /// </summary>
        private static void CompareFloatResults(string floatType, string actual, string expected)
        {
            string fieldName = $"{floatType.ToLowerInvariant()}_types";

            using JsonDocument actualJsonDoc = JsonDocument.Parse(actual);
            using JsonDocument expectedJsonDoc = JsonDocument.Parse(expected);

            if (actualJsonDoc.RootElement.ValueKind is JsonValueKind.Array)
            {
                ValidateArrayResults(actualJsonDoc, expectedJsonDoc, fieldName);
                return;
            }

            string actualFloat;
            string expectedFloat;
            actualFloat = actualJsonDoc.RootElement.GetProperty(fieldName).ToString();
            expectedFloat = expectedJsonDoc.RootElement.GetProperty(fieldName).ToString();

            // handles cases when one of the values is null
            if (string.IsNullOrEmpty(actualFloat) || string.IsNullOrEmpty(expectedFloat))
            {
                Assert.AreEqual(expectedFloat, actualFloat);
                return;
            }

            switch (floatType)
            {
                case SINGLE_TYPE:
                    Assert.AreEqual(float.Parse(expectedFloat), float.Parse(actualFloat));
                    break;
                case FLOAT_TYPE:
                    Assert.AreEqual(double.Parse(expectedFloat), double.Parse(actualFloat));
                    break;
                case DECIMAL_TYPE:
                    Assert.AreEqual(decimal.Parse(expectedFloat), decimal.Parse(actualFloat));
                    break;
                default:
                    Assert.Fail($"Calling compare on unrecognized float type {floatType}");
                    break;
            }
        }

        /// <summary>
        /// Required due to different format between sql datetime and HotChocolate datetime
        /// result
        /// </summary>
        private static void CompareDateTimeResults(string actual, string expected, string fieldType)
        {
            string fieldName = $"{fieldType.ToLower()}_types";

            using JsonDocument actualJsonDoc = JsonDocument.Parse(actual);
            using JsonDocument expectedJsonDoc = JsonDocument.Parse(expected);

            if (actualJsonDoc.RootElement.ValueKind is JsonValueKind.Array)
            {
                ValidateArrayResults(actualJsonDoc, expectedJsonDoc, fieldName);
                return;
            }

            string actualDateTime = actualJsonDoc.RootElement.GetProperty(fieldName).ToString();
            string expectedDateTime = expectedJsonDoc.RootElement.GetProperty(fieldName).ToString();

            // handles cases when one of the values is null
            if (string.IsNullOrEmpty(actualDateTime) || string.IsNullOrEmpty(expectedDateTime))
            {
                Assert.AreEqual(expectedDateTime, actualDateTime);
            }
            else
            {
                AssertOnFields(fieldName, actualDateTime, expectedDateTime);
            }
        }

        /// <summary>
        /// Required due to different format between sql datetimeoffset and HotChocolate datetime
        /// result
        /// </summary>
        private static void CompareDateTimeOffsetResults(string actual, string expected)
        {
            string fieldName = "datetimeoffset_types";

            using JsonDocument actualJsonDoc = JsonDocument.Parse(actual);
            using JsonDocument expectedJsonDoc = JsonDocument.Parse(expected);

            if (actualJsonDoc.RootElement.ValueKind is JsonValueKind.Array)
            {
                ValidateArrayResults(actualJsonDoc, expectedJsonDoc, fieldName);
                return;
            }

            string actualDateTimeOffsetString = actualJsonDoc.RootElement.GetProperty(fieldName).ToString();
            string expectedDateTimeOffsetString = expectedJsonDoc.RootElement.GetProperty(fieldName).ToString();

            // handles cases when one of the values is null
            if (string.IsNullOrEmpty(actualDateTimeOffsetString) || string.IsNullOrEmpty(expectedDateTimeOffsetString))
            {
                Assert.AreEqual(expectedDateTimeOffsetString, actualDateTimeOffsetString);
            }
            else
            {
                AssertOnFields(fieldName, actualDateTimeOffsetString, expectedDateTimeOffsetString);
            }
        }

        /// <summary>
        /// Compares the value from SQL time and HotChocolate LocalTime.
        /// </summary>
        private static void CompareTimeResults(string actual, string expected)
        {
            string fieldName = "time_types";

            using JsonDocument actualJsonDoc = JsonDocument.Parse(actual);
            using JsonDocument expectedJsonDoc = JsonDocument.Parse(expected);

            if (actualJsonDoc.RootElement.ValueKind is JsonValueKind.Array)
            {
                ValidateArrayResults(actualJsonDoc, expectedJsonDoc, fieldName);
                return;
            }

            string actualTimeString = actualJsonDoc.RootElement.GetProperty(fieldName).ToString();
            string expectedTimeString = expectedJsonDoc.RootElement.GetProperty(fieldName).ToString();

            // handles cases when one of the values is null
            if (string.IsNullOrEmpty(actualTimeString) || string.IsNullOrEmpty(expectedTimeString))
            {
                Assert.AreEqual(expectedTimeString, actualTimeString);
            }
            else
            {
                AssertOnFields(fieldName, actualTimeString, expectedTimeString);
            }
        }

        private static void ValidateArrayResults(JsonDocument actualJsonDoc, JsonDocument expectedJsonDoc, string fieldName)
        {
            JsonElement.ArrayEnumerator actualEnumerater = actualJsonDoc.RootElement.EnumerateArray();
            foreach (JsonElement expectedElement in expectedJsonDoc.RootElement.EnumerateArray())
            {
                actualEnumerater.MoveNext();
                JsonElement actualElement = actualEnumerater.Current;
                actualElement.TryGetProperty(fieldName, out JsonElement actualValue);
                expectedElement.TryGetProperty(fieldName, out JsonElement expectedValue);

                AssertOnFields(fieldName, actualValue.ToString(), expectedValue.ToString());
            }
        }

        /// <summary>
        /// Compare given fields from actual and expected json.
        /// </summary>
        private static void AssertOnFields(string field, string actualElement, string expectedElement)
        {
            if (field.StartsWith(DATETIMEOFFSET_TYPE.ToLower()))
            {
                DateTimeOffset actualDateTimeOffset = DateTimeOffset.Parse(actualElement.ToString(), DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal);
                DateTimeOffset expectedDateTimeOffset = DateTimeOffset.Parse(expectedElement.ToString(), DateTimeFormatInfo.InvariantInfo, DateTimeStyles.AssumeUniversal);
                Assert.AreEqual(actualDateTimeOffset.ToString(), expectedDateTimeOffset.ToString());
                // Comparing for milliseconds separately since HotChocolate time type is resolved only to 3 decimal places.
                Assert.AreEqual(actualDateTimeOffset.Millisecond, expectedDateTimeOffset.Millisecond);
            }
            else if (field.StartsWith(DATETIME2_TYPE.ToLower()))
            {
                // Adjusting to universal, since DateTime doesn't account for TimeZone
                DateTime actualDateTime = DateTime.Parse(actualElement.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
                DateTime expectedDateTime = DateTime.Parse(expectedElement.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
                Assert.AreEqual(expectedDateTime.ToString(), actualDateTime.ToString());
                // Comparing for milliseconds separately since HotChocolate datetime2 type is resolved only to 3 decimal places.
                Assert.AreEqual(expectedDateTime.Millisecond, actualDateTime.Millisecond);
            }
            else if (field.StartsWith(DATE_TYPE.ToLower()) || field.StartsWith(SMALLDATETIME_TYPE.ToLower()) || field.StartsWith(DATETIME_TYPE.ToLower()))
            {
                // Adjusting to universal, since DateTime doesn't account for TimeZone
                DateTime actualDateTime = DateTime.Parse(actualElement.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
                DateTime expectedDateTime = DateTime.Parse(expectedElement.ToString(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
                Assert.AreEqual(expectedDateTime, actualDateTime);
            }
            else if (field.StartsWith(SINGLE_TYPE.ToLower()))
            {
                Assert.AreEqual(float.Parse(expectedElement), float.Parse(actualElement));
            }
            else if (field.StartsWith(TIME_TYPE.ToLower()))
            {
                TimeOnly actualTime = TimeOnly.Parse(actualElement.ToString());
                TimeOnly expectedTime = TimeOnly.Parse(expectedElement.ToString());
                Assert.AreEqual(expectedTime.ToLongTimeString(), actualTime.ToLongTimeString());
            }
            else if (field.StartsWith(UUID_TYPE.ToLower()))
            {
                Guid actualValue = Guid.Parse(actualElement.ToString());
                Guid expectedValue = Guid.Parse(expectedElement.ToString());
                Assert.AreEqual(actualValue, expectedValue);
            }
            else
            {
                Assert.AreEqual(double.Parse(expectedElement), double.Parse(actualElement));
            }
        }

        /// <summary>
        /// Needed to map the type name to a graphql type in argument tests
        /// where the argument type need to be specified.
        /// </summary>
        private static string TypeNameToGraphQLType(string typeName)
        {
            if (typeName is DATETIMEOFFSET_TYPE)
            {
                return DATETIME_TYPE;
            }

            return typeName;
        }

        protected abstract string MakeQueryOnTypeTable(
            List<string> queriedColumns,
            string filterValue = "1",
            string filterOperator = "=",
            string filterField = "1",
            string orderBy = "id",
            string limit = "1");

        protected abstract string MakeQueryOnTypeTable(List<string> columnsToQuery, int id);
        protected virtual bool IsSupportedType(string type)
        {
            return true;
        }
    }
}
