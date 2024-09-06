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
        [DataRow("books", "ASC", "title", @"SELECT title FROM books ORDER BY title ASC FOR JSON PATH, INCLUDE_NULL_VALUES")]
        [DataRow("books", "DESC", "title", @"SELECT title FROM books ORDER BY title DESC FOR JSON PATH, INCLUDE_NULL_VALUES")]
        [DataRow("authors", "ASC", "name", @"SELECT name FROM authors ORDER BY name ASC FOR JSON PATH, INCLUDE_NULL_VALUES")]
        [DataRow("authors", "DESC", "name", @"SELECT name FROM authors ORDER BY name DESC FOR JSON PATH, INCLUDE_NULL_VALUES")]
        [DataRow("shrubs", "ASC", "fancyName", @"SELECT species as fancyName FROM trees ORDER BY species ASC FOR JSON PATH, INCLUDE_NULL_VALUES")]
        [DataRow("shrubs", "DESC", "fancyName", @"SELECT species as fancyName FROM trees ORDER BY species DESC FOR JSON PATH, INCLUDE_NULL_VALUES")]
        public async Task MsCapitalizationResultQuery(string type, string order, string item, string dbQuery)
        {
            await CapitalizationResultQuery(type, order, item, dbQuery);
        }

        #endregion
    }
}
