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
        /// MsSql Collation Tests to ensure that GraphQL is working properly when there is a change in case sensitivity on the database
        /// </summary>
        [DataTestMethod]
        [DataRow("comics", "title", @"SELECT title FROM comics ORDER BY title ASC FOR JSON PATH, INCLUDE_NULL_VALUES")]
        [DataRow("authors", "name", @"SELECT name FROM authors ORDER BY name ASC FOR JSON PATH, INCLUDE_NULL_VALUES")]
        [DataRow("fungi", "habitat", @"SELECT habitat FROM fungi ORDER BY habitat ASC FOR JSON PATH, INCLUDE_NULL_VALUES")]
        public async Task MsSqlCaseSensitiveResultQuery(string objectType, string fieldName, string dbQuery)
        {
            string defaultCollationQuery = MsSqlCollationQuery(objectType, fieldName, DEFAULT_COLLATION);
            string newCollationQuery = MsSqlCollationQuery(objectType, fieldName, CASE_SENSITIVE_COLLATION);
            await TestQueryingWithCaseSensitiveCollation(objectType, fieldName, dbQuery, defaultCollationQuery, newCollationQuery);
        }

        /// <summary>
        /// Creates collation query for a specific column on a table in the database for MsSql
        /// </summary>
        private static string MsSqlCollationQuery(string table, string column, string newCollation)
        {
            string dbQuery = @"
                ALTER TABLE dbo." + table + @"
                ALTER COLUMN " + column + @" varchar(max) COLLATE " + newCollation;
            return dbQuery;
        }
        #endregion
    }
}
