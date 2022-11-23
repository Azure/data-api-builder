using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLQueryTests
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlGraphQLQueryTests : GraphQLQueryTestBase
    {
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MYSQL;
            await InitializeTestFixture(context);
        }

        #region Tests

        [TestMethod]
        public async Task MultipleResultQuery()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`, 'title', `subq1`.`title`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `id`,
                          `table0`.`title` AS `title`
                   FROM `books` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`id` asc
                   LIMIT 100) AS `subq1`";

            await MultipleResultQuery(mySqlQuery);
        }

        [TestMethod]
        public async Task MultipleResultQueryWithVariables()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`, 'title', `subq1`.`title`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `id`,
                          `table0`.`title` AS `title`
                   FROM `books` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`id` asc
                   LIMIT 100) AS `subq1`";

            await MultipleResultQueryWithVariables(mySqlQuery);
        }

        /// <summary>
        /// Test One-To-One relationship both directions
        /// (book -> website placement, website placememnt -> book)
        /// <summary>
        [TestMethod]
        public async Task OneToOneJoinQuery()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq11`.`id`, 'websiteplacement', `subq11`.`websiteplacement`)
                       AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table1_subq`.`data` AS `websiteplacement`
                    FROM `books` AS `table0`
                    LEFT OUTER JOIN LATERAL(SELECT JSON_OBJECT('id', `subq10`.`id`, 'price', `subq10`.`price`, 'books',
                                `subq10`.`books`) AS `data` FROM (
                            SELECT `table1`.`id` AS `id`,
                                `table1`.`price` AS `price`,
                                `table2_subq`.`data` AS `books`
                            FROM `book_website_placements` AS `table1`
                            LEFT OUTER JOIN LATERAL(SELECT JSON_OBJECT('id', `subq9`.`id`) AS `data` FROM (
                                    SELECT `table2`.`id` AS `id`
                                    FROM `books` AS `table2`
                                    WHERE `table1`.`book_id` = `table2`.`id`
                                    ORDER BY `table2`.`id` asc LIMIT 1
                                    ) AS `subq9`) AS `table2_subq` ON TRUE
                            WHERE `table0`.`id` = `table1`.`book_id`
                            ORDER BY `table1`.`id` asc LIMIT 1
                            ) AS `subq10`) AS `table1_subq` ON TRUE
                    WHERE `table0`.`id` = 1
                    ORDER BY `table0`.`id` asc LIMIT 100
                    ) AS `subq11`
            ";

            await OneToOneJoinQuery(mySqlQuery);
        }

        [TestMethod]
        public async Task QueryWithSingleColumnPrimaryKey()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('title', `subq2`.`title`) AS `data`
                FROM
                  (SELECT `table0`.`title` AS `title`
                   FROM `books` AS `table0`
                   WHERE `table0`.`id` = 2
                   ORDER BY `table0`.`id` asc
                   LIMIT 1) AS `subq2`
            ";

            await QueryWithSingleColumnPrimaryKey(mySqlQuery);
        }

        [TestMethod]
        public async Task QueryWithMultipleColumnPrimaryKey()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('content', `subq3`.`content`) AS `data`
                FROM (
                    SELECT `table0`.`content` AS `content`
                    FROM `reviews` AS `table0`
                    WHERE `table0`.`id` = 568
                        AND `table0`.`book_id` = 1
                    ORDER BY `table0`.`book_id` asc,
                        `table0`.`id` asc LIMIT 1
                    ) AS `subq3`
            ";

            await QueryWithMultipleColumnPrimaryKey(mySqlQuery);
        }

        [TestMethod]
        public async Task QueryWithNullableForeignKey()
        {
            string mySqlQuery = @"
                SELECT
                  JSON_OBJECT(
                    'title', `subq7`.`title`, 'myseries',
                    `subq7`.`series`
                  ) AS `data`
                FROM
                  (
                    SELECT
                      `table0`.`title` AS `title`,
                      `table1_subq`.`data` AS `series`
                    FROM
                      `comics` AS `table0`
                      LEFT OUTER JOIN LATERAL (
                        SELECT
                          JSON_OBJECT('name', `subq6`.`name`) AS `data`
                        FROM
                          (
                            SELECT
                              `table1`.`name` AS `name`
                            FROM
                              `series` AS `table1`
                            WHERE
                              `table0`.`series_id` = `table1`.`id`
                            ORDER BY
                              `table1`.`id` ASC
                            LIMIT
                              1
                          ) AS `subq6`
                      ) AS `table1_subq` ON TRUE
                    WHERE
                      `table0`.`id` = 1
                    ORDER BY
                      `table0`.`id` ASC
                    LIMIT
                      1
                  ) AS `subq7`";

            await QueryWithNullableForeignKey(mySqlQuery);
        }

        /// <summary>
        /// Get all instances of a type with nullable interger fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableIntFields()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`, 'title', `subq1`.`title`, 'issue_number',
                                `subq1`.`issue_number`)), JSON_ARRAY()) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`,
                        `table0`.`issue_number` AS `issue_number`
                    FROM `magazines` AS `table0`
                    WHERE 1 = 1
                    ORDER BY `table0`.`id` asc LIMIT 100
                    ) AS `subq1`
            ";

            await TestQueryingTypeWithNullableIntFields(mySqlQuery);
        }

        /// <summary>
        /// Get all instances of a type with nullable string fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableStringFields()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`, 'username', `subq1`.`username`)), JSON_ARRAY()) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`username` AS `username`
                    FROM `website_users` AS `table0`
                    WHERE 1 = 1
                    ORDER BY `table0`.`id` asc LIMIT 100
                    ) AS `subq1`
            ";

            await TestQueryingTypeWithNullableStringFields(mySqlQuery);
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id and book_title are aliases used for corresponding query fields.
        /// The response for the query will contain the alias instead of raw db column..
        /// </summary>
        [TestMethod]
        public async Task TestAliasSupportForGraphQLQueryFields()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('book_id', `subq1`.`book_id`, 'book_title', `subq1`.`book_title`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `book_id`,
                          `table0`.`title` AS `book_title`
                   FROM `books` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`id` asc
                   LIMIT 2) AS `subq1`";

            await TestAliasSupportForGraphQLQueryFields(mySqlQuery);
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id is an alias, while title is the raw db field.
        /// The response for the query will use the alias where it is provided in the query.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForMixOfRawDbFieldFieldAndAlias()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('book_id', `subq1`.`book_id`, 'title', `subq1`.`title`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `book_id`,
                          `table0`.`title` AS `title`
                   FROM `books` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`id` asc
                   LIMIT 2) AS `subq1`";

            await TestSupportForMixOfRawDbFieldFieldAndAlias(mySqlQuery);
        }

        /// <summary>
        /// Tests orderBy on a list query
        /// </summary>
        [TestMethod]
        public async Task TestOrderByInListQuery()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`, 'title', `subq1`.`title`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `id`,
                          `table0`.`title` AS `title`
                   FROM `books` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`title` DESC, `table0`.`id` ASC
                   LIMIT 100) AS `subq1`";

            await TestOrderByInListQuery(mySqlQuery);
        }

        /// <summary>
        /// Use multiple order options and order an entity with a composite pk
        /// </summary>
        [TestMethod]
        public async Task TestOrderByInListQueryOnCompPkType()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`, 'content', `subq1`.`content`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `id`,
                          `table0`.`content` AS `content`
                   FROM `reviews` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`content` ASC, `table0`.`id` DESC, `table0`.`book_id` ASC
                   LIMIT 100) AS `subq1`";

            await TestOrderByInListQueryOnCompPkType(mySqlQuery);
        }

        /// <summary>
        /// Tests null fields in orderBy are ignored
        /// meaning that null pk columns are included in the ORDER BY clause
        /// as ASC by default while null non-pk columns are completely ignored
        /// </summary>
        [TestMethod]
        public async Task TestNullFieldsInOrderByAreIgnored()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`, 'title', `subq1`.`title`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `id`,
                          `table0`.`title` AS `title`
                   FROM `books` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`title` DESC, `table0`.`id` ASC
                   LIMIT 100) AS `subq1`";

            await TestNullFieldsInOrderByAreIgnored(mySqlQuery);
        }

        /// <summary>
        /// Tests that an orderBy with only null fields results in default pk sorting
        /// </summary>
        [TestMethod]
        public async Task TestOrderByWithOnlyNullFieldsDefaultsToPkSorting()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`, 'title', `subq1`.`title`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `id`,
                          `table0`.`title` AS `title`
                   FROM `books` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`id` ASC
                   LIMIT 100) AS `subq1`";

            await TestOrderByWithOnlyNullFieldsDefaultsToPkSorting(mySqlQuery);
        }

        [TestMethod]
        public async Task TestSettingOrderByOrderUsingVariable()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`, 'title', `subq1`.`title`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `id`,
                          `table0`.`title` AS `title`
                   FROM `books` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`id` DESC
                   LIMIT 4) AS `subq1`";

            await TestSettingOrderByOrderUsingVariable(mySqlQuery);
        }

        [TestMethod]
        public async Task TestSettingComplexArgumentUsingVariables()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`, 'title', `subq1`.`title`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `id`,
                          `table0`.`title` AS `title`
                   FROM `books` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`id` ASC
                   LIMIT 100) AS `subq1`";
            await base.TestSettingComplexArgumentUsingVariables(mySqlQuery);
        }

        [TestMethod]
        public async Task TestQueryWithExplicitlyNullArguments()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`, 'title', `subq1`.`title`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `id`,
                          `table0`.`title` AS `title`
                   FROM `books` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`id` asc
                   LIMIT 100) AS `subq1`";

            await TestQueryWithExplicitlyNullArguments(mySqlQuery);
        }

        [TestMethod]
        public async Task TestQueryOnBasicView()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`, 'title', `subq1`.`title`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `id`,
                          `table0`.`title` AS `title`
                   FROM `books_view_all` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`id`
                   LIMIT 5) AS `subq1`";

            await TestQueryOnBasicView(mySqlQuery);
        }

        [TestMethod]
        public async Task TestQueryOnCompositeView()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`, 'name', `subq1`.`name`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `id`,
                          `table0`.`name` AS `name`
                   FROM `books_publishers_view_composite` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`id`
                   LIMIT 5) AS `subq1`";

            await TestQueryOnCompositeView(mySqlQuery);
        }

        #endregion
    }
}
