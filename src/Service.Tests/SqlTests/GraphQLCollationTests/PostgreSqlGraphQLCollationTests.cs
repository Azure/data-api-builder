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
        [DataRow("books", "ASC", "title", @"SELECT json_agg(to_jsonb(table0)) FROM (SELECT title FROM books ORDER BY title asc) as table0")]
        [DataRow("books", "DESC", "title", @"SELECT json_agg(to_jsonb(table0)) FROM (SELECT title FROM books ORDER BY title desc) as table0")]
        [DataRow("authors", "ASC", "name", @"SELECT json_agg(to_jsonb(table0)) FROM (SELECT name FROM authors ORDER BY name asc) as table0")]
        [DataRow("authors", "DESC", "name", @"SELECT json_agg(to_jsonb(table0)) FROM (SELECT name FROM authors ORDER BY name desc) as table0")]
        [DataRow("shrubs", "ASC", "fancyName", @"SELECT json_agg(to_jsonb(table0)) FROM (SELECT species as ""fancyName"" FROM trees as shrubs ORDER BY species asc) as table0")]
        [DataRow("shrubs", "DESC", "fancyName", @"SELECT json_agg(to_jsonb(table0)) FROM (SELECT species as ""fancyName"" FROM trees as shrubs ORDER BY species desc) as table0")]
        public async Task PostgreCapitalizationResultQuery(string type, string order, string item, string dbQuery)
        {
            await CapitalizationResultQuery(type, order, item, dbQuery);
        }

        #endregion
    }
}
