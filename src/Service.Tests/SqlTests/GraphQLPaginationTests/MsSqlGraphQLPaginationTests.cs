// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLPaginationTests
{

    /// <summary>
    /// Only sets up the underlying GraphQLPaginationTestBase to run tests for MsSql
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGraphQLPaginationTests : GraphQLPaginationTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
            await InitializeTestFixture();
        }

        /// <inheritdoc />
        [DataTestMethod]
        [DataRow("typeid", 1, 4, "", "",
            DisplayName = "Test after token for primary key with mapped name.")]
        [DataRow("byte_types", 0, 255, 2, 4, DisplayName = "Test after token for byte values.")]
        [DataRow("short_types", -32768, 32767, 3, 4, DisplayName = "Test after token for short values.")]
        [DataRow("int_types", -2147483648, 2147483647, 3, 4,
            DisplayName = "Test after token for int values.")]
        [DataRow("long_types", -9223372036854775808, 9.223372036854776E+18, 3, 4,
            DisplayName = "Test after token for long values.")]
        [DataRow("string_types", "\"\"", "\"null\"", 1, 4,
            DisplayName = "Test after token for string values.")]
        [DataRow("single_types", -3.39E38, 3.4E38, 3, 4,
            DisplayName = "Test after token for single values.")]
        [DataRow("float_types", -1.7E308, 1.7E308, 3, 4,
            DisplayName = "Test after token for float values.")]
        [DataRow("decimal_types", -9.292929, 0.333333, 2, 1,
            DisplayName = "Test after token for decimal values.")]
        [DataRow("boolean_types", "false", "true", 2, 4,
            DisplayName = "Test after token for boolean values.")]
        [DataRow("date_types", "\"0001-01-01\"",
            "\"9999-12-31\"", 3, 4,
            DisplayName = "Test after token for date values.")]
        [DataRow("datetime_types", "\"1753-01-01T00:00:00.000\"",
            "\"9999-12-31T23:59:59\"", 3, 4,
            DisplayName = "Test after token for datetime values.")]
        [DataRow("datetime2_types", "\"0001-01-01 00:00:00.0000000\"",
            "\"9999-12-31T23:59:59.9999999\"", 3, 4,
            DisplayName = "Test after token for datetime2 values.")]
        [DataRow("datetimeoffset_types", "\"0001-01-01 00:00:00.0000000+0:00\"",
            "\"9999-12-31T23:59:59.9999999+14:00\"", 3, 4,
            DisplayName = "Test after token for datetimeoffset values.")]
        [DataRow("smalldatetime_types", "\"1900-01-01 00:00:00\"",
            "\"2079-06-06T00:00:00\"", 3, 4,
            DisplayName = "Test after token for smalldate values.")]
        [DataRow("bytearray_types", "\"AAAAAA==\"", "\"/////w==\"", 3, 4,
            DisplayName = "Test after token for bytearray values.")]
        [TestMethod]
        public override async Task RequestAfterTokenOnly(
            string exposedFieldName,
            object afterValue,
            object endCursorValue,
            object afterIdValue,
            object endCursorIdValue)
        {
            await base.RequestAfterTokenOnly(
                exposedFieldName,
                afterValue,
                endCursorValue,
                afterIdValue,
                endCursorIdValue);
        }
    }
}
