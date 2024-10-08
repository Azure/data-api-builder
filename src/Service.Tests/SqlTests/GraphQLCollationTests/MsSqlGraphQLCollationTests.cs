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
        //Collation setting for database
        private const string DEFAULT_COLLATION = "Latin1_General_100_CI_AI_SC_UTF8";
        private const string CASE_SENSITIVE_COLLATION = "Latin1_General_100_CS_AI_SC_UTF8";

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
        //[DataRow("books", "title", @"SELECT title FROM BOOKS ORDER BY TITLE ASC FOR JSON PATH, INCLUDE_NULL_VALUES")]
        //[DataRow("books", "title", @"SELECT title FROM Books ORDER BY Title ASC FOR JSON PATH, INCLUDE_NULL_VALUES")]
        public async Task MsSqlCapitalizationResultQuery(string type, string item, string dbQuery)
        {
            string defaultCollationQuery = MsSqlCollationQuery(type, item, DEFAULT_COLLATION);
            string newCollationQuery = MsSqlCollationQuery(type, item, CASE_SENSITIVE_COLLATION);
            await CapitalizationResultQuery(type, item, dbQuery, defaultCollationQuery, newCollationQuery);
        }

        /// <summary>
        /// Changes collation from a column on a table in the database for MsSql
        /// </summary>
        private static string MsSqlCollationQuery(string table, string column, string newCollation)
        {
            string dbQuery = @"
                ALTER TABLE dbo." + table + @"
                ALTER COLUMN " + column + @" varchar(max) COLLATE " + newCollation; //Check if there is a way to obtain the datatype (varchar(max))
            return dbQuery;
        }
        #endregion
    }
}
