// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLPaginationTests
{

    /// <summary>
    /// Only sets up the underlying GraphQLPaginationTestBase to run tests for MySql
    /// </summary>
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlGraphQLPaginationTests : GraphQLPaginationTestBase
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

        /// <inheritdoc />
        [DataTestMethod]
        [DataRow("typeid", 1, 3, "", "", false,
            DisplayName = "Test after token for primary key with mapped name.")]
        [DataRow("typeid", 4, 6, "", "", true,
            DisplayName = "Test after token for primary key with mapped name for last page.")]
        [DataRow("byte_types", 0, 1, 2, 1, false, DisplayName = "Test after token for byte values.")]
        [DataRow("byte_types", 1, "", 1, "", true, DisplayName = "Test after token for byte values for last page.")]
        [DataRow("short_types", -32768, 1, 3, 1, false, DisplayName = "Test after token for short values.")]
        [DataRow("short_types", 1, "", 1, "", true, DisplayName = "Test after token for short values for last page.")]
        [DataRow("int_types", -2147483648, 1, 3, 1, false,
            DisplayName = "Test after token for int values.")]
        [DataRow("int_types", 1, "", 1, "", true,
            DisplayName = "Test after token for int values for last page.")]
        [DataRow("long_types", -9223372036854775808, 1, 3, 1, false,
            DisplayName = "Test after token for long values.")]
        [DataRow("long_types", 1, "", 1, "", true,
            DisplayName = "Test after token for long values for last page.")]
        [DataRow("string_types", "\"\"", "\"lksa;jdflasdf;alsdflksdfkldj\"", 1, 2, false,
            DisplayName = "Test after token for string values.")]
        [DataRow("string_types", "null", "", 3, "", true,
            DisplayName = "Test after token for string values for last page.")]
        [DataRow("single_types", -3.39E38, .33000001311302185, 3, 1, false,
            DisplayName = "Test after token for single values.")]
        [DataRow("single_types", .33, "", 1, "", true,
            DisplayName = "Test after token for single values for last page.")]
        [DataRow("float_types", -1.7E308, .33, 3, 1, false,
            DisplayName = "Test after token for float values.")]
        [DataRow("float_types", .33, "", 1, "", true,
            DisplayName = "Test after token for float values for last page.")]
        [DataRow("decimal_types", -9.292929, 0.0000000000000292929, 2, 4, false,
            DisplayName = "Test after token for decimal values.")]
        [DataRow("decimal_types", 0.333333, "", 1, "", true,
            DisplayName = "Test after token for decimal values for last page.")]
        [DataRow("boolean_types", "false", "true", 2, 3, false,
            DisplayName = "Test after token for boolean values.")]
        [DataRow("boolean_types", "true", "", 3, "", true,
            DisplayName = "Test after token for boolean values for last page.")]
        [DataRow("datetime_types", "\"1753-01-01T00:00:00.000\"",
            "\"1999-01-08T10:23:54\"", 3, 1, false,
            DisplayName = "Test after token for datetime values.")]
        [DataRow("datetime_types", "\"9999-12-31T23:59:59\"",
            "", 4, "", true,
            DisplayName = "Test after token for datetime values for last page.")]
        [DataRow("bytearray_types", "\"AAAAAA==\"", "\"q83vASM=\"", 3, 1, false,
            DisplayName = "Test after token for bytearray values.")]
        [DataRow("bytearray_types", "\"q83vASM=\"", "", 1, "", true,
            DisplayName = "Test after token for bytearray values for last page.")]
        [TestMethod]
        public override async Task RequestAfterTokenOnly(
            string exposedFieldName,
            object afterValue,
            object endCursorValue,
            object afterIdValue,
            object endCursorIdValue,
            bool isLastPage)
        {
            await base.RequestAfterTokenOnly(
                exposedFieldName,
                afterValue,
                endCursorValue,
                afterIdValue,
                endCursorIdValue,
                isLastPage);
        }
    }
}
