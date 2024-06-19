// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedDateTimeTypes;
using static Azure.DataApiBuilder.Service.GraphQLBuilder.GraphQLTypes.SupportedHotChocolateTypes;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLSupportedTypesTests
{

    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlGQLSupportedTypesTests : GraphQLSupportedTypesTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MYSQL;
            await InitializeTestFixture();
        }

        /// <summary>
        /// Validates that GraphQL mutation input for:
        /// GraphQL Field Name (Type): datetime_types (DateTime)
        /// MySQL Field Name (Type): datetime_types (datetime)
        /// succeeds when the input is a valid DateTime string as described by MySQL documentation.
        /// The field under test is of MySQL type datetime, with no fractional second precision.
        /// MySQL converts the supplied datetime value to UTC before storing it in the database.
        /// </summary>
        /// <see cref="https://dev.mysql.com/doc/refman/8.4/en/datetime.html">
        /// The DATETIME type is used for values that contain both date and time parts.
        /// MySQL retrieves and displays DATETIME values in 'YYYY-MM-DD hh:mm:ss' format.
        /// The supported range is '1000-01-01 00:00:00' to '9999-12-31 23:59:59'.
        /// </see>
        /// <see cref="https://dev.mysql.com/doc/refman/8.4/en/fractional-seconds.html">
        /// Inserting a TIME, DATE, or TIMESTAMP value with a fractional seconds part into a column
        /// of the same type but having fewer fractional digits results in rounding.
        /// </see>
        /// <see cref="https://dev.mysql.com/doc/refman/8.4/en/date-and-time-literals.html">
        /// - The (time zone) offset is not displayed when selecting a datetime value,
        /// even if one was used when inserting it.
        /// - The date and time parts can be separated by T rather than a space.
        /// For example, '2012-12-31 11:30:45' '2012-12-31T11:30:45' are equivalent.
        /// </see>
        /// <see cref="https://www.graphql-scalars.com/date-time/#only-date-time">
        /// UTC offset should be formatted as Z and not +00:00 and lower case z and t should be converted to Z and T.
        /// </see>
        /// <param name="dateTimeGraphQLInput">Unescaped string used as value for GraphQL input field datetime_types</param>
        /// <param name="expectedResult">Expected result the HotChocolate returns from resolving database response.</param>
        // Date and time
        [DataRow("1000-01-01 00:00:00", "1000-01-01T00:00:00.000Z", DisplayName = "Datetime value separated by space.")]
        [DataRow("9999-12-31T23:59:59", "9999-12-31T23:59:59.000Z", DisplayName = "Datetime value separated by T.")]
        [DataRow("9999-12-31 23:59:59Z", "9999-12-31T23:59:59.000Z", DisplayName = "Datetime value specified with UTC offset Z as resolved by HotChocolate.")]
        [DataRow("9999-12-31 23:59:59+00:00", "9999-12-31T23:59:59.000Z", DisplayName = "Datetime value specified with UTC offset with no datetime change when stored in db.")]
        [DataRow("9999-12-31 23:59:59+03:00", "9999-12-31T20:59:59.000Z", DisplayName = "Timezone offset UTC+03:00 accepted by MySQL because UTC value is in supported datetime range.")]
        [DataRow("9999-12-31 20:59:59-03:00", "9999-12-31T23:59:59.000Z", DisplayName = "Timezone offset UTC-03:00 accepted by MySQL because UTC value is in supported datetime range.")]
        // Fractional seconds rounded up/down when mysql column datetime doesn't specify fractional seconds
        // e.g. column not defined as datetime({1-6})
        [DataRow("9999-12-31 23:59:59.499999", "9999-12-31T23:59:59.000Z", DisplayName = "Fractional seconds rounded down because fractional seconds are passed to column with datatype datetime(0).")]
        [DataRow("2024-12-31 23:59:59.999999", "2025-01-01T00:00:00.000Z", DisplayName = "Fractional seconds rounded up because fractional seconds are passed to column with datatype datetime(0).")]
        // Only date
        [DataRow("9999-12-31", "9999-12-31T00:00:00.000Z", DisplayName = "Max date for datetime column stored with zeroed out time.")]
        [DataRow("1000-01-01", "1000-01-01T00:00:00.000Z", DisplayName = "Min date for datetime column stored with zeroed out time.")]
        [DataTestMethod]
        public async Task InsertMutationInput_DateTimeTypes_ValidRange_ReturnsExpectedValues(string dateTimeGraphQLInput, string expectedResult)
        {
            // Arrange
            const string DATETIME_FIELD = "datetime_types";
            string graphQLMutationName = "createSupportedType";
            string gqlMutation = "mutation{ createSupportedType (item: {" + DATETIME_FIELD + ": \"" + dateTimeGraphQLInput + "\" }){ typeid, " + DATETIME_FIELD + " } }";

            // Act
            JsonElement actual = await ExecuteGraphQLRequestAsync(gqlMutation, graphQLMutationName, isAuthenticated: true);

            // Assert
            Assert.IsTrue(
                condition: actual.GetProperty("typeid").TryGetInt32(out _),
                message: "Error: GraphQL mutation result indicates issue during record creation because primary key 'typeid' was not resolved.");

            Assert.AreEqual(
                expected: expectedResult,
                actual: actual.GetProperty(DATETIME_FIELD).GetString(),
                message: "Unexpected datetime value.");
        }

        /// <summary>
        /// For MySQL, values passed to columns with datatype datetime(0) (no fractional seconds) that only include time
        /// are auto-populated with the current date.
        /// Based on the supplied time (assumed to be UTC), gets the current date. This calculation must fetch
        /// DateTime.UtcNow, otherwise the test machine's timezone will be used and result in an unexpected date.
        /// </summary>
        /// <param name="dateTimeGraphQLInput">Unescaped string used as value for GraphQL input field datetime_types</param>
        /// <param name="expectedResult">Expected result the HotChocolate returns from resolving database response.</param>
        [DataRow("23:59:59.499999", "23:59:59.000Z", DisplayName = "hh:mm::ss.ffffff for datetime column stored with zeroed out date and rounded down fractional seconds.")]
        [DataRow("23:59:59", "23:59:59.000Z", DisplayName = "hh:mm:ss for datetime column stored with zeroed out date.")]
        [DataRow("23:59", "23:59:00.000Z", DisplayName = "hh:mm for datetime column stored with zeroed out date and seconds.")]
        [DataTestMethod]
        public async Task InsertMutationInput_DateTimeTypes_TimeOnly_ValidRange_ReturnsExpectedValues(string dateTimeGraphQLInput, string expectedResult)
        {
            // Arrange
            expectedResult = DateTime.UtcNow.ToString("yyyy-MM-ddT") + expectedResult;
            await InsertMutationInput_DateTimeTypes_ValidRange_ReturnsExpectedValues(dateTimeGraphQLInput, expectedResult);
        }

        /// <summary>
        /// MySql Single Type Tests.
        /// </summary>
        /// <param name="graphqlDataType">GraphQL Data Type</param>
        /// <param name="filterOperator">Comparison operator: gt, lt, gte, lte, etc.</param>
        /// <param name="sqlValue">Value to be set in "expected value" sql query.</param>
        /// <param name="gqlValue">GraphQL input value supplied.</param>
        /// <param name="queryOperator">Query operator for "expected value" sql query.</param>
        [DataRow(SINGLE_TYPE, "gt", "-9.3", "-9.3", ">")]
        [DataRow(SINGLE_TYPE, "gte", "-9.2", "-9.2", ">=")]
        [DataRow(SINGLE_TYPE, "lt", ".33", "0.33", "<")]
        [DataRow(SINGLE_TYPE, "lte", ".33", "0.33", "<=")]
        [DataRow(SINGLE_TYPE, "neq", "9.2", "9.2", "!=")]
        [DataRow(SINGLE_TYPE, "eq", "'0.33'", "0.33", "=")]
        [DataTestMethod]
        public async Task MySql_real_graphql_single_filter_expectedValues(
            string graphqlDataType,
            string filterOperator,
            string sqlValue,
            string gqlValue,
            string queryOperator)
        {
            await QueryTypeColumnFilterAndOrderBy(graphqlDataType, filterOperator, sqlValue, gqlValue, queryOperator);
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
            string jsonResultProperties = string.Join(", ", queryFields.Select(field => $"\"{field.Alias}\" , {ProperlyFormatTypeTableColumn(field.BackingColumnName)}"));
            string formattedSelect = limit.Equals("1") ? "SELECT JSON_OBJECT(" + jsonResultProperties + @") AS `data`" :
                "SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(" + jsonResultProperties + @")), '[]') AS `data`";

            return @"
                " + formattedSelect + @"
                FROM (
                    SELECT " + string.Join(", ", queryFields.Select(field => field.BackingColumnName)) + @"
                    FROM type_table AS `table0`
                    WHERE " + filterField + " " + filterOperator + " " + filterValue + @"
                    ORDER BY " + orderBy + @" asc
                    LIMIT " + limit + @"
                    ) AS `subq3`
            ";
        }

        protected override bool IsSupportedType(string type)
        {
            return type switch
            {
                UUID_TYPE => false,
                DATE_TYPE => false,
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
            if (columnName.Contains(BOOLEAN_TYPE.ToLowerInvariant()))
            {
                return $"cast({columnName} is true as json)";
            }
            else if (columnName.Contains(BYTEARRAY_TYPE.ToLowerInvariant()))
            {
                return $"to_base64({columnName})";
            }
            else
            {
                return columnName;
            }
        }
    }
}
