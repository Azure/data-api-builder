using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Exceptions;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass, TestCategory(TestCategory.MYSQL)]
    public class MySqlGraphQLMutationTests : SqlTestBase
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
            _graphQLService = new GraphQLService(
                _runtimeConfigPath,
                _queryEngine,
                _mutationEngine,
                _metadataStoreProvider,
                new DocumentCache(),
                new Sha256DocumentHashProvider(),
                _sqlMetadataProvider);
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
        /// <code>Do: </code> Inserts new review with default content for a Review and return its id and content
        /// <code>Check: </code> If book with the given id is present in the database then
        /// the mutation query will return the review Id with the content of the review added
        /// </summary>
        [TestMethod]
        public async Task InsertMutationForConstantdefaultValue()
        {
            string graphQLMutationName = "insertReview";
            string graphQLMutation = @"
                mutation {
                    insertReview(book_id: 1) {
                        id
                        content
                    }
                }
            ";

            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq`.`id`, 'content', `subq`.`content`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`content` AS `content`
                    FROM `reviews` AS `table0`
                    WHERE `id` = 5001
                        AND `content` = 'Its a classic'
                        AND `book_id` = 1
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

        /// <summary>
        /// Test explicitly inserting a null column
        /// </summary>
        [TestMethod]
        public async Task TestExplicitNullInsert()
        {
            string graphQLMutationName = "insertMagazine";
            string graphQLMutation = @"
                mutation {
                    insertMagazine(id: 800, title: ""New Magazine"", issue_number: null) {
                        id
                        title
                        issue_number
                    }
                }
            ";

            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq2`.`id`, 'title', `subq2`.`title`, 'issue_number', `subq2`.`issue_number`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`,
                        `table0`.`issue_number` AS `issue_number`
                    FROM `magazines` AS `table0`
                    WHERE `table0`.`id` = 800
                        AND `table0`.`title` = 'New Magazine'
                        AND `table0`.`issue_number` IS NULL
                    ORDER BY `table0`.`id` LIMIT 1
                    ) AS `subq2`
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test implicitly inserting a null column
        /// </summary>
        [TestMethod]
        public async Task TestImplicitNullInsert()
        {
            string graphQLMutationName = "insertMagazine";
            string graphQLMutation = @"
                mutation {
                    insertMagazine(id: 801, title: ""New Magazine 2"") {
                        id
                        title
                        issue_number
                    }
                }
            ";

            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq2`.`id`, 'title', `subq2`.`title`, 'issue_number', `subq2`.`issue_number`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`,
                        `table0`.`issue_number` AS `issue_number`
                    FROM `magazines` AS `table0`
                    WHERE `table0`.`id` = 801
                        AND `table0`.`title` = 'New Magazine 2'
                        AND `table0`.`issue_number` IS NULL
                    ORDER BY `table0`.`id` LIMIT 1
                    ) AS `subq2`
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test updating a column to null
        /// </summary>
        [TestMethod]
        public async Task TestUpdateColumnToNull()
        {
            string graphQLMutationName = "updateMagazine";
            string graphQLMutation = @"
                mutation {
                    updateMagazine(id: 1, issue_number: null) {
                        id
                        issue_number
                    }
                }
            ";

            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq2`.`id`, 'issue_number', `subq2`.`issue_number`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`issue_number` AS `issue_number`
                    FROM `magazines` AS `table0`
                    WHERE `table0`.`id` = 1
                        AND `table0`.`issue_number` IS NULL
                    ORDER BY `table0`.`id` LIMIT 1
                    ) AS `subq2`
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test updating a missing column in the update mutation will not be updated to null
        /// </summary>
        [TestMethod]
        public async Task TestMissingColumnNotUpdatedToNull()
        {
            string graphQLMutationName = "updateMagazine";
            string graphQLMutation = @"
                mutation {
                    updateMagazine(id: 1, title: ""Newest Magazine"") {
                        id
                        title
                        issue_number
                    }
                }
            ";

            string mySqlQuery = @"
                SELECT JSON_OBJECT('id', `subq2`.`id`, 'title', `subq2`.`title`, 'issue_number', `subq2`.`issue_number`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `id`,
                        `table0`.`title` AS `title`,
                        `table0`.`issue_number` AS `issue_number`
                    FROM `magazines` AS `table0`
                    WHERE `table0`.`id` = 1
                        AND `table0`.`title` = 'Newest Magazine'
                        AND `table0`.`issue_number` = 1234
                    ORDER BY `table0`.`id` LIMIT 1
                    ) AS `subq2`
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(mySqlQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id and book_title are aliases used for corresponding query fields.
        /// The response for the query will use the alias instead of raw db column.
        /// </summary>
        [TestMethod]
        public async Task TestAliasSupportForGraphQLMutationQueryFields()
        {
            string graphQLMutationName = "insertBook";
            string graphQLMutation = @"
                mutation {
                    insertBook(title: ""My New Book"", publisher_id: 1234) {
                        book_id: id
                        book_title: title
                    }
                }
            ";

            string mySqlQuery = @"
                SELECT JSON_OBJECT('book_id', `subq`.`book_id`, 'book_title', `subq`.`book_title`) AS `data`
                FROM (
                    SELECT `table0`.`id` AS `book_id`,
                        `table0`.`title` AS `book_title`
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

            SqlTestHelper.TestForErrorInGraphQLResponse(
                result.ToString(),
                message: MySqlDbExceptionParser.INTEGRITY_CONSTRAINT_VIOLATION_MESSAGE,
                statusCode: $"{DataGatewayException.SubStatusCodes.DatabaseOperationFailed}"
            );

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

            SqlTestHelper.TestForErrorInGraphQLResponse(
                result.ToString(),
                message: MySqlDbExceptionParser.INTEGRITY_CONSTRAINT_VIOLATION_MESSAGE,
                statusCode: $"{DataGatewayException.SubStatusCodes.DatabaseOperationFailed}"
            );

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
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.BadRequest}");
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
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataGatewayException.SubStatusCodes.EntityNotFound}");
        }

        /// <summary>
        /// Test adding a website placement to a book which already has a website
        /// placement
        /// </summary>
        [TestMethod]
        public async Task TestViolatingOneToOneRelashionShip()
        {
            string graphQLMutationName = "insertWebsitePlacement";
            string graphQLMutation = @"
                mutation {
                    insertWebsitePlacement(book_id: 1, price: 25) {
                        id
                    }
                }
            ";

            JsonElement result = await GetGraphQLControllerResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            SqlTestHelper.TestForErrorInGraphQLResponse(
                result.ToString(),
                message: MySqlDbExceptionParser.INTEGRITY_CONSTRAINT_VIOLATION_MESSAGE,
                statusCode: $"{DataGatewayException.SubStatusCodes.DatabaseOperationFailed}"
            );
        }
        #endregion
    }
}
