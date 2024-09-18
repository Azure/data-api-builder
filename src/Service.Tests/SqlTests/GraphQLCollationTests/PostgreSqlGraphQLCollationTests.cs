// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLCollationTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGraphQLCollationTests : GraphQLCollationTestBase
    {
        /// <summary>
        /// Set the database engine for tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.POSTGRESQL;
            await InitializeTestFixture();
        }

        #region Tests
        /// <summary>
        /// Postgre Capitalization Collation Tests
        /// </summary>
        [DataTestMethod]
        [DataRow("books", "title", @"SELECT json_agg(to_jsonb(table0)) FROM (SELECT title FROM books ORDER BY title asc) as table0")]
        [DataRow("books", "title", @"SELECT json_agg(to_jsonb(table0)) FROM (SELECT TITLE FROM BOOKS ORDER BY TITLE asc) as table0")]
        [DataRow("books", "title", @"SELECT json_agg(to_jsonb(table0)) FROM (SELECT Title FROM Books ORDER BY Title asc) as table0")]
        public async Task PostgreCapitalizationResultQuery(string type, string item, string dbQuery)
        {
            await CapitalizationResultQuery(type, item, dbQuery);
        }

        #endregion
    }
}
