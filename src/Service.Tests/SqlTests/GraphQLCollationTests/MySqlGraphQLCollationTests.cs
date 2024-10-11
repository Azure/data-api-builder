// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLCollationTests
{

    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlGraphQLCollationTests : GraphQLCollationTestBase
    {
        //Collation setting for database
        private const string DEFAULT_COLLATION = "utf8mb4_0900_ai_ci";
        private const string CASE_SENSITIVE_COLLATION = "utf8mb4_0900_as_cs";

        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MYSQL;
            await InitializeTestFixture();
        }

        #region Tests
        /// <summary>
        /// MySql Collation Tests to ensure that GraphQL is working properly when there is a change in case sensitivity on the database
        /// </summary>
        [DataTestMethod]
        [DataRow("comics", "title", @"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('title', `subq1`.`title`)), '[]') AS `data` FROM ( SELECT `table0`.`title` AS `title` FROM `comics` AS `table0` WHERE 1 = 1 ORDER BY `table0`.`title` ASC LIMIT 100 ) AS `subq1`")]
        [DataRow("authors", "name", @"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('name', `subq1`.`name`)), '[]') AS `data` FROM ( SELECT `table0`.`name` AS `name` FROM `authors` AS `table0` WHERE 1 = 1 ORDER BY `table0`.`name` ASC LIMIT 100 ) AS `subq1`")]
        [DataRow("fungi", "habitat", @"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('habitat', `subq1`.`habitat`)), '[]') AS `data` FROM ( SELECT `table0`.`habitat` AS `habitat` FROM `fungi` AS `table0` WHERE 1 = 1 ORDER BY `table0`.`habitat` ASC LIMIT 100 ) AS `subq1`")]

        public async Task MySqlCaseSensitiveResultQuery(string objectType, string fieldName, string dbQuery)
        {
            string defaultCollationQuery = MySqlCollationQuery(objectType, fieldName, DEFAULT_COLLATION);
            string newCollationQuery = MySqlCollationQuery(objectType, fieldName, CASE_SENSITIVE_COLLATION);
            await TestQueryingWithCaseSensitiveCollation(objectType, fieldName, dbQuery, defaultCollationQuery, newCollationQuery);
        }

        /// <summary>
        /// Creates collation query for a specific column on a table in the database for MySql
        /// </summary>
        private static string MySqlCollationQuery(string table, string column, string newCollation)
        {
            string dbQuery = @"
                ALTER TABLE " + table + @"
                MODIFY COLUMN " + column + @" text
                CHARACTER SET utf8mb4 COLLATE " + newCollation;
            return dbQuery;
        }
        #endregion
    }
}
