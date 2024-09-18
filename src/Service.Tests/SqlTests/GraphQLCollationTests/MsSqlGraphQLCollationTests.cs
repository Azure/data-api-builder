// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLCollationTests
{
    /// <summary>
    /// Test GraphQL Collations validating proper operations.
    /// </summary>
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGraphQLCollationTests : GraphQLCollationTestBase
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

        #region Tests
        /// <summary>
        /// MsSql Capitalization Collation Tests
        /// </summary>
        [DataTestMethod]
        [DataRow("books", "title", @"SELECT title FROM books ORDER BY title ASC FOR JSON PATH, INCLUDE_NULL_VALUES")]
        [DataRow("books", "title", @"SELECT title FROM BOOKS ORDER BY TITLE ASC FOR JSON PATH, INCLUDE_NULL_VALUES")]
        [DataRow("books", "title", @"SELECT title FROM Books ORDER BY Title ASC FOR JSON PATH, INCLUDE_NULL_VALUES")]
        public async Task MsSqlCapitalizationResultQuery(string type, string item, string dbQuery)
        {
            await CapitalizationResultQuery(type, item, dbQuery);
        }

        #endregion
    }
}
