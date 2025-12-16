// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
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
            await InitializeTestFixture();
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
        public async Task MultipleResultQueryWithMappings()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('column1', `subq1`.`column1`, 'column2', `subq1`.`column2`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`__column1` AS `column1`,
                          `table0`.`__column2` AS `column2`
                   FROM `GQLmappings` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`__column1` asc
                   LIMIT 100) AS `subq1`";

            await MultipleResultQueryWithMappings(mySqlQuery);
        }

        /// <summary>
        /// Gets array of results for querying a table containing computed columns.
        /// </summary>
        /// <check>rows from sales table</check>
        [TestMethod]
        public async Task MultipleResultQueryContainingComputedColumns()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT(
                    'id', `subq1`.`id`,
                    'item_name', `subq1`.`item_name`,
                    'subtotal', `subq1`.`subtotal`,
                    'tax', `subq1`.`tax`,
                    'total', `subq1`.`total`
                    )), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `id`,
                          `table0`.`item_name` AS `item_name`,
                          `table0`.`subtotal` AS `subtotal`,
                          `table0`.`tax` AS `tax`,
                          `table0`.`total` AS `total`
                   FROM `sales` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`id` asc
                   LIMIT 100) AS `subq1`";

            await MultipleResultQueryContainingComputedColumns(mySqlQuery);
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
        /// Tests In operator using query variables
        /// </summary>
        [TestMethod]
        public async Task InQueryWithVariables()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq1`.`id`, 'title', `subq1`.`title`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`id` AS `id`,
                          `table0`.`title` AS `title`
                   FROM `books` AS `table0`
                   WHERE `table0`.`id` IN (1,2)
                   ORDER BY `table0`.`id` asc
                   LIMIT 100) AS `subq1`";

            await InQueryWithVariables(mySqlQuery);
        }

        /// <summary>
        /// Tests In operator with null's and empty values
        /// <checks>Runs an mssql query and then validates that the result from the dwsql query graphql call matches the mssql query result.</checks>
        /// </summary>
        [TestMethod]
        public async Task InQueryWithNullAndEmptyvalues()
        {
            string mySqlQuery = @"SELECT COALESCE(
                            JSON_ARRAYAGG(
                                JSON_OBJECT(
                                    'string_types', `subq7`.`string_types`
                                )
                            ), 
                            JSON_ARRAY()
                        ) AS `data`
                        FROM (
                            SELECT `string_types`
                            FROM `type_table`
                            WHERE (`string_types` IN ('lksa;jdflasdf;alsdflksdfkldj', '', null))
                        ) AS `subq7`;
                        ";
            await InQueryWithNullAndEmptyvalues(mySqlQuery);
        }

        /// <summary>
        /// Test One-To-One relationship both directions
        /// (book -> website placement, website placememnt -> book)
        /// <summary>
        [TestMethod]
        public async Task OneToOneJoinQuery()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq7`.`id`, 'title', `subq7`.`title`, 'websiteplacement',
                                `subq7`.`websiteplacement`)), JSON_ARRAY()) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`,
                        `table1_subq`.`data` AS `websiteplacement`
                    FROM `books` AS `table0`
                    LEFT OUTER JOIN LATERAL(SELECT JSON_OBJECT('price', `subq6`.`price`) AS `data` FROM (
                            SELECT `table1`.`price` AS `price`
                            FROM `book_website_placements` AS `table1`
                            WHERE `table1`.`book_id` = `table0`.`id`
                            ORDER BY `table1`.`id` ASC LIMIT 1
                            ) AS `subq6`) AS `table1_subq` ON TRUE
                    WHERE 1 = 1
                    ORDER BY `table0`.`id` ASC LIMIT 100
                    ) AS `subq7`
            ";

            await OneToOneJoinQuery(mySqlQuery);
        }

        /// <summary>
        /// Test IN filter with One-To-One relationship both directions
        /// (book -> website placement, website placememnt -> book)
        /// <summary>
        [TestMethod]
        public async Task InFilterOneToOneJoinQuery()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('id', `subq7`.`id`, 'title', `subq7`.`title`, 'websiteplacement',
                                `subq7`.`websiteplacement`)), JSON_ARRAY()) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`,
                        `table1_subq`.`data` AS `websiteplacement`
                    FROM `books` AS `table0`
                    LEFT OUTER JOIN LATERAL(SELECT JSON_OBJECT('price', `subq6`.`price`, 'book_id', `subq6`.`book_id`) AS `data` FROM (
                            SELECT `table1`.`price` AS `price`,
                                    `table1`.`book_id` AS `book_id`
                            FROM `book_website_placements` AS `table1`
                            WHERE `table1`.`book_id` = `table0`.`id`
                            ORDER BY `table1`.`id` ASC LIMIT 1
                            ) AS `subq6`) AS `table1_subq` ON TRUE
                    WHERE (
                        `table0`.`title` IN ('Awesome book', 'Also Awesome book')
                    )
                    ORDER BY `table0`.`id` DESC LIMIT 100
                    ) AS `subq7`
            ";
            string graphQLQueryName = "books";
            string graphQLQuery = @"query {
                  books(filter:  {
                     title:  {
                        in: [""Awesome book"", ""Also Awesome book""]
                     }
                  } orderBy:  {
                     id: DESC
                  }){
                    items{
                      id
                      title
                      websiteplacement{
                        price
                        book_id
                      }
                    }
                  }
                }";

            JsonElement actual = await base.ExecuteGraphQLRequestAsync(graphQLQuery, graphQLQueryName, isAuthenticated: false);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.GetProperty("items").ToString());
        }

        /// <summary>
        /// Test query on One-To-One relationship when the fields defining
        /// the relationship in the entity include fields that are mapped in
        /// that same entity.
        /// <summary>
        [TestMethod]
        public async Task OneToOneJoinQueryWithMappedFieldNamesInRelationship()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('fancyName', `subq7`.`fancyName`, 'fungus', `subq7`.`fungus`)), JSON_ARRAY()) AS `data`
                FROM (
                    SELECT `table0`.`species` AS `fancyName`,
                        `table1_subq`.`data` AS `fungus`
                    FROM `trees` AS `table0`
                    LEFT OUTER JOIN LATERAL(SELECT JSON_OBJECT('habitat', `subq6`.`habitat`) AS `data` FROM (
                            SELECT `table1`.`habitat` AS `habitat`
                            FROM `fungi` AS `table1`
                            WHERE `table1`.`habitat` = `table0`.`species`
                            ORDER BY `table1`.`habitat` ASC LIMIT 1
                            ) AS `subq6`) AS `table1_subq` ON TRUE
                    WHERE 1 = 1
                    LIMIT 100
                    ) AS `subq7`
            ";

            await OneToOneJoinQueryWithMappedFieldNamesInRelationship(mySqlQuery);
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
        public async Task QueryWithSingleColumnPrimaryKeyAndMappings()
        {
            string mySqlQuery = @"
                SELECT JSON_OBJECT('column1', `subq3`.`column1`) AS `data`
                FROM (
                    SELECT `table0`.`__column1` AS `column1`
                    FROM `GQLmappings` AS `table0`
                    WHERE `table0`.`__column1` = 1
                    ORDER BY `table0`.`__column1` asc
                    LIMIT 1
                    ) AS `subq3`
            ";

            await QueryWithSingleColumnPrimaryKeyAndMappings(mySqlQuery);
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
        /// Test where data in the db has a nullable datetime field. The query should successfully return the date in the published_date field if present, else return null.
        /// </summary>
        [TestMethod]
        public async Task TestQueryingTypeWithNullableDateTimeFields()
        {
            string mySqlQuery = @"
                SELECT COALESCE(JSON_ARRAYAGG(JSON_OBJECT('datetime_types', `subq1`.`datetime_types`)), '[]') AS `data`
                FROM
                  (SELECT `table0`.`datetime_types` AS `datetime_types`
                   FROM `type_table` AS `table0`
                   WHERE 1 = 1
                   ORDER BY `table0`.`id` asc
                   LIMIT 100) AS `subq1`";

            await TestQueryingTypeWithNullableDateTimeFields(mySqlQuery);
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

        /// <inheritdoc />
        [DataTestMethod]
        [DataRow(null, null, 1113, "Real Madrid", DisplayName = "No Overriding of existing relationship fields in DB.")]
        [DataRow(new string[] { "new_club_id" }, new string[] { "id" }, 1111, "Manchester United", DisplayName = "Overriding existing relationship fields in DB.")]
        public async Task TestConfigTakesPrecedenceForRelationshipFieldsOverDB(
            string[] sourceFields,
            string[] targetFields,
            int club_id,
            string club_name)
        {
            await TestConfigTakesPrecedenceForRelationshipFieldsOverDB(
                sourceFields,
                targetFields,
                club_id,
                club_name,
                DatabaseType.MySQL,
                TestCategory.MYSQL);
        }

        /// <summary>
        /// Test to check GraphQL support for aggregations with aliases.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        [Ignore]
        public async Task TestSupportForAggregationsWithAliases()
        {
            string msSqlQuery = @"
                SELECT 
                    MAX(categoryid) AS max, 
                    MAX(price) AS max_price,
                    MIN(price) AS min_price,
                    AVG(price) AS avg_price,
                    SUM(price) AS sum_price
                FROM stocks_price
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            // Execute the test for the SQL query
            await TestSupportForAggregationsWithAliases(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for aggregations with aliases and groupby.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        [Ignore]
        public async Task TestSupportForGroupByAggregationsWithAliases()
        {
            string msSqlQuery = @"
                SELECT
                    MAX(categoryid) AS max,
                    MAX(price) AS max_price,
                    MIN(price) AS min_price,
                    AVG(price) AS avg_price,
                    SUM(price) AS sum_price,
                    COUNT(categoryid) AS count
                FROM stocks_price
                GROUP BY categoryid
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            // Execute the test for the SQL query
            await TestSupportForGroupByAggregationsWithAliases(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for min aggregations.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        [Ignore]
        public async Task TestSupportForMinAggregation()
        {
            string msSqlQuery = @"
                SELECT
                    MIN(price) AS min_price
                FROM stocks_price
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            // Execute the test for the SQL query
            await TestSupportForMinAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for Max aggregations.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        [Ignore]
        public async Task TestSupportForMaxAggregation()
        {
            string msSqlQuery = @"
                SELECT
                    MAX(price) AS max_price
                FROM stocks_price
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            // Execute the test for the SQL query
            await TestSupportForMaxAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for avg aggregations.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        [Ignore]
        public async Task TestSupportForAvgAggregation()
        {
            string msSqlQuery = @"
                SELECT
                    AVG(price) AS avg_price
                FROM stocks_price
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            // Execute the test for the SQL query
            await TestSupportForAvgAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for sum aggregations.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        [Ignore]
        public async Task TestSupportForSumAggregation()
        {
            string msSqlQuery = @"
                SELECT
                    SUM(price) AS sum_price
                FROM stocks_price
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            // Execute the test for the SQL query
            await TestSupportForSumAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for count aggregations.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        [Ignore]
        public async Task TestSupportForCountAggregation()
        {
            string msSqlQuery = @"
                SELECT
                    COUNT(categoryid) AS count_categoryid
                FROM stocks_price
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            // Execute the test for the SQL query
            await TestSupportForCountAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for having filter.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        [Ignore]
        public async Task TestSupportForHavingAggregation()
        {
            string msSqlQuery = @"
                SELECT
                    SUM(price) AS sum_price
                FROM stocks_price
                HAVING SUM(price) > 50
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            // Execute the test for the SQL query
            await TestSupportForHavingAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for count aggregations.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        [Ignore]
        public async Task TestSupportForGroupByHavingAggregation()
        {
            string msSqlQuery = @"
                SELECT
                    SUM(price) AS sum_price
                FROM stocks_price
                GROUP BY categoryid, pieceid
                HAVING SUM(price) > 50
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            // Execute the test for the SQL query
            await TestSupportForGroupByHavingAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for count aggregations.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        [Ignore]
        public async Task TestSupportForGroupByHavingFieldsAggregation()
        {
            string msSqlQuery = @"
                SELECT
                    categoryid,
                    pieceid,
                    SUM(price) AS sum_price,
                    COUNT(pieceid) AS count_piece
                FROM stocks_price
                GROUP BY categoryid, pieceid
                HAVING SUM(price) > 50 AND COUNT(pieceid) <= 100
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            // Execute the test for the SQL query
            await TestSupportForGroupByHavingFieldsAggregation(msSqlQuery);
        }

        /// <summary>
        /// Test to check GraphQL support for count aggregations.
        /// This test verifies that the SQL query results are correctly mapped to the expected GraphQL format.
        /// </summary>
        [TestMethod]
        [Ignore]
        public async Task TestSupportForGroupByNoAggregation()
        {
            string msSqlQuery = @"
                SELECT
                    categoryid,
                    pieceid
                FROM stocks_price
                GROUP BY categoryid, pieceid
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            // Execute the test for the SQL query
            await TestSupportForGroupByNoAggregation(msSqlQuery);
        }

        [TestMethod]
        [Ignore]
        public override async Task TestNoAggregationOptionsForTableWithoutNumericFields()
        {
            await TestNoAggregationOptionsForTableWithoutNumericFields();
        }
        #endregion
    }
}
