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
            await InitializeTestFixture(context);
        }

        /// <inheritdoc />
        [DataTestMethod]
        [DataRow("id", 1, 4, "", "",
            DisplayName = "Test after token for primary key values.")]
        [DataRow("byte_types", 0, 255, 2, 4, DisplayName = "Test after token for byte values.")]
        [DataRow("short_types", -32768, 32767, 3, 4, DisplayName = "Test after token for short values.")]
        [DataRow("int_types", -2147483648, 2147483647, 3, 4,
            DisplayName = "Test after token for int values with mapped name.")]
        [DataRow("long_types", -9223372036854775808, 9.223372036854776E+18, 3, 4,
            DisplayName = "Test after token for long values.")]
        [DataRow("string_types", "\"\"", "\"null\"", 1, 4,
            DisplayName = "Test after token for string values.")]
        [DataRow("single_types", -3.39E38, 3.3999999521443642E+38, 3, 4,
            DisplayName = "Test after token for single values.")]
        [DataRow("float_types", -1.7E308, 1.7E308, 3, 4,
            DisplayName = "Test after token for float values.")]
        [DataRow("decimal_types", -9.292929, 0.333333, 2, 1,
            DisplayName = "Test after token for decimal values.")]
        [DataRow("boolean_types", "false", "true", 2, 4,
            DisplayName = "Test after token for boolean values.")]
        [DataRow("datetime_types", "\"1753-01-01T00:00:00.000\"",
            "\"9999-12-31 23:59:59.000000\"", 3, 4,
            DisplayName = "Test after token for datetime values.")]
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
