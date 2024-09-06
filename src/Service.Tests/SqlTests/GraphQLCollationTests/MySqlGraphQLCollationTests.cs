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
        [DataRow("books", "ASC", "title", @"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('title', `subq1`.`title`)), '[]') AS `data` FROM ( SELECT `table0`.`title` AS `title` FROM `books` AS `table0` WHERE 1 = 1 ORDER BY `table0`.`title` ASC, `table0`.`id` ASC LIMIT 100 ) AS `subq1`")]
        [DataRow("books", "DESC", "title", @"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('title', `subq1`.`title`)), '[]') AS `data` FROM ( SELECT `table0`.`title` AS `title` FROM `books` AS `table0` WHERE 1 = 1 ORDER BY `table0`.`title` DESC, `table0`.`id` ASC LIMIT 100 ) AS `subq1`")]
        [DataRow("authors", "ASC", "name", @"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('name', `subq1`.`name`)), '[]') AS `data` FROM ( SELECT `table0`.`name` AS `name` FROM `authors` AS `table0` WHERE 1 = 1 ORDER BY `table0`.`name` ASC, `table0`.`id` ASC LIMIT 100 ) AS `subq1`")]
        [DataRow("authors", "DESC", "name", @"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('name', `subq1`.`name`)), '[]') AS `data` FROM ( SELECT `table0`.`name` AS `name` FROM `authors` AS `table0` WHERE 1 = 1 ORDER BY `table0`.`name` DESC, `table0`.`id` ASC LIMIT 100 ) AS `subq1`")]
        [DataRow("shrubs", "ASC", "fancyName", @"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('fancyName', `subq1`.`fancyName`)), '[]') AS `data` FROM ( SELECT `table0`.`species` AS `fancyName` FROM `trees` AS `table0` WHERE 1 = 1 ORDER BY `table0`.`species` ASC, `table0`.`treeId` ASC LIMIT 100 ) AS `subq1`")]
        [DataRow("shrubs", "DESC", "fancyName", @"SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('fancyName', `subq1`.`fancyName`)), '[]') AS `data` FROM ( SELECT `table0`.`species` AS `fancyName` FROM `trees` AS `table0` WHERE 1 = 1 ORDER BY `table0`.`species` DESC, `table0`.`treeId` ASC LIMIT 100 ) AS `subq1`")]
        public async Task MyCapitalizationResultQuery(string type, string order, string item, string dbQuery)
        {
            await CapitalizationResultQuery(type, order, item, dbQuery);
        }

        #endregion
    }
}
