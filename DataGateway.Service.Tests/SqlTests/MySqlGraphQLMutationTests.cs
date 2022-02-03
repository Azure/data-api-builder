using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlGraphQLMutationTests : SqlTestBase
    {

        #region Test Fixture Setup
        private static GraphQLService _graphQLService;
        private static GraphQLController _graphQLController;
        private static readonly string _integrationTableName = "books";

        /// <summary>
        /// Sets up test fixture for class, only to be run once per test run, as defined by
        /// MSTest decorator.
        /// </summary>
        /// <param name="context"></param>
        [ClassInitialize]
        public static async Task InitializeTestFixture(TestContext context)
        {
            await InitializeTestFixture(context, _integrationTableName, TestCategory.MYSQL);

            // Setup GraphQL Components
            _graphQLService = new GraphQLService(_queryEngine, _mutationEngine, _metadataStoreProvider);
            _graphQLController = new GraphQLController(_graphQLService);
        }

        /// <summary>
        /// Runs after every test to reset the database state
        /// </summary>
        [TestCleanup]
        public async Task TestCleanup()
        {
            await ResetDbStateAsync();
        }

        #endregion

        #region  Positive Tests

        /// <summary>
        /// <code>Do: </code> Inserts new book and return its id and title
        /// <code>Check: </code> If book with the expected values of the new book is present in the database and
        /// if the mutation query has returned the correct information
        /// </summary>
        [TestMethod]
        public async Task InsertMutation()
        {
            string graphQLMutationName = "insertBook";
            string graphQLMutation = @"
                mutation {
                    insertBook(title: ""My New Book"", publisher_id: 1234) {
                        id
                        title
                    }
                }
            ";

            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq`.`id`, 'title', `subq`.`title`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`
                    FROM `books` AS `table0`
                    WHERE `id` = 5001
                        AND `title` = 'My New Book'
                        AND `publisher_id` = 1234
                    ORDER BY `id` LIMIT 1
                    ) AS `subq`
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// <code>Do: </code>Update book in database and return its updated fields
        /// <code>Check: </code>if the book with the id of the edited book and the new values exists in the database
        /// and if the mutation query has returned the values correctly
        /// </summary>
        [TestMethod]
        public async Task UpdateMutation()
        {
            string graphQLMutationName = "editBook";
            string graphQLMutation = @"
                mutation {
                    editBook(id: 1, title: ""Even Better Title"", publisher_id: 2345) {
                        title
                        publisher_id
                    }
                }
            ";

            string mySqlQuery = @"
                SELECT JSON_OBJECT('title', `subq2`.`title`, 'publisher_id', `subq2`.`publisher_id`) AS `data`
                FROM (
                    SELECT `table0`.`title` AS `title`,
                        `table0`.`publisher_id` AS `publisher_id`
                    FROM `books` AS `table0`
                    WHERE `table0`.`id` = 1
                    ORDER BY `table0`.`id` LIMIT 1
                    ) AS `subq2`
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// <code>Do: </code>Delete book by id
        /// <code>Check: </code>if the mutation returned result is as expected and if book by that id has been deleted
        /// </summary>
        [TestMethod]
        public async Task DeleteMutation()
        {
            string graphQLMutationName = "deleteBook";
            string graphQLMutation = @"
                mutation {
                    deleteBook(id: 1) {
                        title
                        publisher_id
                    }
                }
            ";

            string mySqlQueryForResult = @"
                SELECT JSON_OBJECT('title', `subq2`.`title`, 'publisher_id', `subq2`.`publisher_id`) AS `data`
                FROM (
                    SELECT `table0`.`title` AS `title`,
                        `table0`.`publisher_id` AS `publisher_id`
                    FROM `books` AS `table0`
                    WHERE `table0`.`id` = 1
                    ORDER BY `table0`.`id` LIMIT 1
                    ) AS `subq2`
            ";

            // query the table before deletion is performed to see if what the mutation
            // returns is correct
            string expected = await GetDatabaseResultAsync(mySqlQueryForResult);
            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);

            string mySqlQueryToVerifyDeletion = @"
                SELECT JSON_OBJECT('count', `subq`.`count`) AS `data`
                FROM (
                    SELECT COUNT(*) AS `count`
                    FROM `books` AS `table0`
                    WHERE `id` = 1
                    ) AS `subq`
            ";

            string dbResponse = await GetDatabaseResultAsync(mySqlQueryToVerifyDeletion);

            using JsonDocument result = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(result.RootElement.GetProperty("count").GetInt64(), 0);
        }

        /// <summary>
        /// <code>Do: </code>run a mutation which mutates a relationship instead of a graphql type
        /// <code>Check: </code>that the insertion of the entry in the appropriate link table was successful
        /// </summary>
        [TestMethod]
        public async Task InsertMutationForNonGraphQLTypeTable()
        {
            string graphQLMutationName = "addAuthorToBook";
            string graphQLMutation = @"
                mutation {
                    addAuthorToBook(author_id: 123, book_id: 2)
                }
            ";

            string mySqlQuery = @"
                SELECT JSON_OBJECT('count', `subq`.`count`) AS DATA
                FROM
                  (SELECT COUNT(*) AS `count`
                   FROM book_author_link
                   WHERE book_id = 2
                     AND author_id = 123) AS subq
            ";

            await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string dbResponse = await GetDatabaseResultAsync(mySqlQuery);

            using JsonDocument result = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(result.RootElement.GetProperty("count").GetInt64(), 1);
        }

        /// <summary>
        /// <code>Do: </code>a new Book insertion and do a nested querying of the returned book
        /// <code>Check: </code>if the result returned from the mutation is correct
        /// </summary>
        [TestMethod]
        public async Task NestedQueryingInMutation()
        {
            string graphQLMutationName = "insertBook";
            string graphQLMutation = @"
                mutation {
                    insertBook(title: ""My New Book"", publisher_id: 1234) {
                        id
                        title
                        publisher {
                            name
                        }
                    }
                }
            ";

            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq4`.`id`, 'title', `subq4`.`title`, 'publisher', JSON_EXTRACT(`subq4`.
                            `publisher`, '$')) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`,
                        `table1_subq`.`data` AS `publisher`
                    FROM `books` AS `table0`
                    LEFT OUTER JOIN LATERAL(SELECT JSON_OBJECT('name', `subq3`.`name`) AS `data` FROM (
                            SELECT `table1`.`name` AS `name`
                            FROM `publishers` AS `table1`
                            WHERE `table0`.`publisher_id` = `table1`.`id`
                            ORDER BY `table1`.`id` LIMIT 1
                            ) AS `subq3`) AS `table1_subq` ON TRUE
                    WHERE `table0`.`id` = 5001
                    ORDER BY `table0`.`id` LIMIT 1
                    ) AS `subq4`
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }
        #endregion

        #region Negative Tests

        /// <summary>
        /// <code>Do: </code>insert a new Book with an invalid foreign key
        /// <code>Check: </code>that GraphQL returns an error and that the book has not actually been added
        /// </summary>
        [TestMethod]
        public async Task InsertWithInvalidForeignKey()
        {
            string graphQLMutationName = "insertBook";
            string graphQLMutation = @"
                mutation {
                    insertBook(title: ""My New Book"", publisher_id: -1) {
                        id
                        title
                    }
                }
            ";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);

            // TODO improve the error message returned by the graphql service to something more useful then smth generic
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString());

            string mySqlQuery = @"
                SELECT JSON_OBJECT('count', `subq`.`count`) AS `data`
                FROM (
                    SELECT COUNT(*) AS `count`
                    FROM `books`
                    WHERE `publisher_id` = - 1
                    ) AS `subq`
            ";

            string dbResponse = await GetDatabaseResultAsync(mySqlQuery);
            using JsonDocument dbResponseJson = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(dbResponseJson.RootElement.GetProperty("count").GetInt64(), 0);
        }

        /// <summary>
        /// <code>Do: </code>edit a book with an invalid foreign key
        /// <code>Check: </code>that GraphQL returns an error and the book has not been editted
        /// </summary>
        [TestMethod]
        public async Task UpdateWithInvalidForeignKey()
        {
            string graphQLMutationName = "editBook";
            string graphQLMutation = @"
                mutation {
                    editBook(id: 1, publisher_id: -1) {
                        id
                        title
                    }
                }
            ";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);

            // TODO improve the error message returned by the graphql service to something more useful then smth generic
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString());

            string mySqlQuery = @"
                SELECT JSON_OBJECT('count', `subq`.`count`) AS `data`
                FROM (
                    SELECT COUNT(*) AS `count`
                    FROM `books`
                    WHERE `id` = 1
                        AND `publisher_id` = - 1
                    ) AS `subq`
            ";

            string dbResponse = await GetDatabaseResultAsync(mySqlQuery);
            using JsonDocument dbResponseJson = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(dbResponseJson.RootElement.GetProperty("count").GetInt64(), 0);
        }

        /// <summary>
        /// <code>Do: </code>use an update mutation without passing any of the optional new values to update
        /// <code>Check: </code>check that GraphQL returns the appropriate message to the user
        /// </summary>
        [TestMethod]
        public async Task UpdateWithNoNewValues()
        {
            string graphQLMutationName = "editBook";
            string graphQLMutation = @"
                mutation {
                    editBook(id: 1) {
                        id
                        title
                    }
                }
            ";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DatagatewayException.SubStatusCodes.BadRequest}");
        }

        /// <summary>
        /// <code>Do: </code>use an update mutation with an invalid id to update
        /// <code>Check: </code>check that GraphQL returns an appropriate exception to the user
        /// </summary>
        [TestMethod]
        public async Task UpdateWithInvalidIdentifier()
        {
            string graphQLMutationName = "editBook";
            string graphQLMutation = @"
                mutation {
                    editBook(id: -1, title: ""Even Better Title"") {
                        id
                        title
                    }
                }
            ";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DatagatewayException.SubStatusCodes.EntityNotFound}");
        }
        #endregion
    }
}
