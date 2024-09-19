// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLCollationTests
{

    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlGraphQLCollationTests : GraphQLCollationTestBase
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

        #region Tests
        /// <summary>
        /// MySql Capitalization Collation Tests
        /// </summary>
        [DataTestMethod]
        [DataRow("books", "title", @"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('title', `subq1`.`title`)), '[]') AS `data` FROM ( SELECT `table0`.`title` AS `TITLE` FROM `books` AS `table0` WHERE 1 = 1 ORDER BY `table0`.`title` ASC LIMIT 100 ) AS `subq1`")]
        [DataRow("books", "title", @"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('title', `subq1`.`TITLE`)), '[]') AS `DATA` FROM ( SELECT `table0`.`TITLE` AS `TITLE` FROM `books` AS `table0` WHERE 1 = 1 ORDER BY `table0`.`TITLE` ASC LIMIT 100 ) AS `subq1`")]
        [DataRow("books", "title", @"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('title', `subq1`.`Title`)), '[]') AS `Data` FROM ( SELECT `table0`.`Title` AS `Title` FROM `Books` AS `table0` WHERE 1 = 1 ORDER BY `table0`.`Title` ASC LIMIT 100 ) AS `subq1`")]

        public async Task MySqlCapitalizationResultQuery(string type, string item, string dbQuery)
        {
            await CapitalizationResultQuery(type, item, dbQuery);
        }

        #endregion
    }
}
