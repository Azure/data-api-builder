using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{
    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MYSqlGraphQLQueryTests : SqlTestBase
    {

        #region Test Fixture Setup
        private static GraphQLService _graphQLService;
        private static GraphQLController _graphQLController;

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
            _graphQLService = new GraphQLService(_queryEngine, mutationEngine: null, _metadataStoreProvider, new DocumentCache(), new Sha256DocumentHashProvider());
            _graphQLController = new GraphQLController(_graphQLService);
        }

        #endregion

        #region Tests

        [TestMethod]
        public async Task MultipleResultQuery()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(first: 100) {
                    id
                    title
                }
            }";
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`, 'title', `subq1`.`title`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `id`,
                          `table0`.`title` AS `title`
                   FROM `books` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`id`
                   LIMIT 100) AS `subq1`";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        [TestMethod]
        public async Task MultipleResultQueryWithVariables()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"query ($first: Int!) {
                getBooks(first: $first) {
                    id
                    title
                }
            }";
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`, 'title', `subq1`.`title`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `id`,
                          `table0`.`title` AS `title`
                   FROM `books` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`id`
                   LIMIT 100) AS `subq1`";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController, new() { { "first", 100 } });
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        [TestMethod]
        public async Task MultipleResultJoinQuery()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(first: 100) {
                    id
                    title
                    publisher_id
                    publisher {
                        id
                        name
                    }
                    reviews(first: 100) {
                        id
                        content
                    }
                    authors(first: 100) {
                        id
                        name
                    }
                }
            }";
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq8`.`id`, 'title', `subq8`.`title`, 'publisher_id',
                                `subq8`.`publisher_id`, 'publisher', JSON_EXTRACT(`subq8`.`publisher`, '$'), 'reviews',
                                JSON_EXTRACT(`subq8`.`reviews`, '$'), 'authors', JSON_EXTRACT(`subq8`.`authors`, '$'))),
                        '[]') AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`,
                        `table0`.`publisher_id` AS `publisher_id`,
                        `table1_subq`.`data` AS `publisher`,
                        `table2_subq`.`data` AS `reviews`,
                        `table3_subq`.`data` AS `authors`
                    FROM `books` AS `table0`
                    LEFT OUTER JOIN LATERAL(SELECT JSON_OBJECT('id', `subq5`.`id`, 'name', `subq5`.`name`) AS `data` FROM (
                            SELECT `table1`.`id` AS `id`,
                                `table1`.`name` AS `name`
                            FROM `publishers` AS `table1`
                            WHERE `table0`.`publisher_id` = `table1`.`id`
                            ORDER BY `table1`.`id` LIMIT 1
                            ) AS `subq5`) AS `table1_subq` ON TRUE
                    LEFT OUTER JOIN LATERAL(SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq6`.`id`, 'content',
                                        `subq6`.`content`)), '[]') AS `data` FROM (
                            SELECT `table2`.`id` AS `id`,
                                `table2`.`content` AS `content`
                            FROM `reviews` AS `table2`
                            WHERE `table0`.`id` = `table2`.`book_id`
                            ORDER BY `table2`.`book_id`,
                                `table2`.`id` LIMIT 100
                            ) AS `subq6`) AS `table2_subq` ON TRUE
                    LEFT OUTER JOIN LATERAL(SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq7`.`id`, 'name', `subq7`
                                        .`name`)), '[]') AS `data` FROM (
                            SELECT `table3`.`id` AS `id`,
                                `table3`.`name` AS `name`
                            FROM `authors` AS `table3`
                            INNER JOIN `book_author_link` AS `table4` ON `table4`.`author_id` = `table3`.`id`
                            WHERE `table0`.`id` = `table4`.`book_id`
                            ORDER BY `table3`.`id` LIMIT 100
                            ) AS `subq7`) AS `table3_subq` ON TRUE
                    WHERE 1 = 1
                    ORDER BY `table0`.`id` LIMIT 100
                    ) AS `subq8`
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test One-To-One relationship both directions
        /// (book -> website placement, website placememnt -> book)
        /// <summary>
        [TestMethod]
        public async Task OneToOneJoinQuery()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"query {
                getBooks {
                    id
                    website_placement {
                        id
                        price
                        book {
                            id
                        }
                    }
                }
            }";

            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq11`.`id`, 'website_placement', `subq11`.`website_placement`)
                        ), JSON_ARRAY()) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table1_subq`.`data` AS `website_placement`
                    FROM `books` AS `table0`
                    LEFT OUTER JOIN LATERAL(SELECT JSON_OBJECT('id', `subq10`.`id`, 'price', `subq10`.`price`, 'book',
                                `subq10`.`book`) AS `data` FROM (
                            SELECT `table1`.`id` AS `id`,
                                `table1`.`price` AS `price`,
                                `table2_subq`.`data` AS `book`
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
                    WHERE 1 = 1
                    ORDER BY `table0`.`id` LIMIT 100
                    ) AS `subq11`
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// This deeply nests a many-to-one/one-to-many join multiple times to
        /// show that it still results in a valid query.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task DeeplyNestedManyToOneJoinQuery()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
              getBooks(first: 100) {
                title
                publisher {
                  name
                  books(first: 100) {
                    title
                    publisher {
                      name
                      books(first: 100) {
                        title
                        publisher {
                          name
                        }
                      }
                    }
                  }
                }
              }
            }";

            string mySqlQuery = @"
            SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('title', `subq11`.`title`, 'publisher', JSON_EXTRACT(`subq11`.
                                `publisher`, '$'))), '[]') AS `data`
            FROM (
                SELECT `table0`.`title` AS `title`,
                    `table1_subq`.`data` AS `publisher`
                FROM `books` AS `table0`
                LEFT OUTER JOIN LATERAL(SELECT JSON_OBJECT('name', `subq10`.`name`, 'books', JSON_EXTRACT(`subq10`.
                                `books`, '$')) AS `data` FROM (
                        SELECT `table1`.`name` AS `name`,
                            `table2_subq`.`data` AS `books`
                        FROM `publishers` AS `table1`
                        LEFT OUTER JOIN LATERAL(SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('title', `subq9`.`title`,
                                            'publisher', JSON_EXTRACT(`subq9`.`publisher`, '$'))), '[]') AS `data`
                                FROM (
                                SELECT `table2`.`title` AS `title`,
                                    `table3_subq`.`data` AS `publisher`
                                FROM `books` AS `table2`
                                LEFT OUTER JOIN LATERAL(SELECT JSON_OBJECT('name', `subq8`.`name`, 'books',
                                            JSON_EXTRACT(`subq8`.`books`, '$')) AS `data` FROM (
                                        SELECT `table3`.`name` AS `name`,
                                            `table4_subq`.`data` AS `books`
                                        FROM `publishers` AS `table3`
                                        LEFT OUTER JOIN LATERAL(SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('title',
                                                            `subq7`.`title`, 'publisher', JSON_EXTRACT(`subq7`.
                                                                `publisher`, '$'))), '[]') AS `data` FROM (
                                                SELECT `table4`.`title` AS `title`,
                                                    `table5_subq`.`data` AS `publisher`
                                                FROM `books` AS `table4`
                                                LEFT OUTER JOIN LATERAL(SELECT JSON_OBJECT('name', `subq6`.`name`)
                                                        AS `data` FROM (
                                                        SELECT `table5`.`name` AS `name`
                                                        FROM `publishers` AS `table5`
                                                        WHERE `table4`.`publisher_id` = `table5`.`id`
                                                        ORDER BY `table5`.`id` LIMIT 1
                                                        ) AS `subq6`) AS `table5_subq` ON TRUE
                                                WHERE `table3`.`id` = `table4`.`publisher_id`
                                                ORDER BY `table4`.`id` LIMIT 100
                                                ) AS `subq7`) AS `table4_subq` ON TRUE
                                        WHERE `table2`.`publisher_id` = `table3`.`id`
                                        ORDER BY `table3`.`id` LIMIT 1
                                        ) AS `subq8`) AS `table3_subq` ON TRUE
                                WHERE `table1`.`id` = `table2`.`publisher_id`
                                ORDER BY `table2`.`id` LIMIT 100
                                ) AS `subq9`) AS `table2_subq` ON TRUE
                        WHERE `table0`.`publisher_id` = `table1`.`id`
                        ORDER BY `table1`.`id` LIMIT 1
                        ) AS `subq10`) AS `table1_subq` ON TRUE
                WHERE 1 = 1
                ORDER BY `table0`.`id` LIMIT 100
                ) AS `subq11`
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// This deeply nests a many-to-many join multiple times to show that
        /// it still results in a valid query.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public async Task DeeplyNestedManyToManyJoinQuery()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
              getBooks(first: 100) {
                title
                authors(first: 100) {
                  name
                  books(first: 100) {
                    title
                    authors(first: 100) {
                      name
                    }
                  }
                }
              }
            }";

            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('title', `subq10`.`title`, 'authors', JSON_EXTRACT(`subq10`.
                                    `authors`, '$'))), '[]') AS `data`
                FROM (
                    SELECT `table0`.`title` AS `title`,
                        `table1_subq`.`data` AS `authors`
                    FROM `books` AS `table0`
                    LEFT OUTER JOIN LATERAL(SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('name', `subq9`.`name`, 'books',
                                        JSON_EXTRACT(`subq9`.`books`, '$'))), '[]') AS `data` FROM (
                            SELECT `table1`.`name` AS `name`,
                                `table2_subq`.`data` AS `books`
                            FROM `authors` AS `table1`
                            INNER JOIN `book_author_link` AS `table6` ON `table6`.`author_id` = `table1`.`id`
                            LEFT OUTER JOIN LATERAL(SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('title', `subq8`.`title`,
                                                'authors', JSON_EXTRACT(`subq8`.`authors`, '$'))), '[]') AS `data` FROM (
                                    SELECT `table2`.`title` AS `title`,
                                        `table3_subq`.`data` AS `authors`
                                    FROM `books` AS `table2`
                                    INNER JOIN `book_author_link` AS `table5` ON `table5`.`book_id` = `table2`.`id`
                                    LEFT OUTER JOIN LATERAL(SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('name', `subq7`.
                                                        `name`)), '[]') AS `data` FROM (
                                            SELECT `table3`.`name` AS `name`
                                            FROM `authors` AS `table3`
                                            INNER JOIN `book_author_link` AS `table4` ON `table4`.`author_id` = `table3`.
                                                `id`
                                            WHERE `table2`.`id` = `table4`.`book_id`
                                            ORDER BY `table3`.`id` LIMIT 100
                                            ) AS `subq7`) AS `table3_subq` ON TRUE
                                    WHERE `table1`.`id` = `table5`.`author_id`
                                    ORDER BY `table2`.`id` LIMIT 100
                                    ) AS `subq8`) AS `table2_subq` ON TRUE
                            WHERE `table0`.`id` = `table6`.`book_id`
                            ORDER BY `table1`.`id` LIMIT 100
                            ) AS `subq9`) AS `table1_subq` ON TRUE
                    WHERE 1 = 1
                    ORDER BY `table0`.`id` LIMIT 100
                    ) AS `subq10`
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        [TestMethod]
        public async Task QueryWithSingleColumnPrimaryKey()
        {
            string graphQLQueryName = "getBook";
            string graphQLQuery = @"{
                getBook(id: 2) {
                    title
                }
            }";
            string mySqlQuery = @"
                SELECT JSON_OBJECT('title', `subq2`.`title`) AS `data`
                FROM
                  (SELECT `table0`.`title` AS `title`
                   FROM `books` AS `table0`
                   WHERE `table0`.`id` = 2
                   ORDER BY `table0`.`id`
                   LIMIT 1) AS `subq2`
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        [TestMethod]
        public async Task QueryWithMultileColumnPrimaryKey()
        {
            string graphQLQueryName = "getReview";
            string graphQLQuery = @"{
                getReview(id: 568, book_id: 1) {
                    content
                }
            }";
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

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        [TestMethod]
        public async Task QueryWithNullResult()
        {
            string graphQLQueryName = "getBook";
            string graphQLQuery = @"{
                getBook(id: -9999) {
                    title
                }
            }";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);

            SqlTestHelper.PerformTestEqualJsonStrings("null", actual);
        }

        /// <sumary>
        /// Test if first param successfully limits list quries
        /// </summary>
        [TestMethod]
        public async Task TestFirstParamForListQueries()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(first: 1) {
                    title
                    publisher {
                        name
                        books(first: 3) {
                            title
                        }
                    }
                }
            }";

            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('title', `subq5`.`title`, 'publisher', JSON_EXTRACT(`subq5`.
                                    `publisher`, '$'))), '[]') AS `data`
                FROM (
                    SELECT `table0`.`title` AS `title`,
                        `table1_subq`.`data` AS `publisher`
                    FROM `books` AS `table0`
                    LEFT OUTER JOIN LATERAL(SELECT JSON_OBJECT('name', `subq4`.`name`, 'books', JSON_EXTRACT(`subq4`.
                                    `books`, '$')) AS `data` FROM (
                            SELECT `table1`.`name` AS `name`,
                                `table2_subq`.`data` AS `books`
                            FROM `publishers` AS `table1`
                            LEFT OUTER JOIN LATERAL(SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('title', `subq3`.`title`)
                                        ), '[]') AS `data` FROM (
                                    SELECT `table2`.`title` AS `title`
                                    FROM `books` AS `table2`
                                    WHERE `table1`.`id` = `table2`.`publisher_id`
                                    ORDER BY `table2`.`id` LIMIT 3
                                    ) AS `subq3`) AS `table2_subq` ON TRUE
                            WHERE `table0`.`publisher_id` = `table1`.`id`
                            ORDER BY `table1`.`id` LIMIT 1
                            ) AS `subq4`) AS `table1_subq` ON TRUE
                    WHERE 1 = 1
                    ORDER BY `table0`.`id` LIMIT 1
                    ) AS `subq5`
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <sumary>
        /// Test if filter and filterOData param successfully filters the query results
        /// </summary>
        [TestMethod]
        public async Task TestFilterAndFilterODataParamForListQueries()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(_filter: {id: {gte: 1} and: [{id: {lte: 4}}]}) {
                    id
                    publisher {
                        books(first: 3, _filterOData: ""id ne 2"") {
                            id
                        }
                    }
                }
            }";

            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(""id"", `subq12`.`id`, ""publisher"", JSON_EXTRACT(`subq12`.
                                    `publisher`, '$'))), '[]') AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table1_subq`.`data` AS `publisher`
                    FROM `books` AS `table0`
                    LEFT OUTER JOIN LATERAL(SELECT JSON_OBJECT(""books"", JSON_EXTRACT(`subq11`.`books`, '$')) AS `data` FROM
                            (
                            SELECT `table2_subq`.`data` AS `books`
                            FROM `publishers` AS `table1`
                            LEFT OUTER JOIN LATERAL(SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(""id"", `subq10`.`id`)),
                                        '[]') AS `data` FROM (
                                    SELECT `table2`.`id` AS `id`
                                    FROM `books` AS `table2`
                                    WHERE (id != 2)
                                        AND `table1`.`id` = `table2`.`publisher_id`
                                    ORDER BY `table2`.`id` LIMIT 3
                                    ) AS `subq10`) AS `table2_subq` ON TRUE
                            WHERE `table0`.`publisher_id` = `table1`.`id`
                            ORDER BY `table1`.`id` LIMIT 1
                            ) AS `subq11`) AS `table1_subq` ON TRUE
                    WHERE (
                            (id >= 1)
                            AND (id <= 4)
                            )
                    ORDER BY `table0`.`id` LIMIT 100
                    ) AS `subq12`
            ";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Get all instances of a type with nullable interger fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableIntFields()
        {
            string graphQLQueryName = "getMagazines";
            string graphQLQuery = @"{
                getMagazines{
                    id
                    title
                    issue_number
                }
            }";

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

            _ = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);

            _ = await GetDatabaseResultAsync(mySqlQuery);
        }

        /// <summary>
        /// Get all instances of a type with nullable string fields
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableStringFields()
        {
            string graphQLQueryName = "getWebsiteUsers";
            string graphQLQuery = @"{
                getWebsiteUsers{
                    id
                    username
                }
            }";

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

            _ = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);

            _ = await GetDatabaseResultAsync(mySqlQuery);
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id and book_title are aliases used for corresponding query fields.
        /// The response for the query will contain the alias instead of raw db column..
        /// </summary>
        [TestMethod]
        public async Task TestAliasSupportForGraphQLQueryFields()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(first: 2) {
                    book_id: id
                    book_title: title
                }
            }";
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('book_id', `subq1`.`book_id`, 'book_title', `subq1`.`book_title`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `book_id`,
                          `table0`.`title` AS `book_title`
                   FROM `books` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`id`
                   LIMIT 2) AS `subq1`";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id is an alias, while title is the raw db field.
        /// The response for the query will use the alias where it is provided in the query.
        /// </summary>
        [TestMethod]
        public async Task TestSupportForMixOfRawDbFieldFieldAndAlias()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(first: 2) {
                    book_id: id
                    title
                }
            }";
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('book_id', `subq1`.`book_id`, 'title', `subq1`.`title`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `book_id`,
                          `table0`.`title` AS `title`
                   FROM `books` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`id`
                   LIMIT 2) AS `subq1`";

            string actual = await GetGraphQLResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        #endregion

        #region Negative Tests

        [TestMethod]
        public async Task TestInvalidFirstParamQuery()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(first: -1) {
                    id
                    title
                }
            }";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.BadRequest}");
        }

        [TestMethod]
        public async Task TestInvalidFilterParamQuery()
        {
            string graphQLQueryName = "getBooks";
            string graphQLQuery = @"{
                getBooks(_filterOData: ""INVALID"") {
                    id
                    title
                }
            }";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLQuery, graphQLQueryName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.BadRequest}");
        }

        #endregion
    }
}
