using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MYSqlGraphQLQueryTests : GraphQLQueryTestBase
    {

        #region Test Fixture Setup
        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task InitializeTestFixture(TestContext context)
        {
            await InitializeTestFixture(context, TestCategory.MYSQL);

            // Setup GraphQL Components
            _graphQLService = new GraphQLService(
                _runtimeConfigPath,
                _queryEngine,
                _mutationEngine,
                new DocumentCache(),
                new Sha256DocumentHashProvider(),
                _sqlMetadataProvider);
            _graphQLController = new GraphQLController(_graphQLService);
        }

        #endregion

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
                   ORDER BY `table0`.`id`
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
                   ORDER BY `table0`.`id`
                   LIMIT 100) AS `subq1`";

            await MultipleResultQueryWithVariables(mySqlQuery);
        }

        [TestMethod]
        public override async Task MultipleResultJoinQuery()
        {
            await base.MultipleResultJoinQuery();
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
                                    ORDER BY `table2`.`id` LIMIT 1
                                    ) AS `subq9`) AS `table2_subq` ON TRUE
                            WHERE `table0`.`id` = `table1`.`book_id`
                            ORDER BY `table1`.`id` LIMIT 1
                            ) AS `subq10`) AS `table1_subq` ON TRUE
                    WHERE `table0`.`id` = 1
                    ORDER BY `table0`.`id` LIMIT 100
                    ) AS `subq11`
            ";

            await OneToOneJoinQuery(mySqlQuery);
        }

        /// <summary>
        /// This deeply nests a many-to-one/one-to-many join multiple times to
        /// show that it still results in a valid query.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public override async Task DeeplyNestedManyToOneJoinQuery()
        {
            await base.DeeplyNestedManyToOneJoinQuery();
        }

        /// <summary>
        /// This deeply nests a many-to-many join multiple times to show that
        /// it still results in a valid query.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public override async Task DeeplyNestedManyToManyJoinQuery()
        {
            await base.DeeplyNestedManyToManyJoinQuery();
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
                   ORDER BY `table0`.`id`
                   LIMIT 1) AS `subq2`
            ";

            await QueryWithSingleColumnPrimaryKey(mySqlQuery);
        }

        [TestMethod]
        public async Task QueryWithMultileColumnPrimaryKey()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('content', `subq3`.`content`) AS `data`
                FROM (
                    SELECT `table0`.`content` AS `content`
                    FROM `reviews` AS `table0`
                    WHERE `table0`.`id` = 568
                        AND `table0`.`book_id` = 1
                    ORDER BY `table0`.`book_id`,
                        `table0`.`id` LIMIT 1
                    ) AS `subq3`
            ";

            await QueryWithMultileColumnPrimaryKey(mySqlQuery);
        }

        [TestMethod]
        public override async Task QueryWithNullResult()
        {
            await base.QueryWithNullResult();
        }

        /// <sumary>
        /// Test if first param successfully limits list quries
        /// </summary>
        [TestMethod]
        public override async Task TestFirstParamForListQueries()
        {
            await base.TestFirstParamForListQueries();
        }

        /// <sumary>
        /// Test if filter param successfully filters the query results
        /// </summary>
        [TestMethod]
        public override async Task TestFilterParamForListQueries()
        {
            await base.TestFilterParamForListQueries();
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
                    ORDER BY `table0`.`id` LIMIT 100
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
                    ORDER BY `table0`.`id` LIMIT 100
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
                   ORDER BY `table0`.`id`
                   LIMIT 2) AS `subq1`";

            await base.TestAliasSupportForGraphQLQueryFields(mySqlQuery);
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
                   ORDER BY `table0`.`id`
                   LIMIT 2) AS `subq1`";

            await TestSupportForMixOfRawDbFieldFieldAndAlias(mySqlQuery);
        }

        /// <summary>
        /// Tests orderBy on a list query
        /// </summary>
        [Ignore]
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
        [Ignore]
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
        [Ignore]
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
        [Ignore]
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

        #endregion

        #region Negative Tests

        [TestMethod]
        public override async Task TestInvalidFirstParamQuery()
        {
            await base.TestInvalidFilterParamQuery();
        }

        [TestMethod]
        public override async Task TestInvalidFilterParamQuery()
        {
            await base.TestInvalidFilterParamQuery();
        }

        #endregion
    }
}
