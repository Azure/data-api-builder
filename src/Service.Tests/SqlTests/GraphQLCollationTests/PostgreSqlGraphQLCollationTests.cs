// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLCollationTests
{

    [TestClass, TestCategory(TestCategory.POSTGRESQL)]
    public class PostgreSqlGraphQLCollationTests : GraphQLCollationTestBase
    {
        //Collation setting for database
        private const string DEFAULT_COLLATION = "pg_catalog.\"default\"";
        private const string CASE_INSENSITIVE_COLLATION = "case_insensitive";

        //Queries to create and drop user created collations
        private const string CREATE_CASE_INSENSITIVE_COLLATION = "CREATE COLLATION case_insensitive (provider = icu, locale = 'und-u-ks-level2', deterministic = false)";
        private const string DROP_CASE_INSENSITIVE_COLLATION = "DROP COLLATION case_insensitive";

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
        [DataRow("comics", "title", @"SELECT json_agg(to_jsonb(table0)) FROM (SELECT title FROM comics ORDER BY title asc) as table0")]
        [DataRow("journals", "journalname", @"SELECT json_agg(to_jsonb(table0)) FROM (SELECT journalname FROM journals ORDER BY journals asc) as table0")]
        [DataRow("trees", "species", @"SELECT json_agg(to_jsonb(table0)) FROM (SELECT species FROM trees ORDER BY species asc) as table0")]
        public async Task PostgreCapitalizationResultQuery(string type, string item, string dbQuery)
        {
            PostgreCreateAndDropCollationQuery(CREATE_CASE_INSENSITIVE_COLLATION);
            string defaultCollationQuery = PostgreCollationQuery(type, item, DEFAULT_COLLATION);
            string newCollationQuery = PostgreCollationQuery(type, item, CASE_INSENSITIVE_COLLATION);
            await CapitalizationResultQuery(type, item, dbQuery, defaultCollationQuery, newCollationQuery);
            PostgreCreateAndDropCollationQuery(DROP_CASE_INSENSITIVE_COLLATION);
        }
        private static string PostgreCollationQuery(string table, string column, string newCollation)
        {
            string dbQuery = @"
                ALTER TABLE " + table + @"
                ALTER COLUMN " + column + @" TYPE text
                COLLATE " + newCollation; //How to find text?????????? Should I make utf8mb4 a variable??
            return dbQuery;
        }

        private static async void PostgreCreateAndDropCollationQuery(string collationQuery)
        {
            await GetDatabaseResultAsync(collationQuery);
        }
        #endregion
    }
}
