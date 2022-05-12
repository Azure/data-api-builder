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

    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGraphQLMutationTests : SqlTestBase
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
            await InitializeTestFixture(context, TestCategory.MSSQL);

            // Setup GraphQL Components
            _graphQLService = new GraphQLService(_queryEngine, _mutationEngine, _metadataStoreProvider, new DocumentCache(), new Sha256DocumentHashProvider());
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

            string msSqlQuery = @"
                SELECT TOP 1 [table0].[id] AS [id],
                    [table0].[title] AS [title]
                FROM [books] AS [table0]
                WHERE [table0].[id] = 5001
                    AND [table0].[title] = 'My New Book'
                    AND [table0].[publisher_id] = 1234
                ORDER BY [id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

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

            string msSqlQuery = @"
                SELECT TOP 1 [table0].[id] AS [id],
                    [table0].[content] AS [content]
                FROM [reviews] AS [table0]
                WHERE [table0].[id] = 5001
                    AND [table0].[content] = 'Its a classic'
                    AND [table0].[book_id] = 1
                ORDER BY [id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

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

            string msSqlQuery = @"
                SELECT TOP 1 [title],
                    [publisher_id]
                FROM [books]
                WHERE [books].[id] = 1
                    AND [books].[title] = 'Even Better Title'
                    AND [books].[publisher_id] = 2345
                ORDER BY [books].[id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

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

            string msSqlQueryForResult = @"
                SELECT TOP 1 [title],
                    [publisher_id]
                FROM [books]
                WHERE [books].[id] = 1
                ORDER BY [books].[id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            // query the table before deletion is performed to see if what the mutation
            // returns is correct
            string expected = await GetDatabaseResultAsync(msSqlQueryForResult);
            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual);

            string msSqlQueryToVerifyDeletion = @"
                SELECT COUNT(*) AS count
                FROM [books]
                WHERE [id] = 1
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string dbResponse = await GetDatabaseResultAsync(msSqlQueryToVerifyDeletion);

            using JsonDocument result = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(result.RootElement.GetProperty("count").GetInt64(), 0);
        }

        /// <summary>
        /// <code>Do: </code>run a mutation which mutates a relationship instead of a graphql type
        /// <code>Check: </code>that the insertion of the entry in the appropriate link table was successful
        /// </summary>
        [TestMethod]
        // IGNORE FOR NOW, SEE: Issue #285
        [Ignore]
        public async Task InsertMutationForNonGraphQLTypeTable()
        {
            string graphQLMutationName = "addAuthorToBook";
            string graphQLMutation = @"
                mutation {
                    addAuthorToBook(author_id: 123, book_id: 2)
                }
            ";

            string msSqlQuery = @"
                SELECT COUNT(*) AS count
                FROM [book_author_link]
                WHERE [book_author_link].[book_id] = 2
                    AND [book_author_link].[author_id] = 123
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string dbResponse = await GetDatabaseResultAsync(msSqlQuery);

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

            string msSqlQuery = @"
                SELECT TOP 1 [table0].[id] AS [id],
                    [table0].[title] AS [title],
                    JSON_QUERY([table1_subq].[data]) AS [publisher]
                FROM [books] AS [table0]
                OUTER APPLY (
                    SELECT TOP 1 [table1].[name] AS [name]
                    FROM [publishers] AS [table1]
                    WHERE [table0].[publisher_id] = [table1].[id]
                    ORDER BY [id]
                    FOR JSON PATH,
                        INCLUDE_NULL_VALUES,
                        WITHOUT_ARRAY_WRAPPER
                    ) AS [table1_subq]([data])
                WHERE [table0].[id] = 5001
                    AND [table0].[title] = 'My New Book'
                    AND [table0].[publisher_id] = 1234
                ORDER BY [id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

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

            string msSqlQuery = @"
                SELECT TOP 1 [id],
                    [title],
                    [issue_number]
                FROM [foo].[magazines]
                WHERE [foo].[magazines].[id] = 800
                    AND [foo].[magazines].[title] = 'New Magazine'
                    AND [foo].[magazines].[issue_number] IS NULL
                ORDER BY [foo].[magazines].[id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

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

            string msSqlQuery = @"
                SELECT TOP 1 [id],
                    [title],
                    [issue_number]
                FROM [foo].[magazines]
                WHERE [foo].[magazines].[id] = 801
                    AND [foo].[magazines].[title] = 'New Magazine 2'
                    AND [foo].[magazines].[issue_number] IS NULL
                ORDER BY [foo].[magazines].[id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

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

            string msSqlQuery = @"
                SELECT TOP 1 [id],
                    [issue_number]
                FROM [foo].[magazines]
                WHERE [foo].[magazines].[id] = 1
                    AND [foo].[magazines].[issue_number] IS NULL
                ORDER BY [foo].[magazines].[id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

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

            string msSqlQuery = @"
                SELECT TOP 1 [id],
                    [title],
                    [issue_number]
                FROM [foo].[magazines]
                WHERE [foo].[magazines].[id] = 1
                    AND [foo].[magazines].[title] = 'Newest Magazine'
                    AND [foo].[magazines].[issue_number] = 1234
                ORDER BY [foo].[magazines].[id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

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

            string msSqlQuery = @"
                SELECT TOP 1 [table0].[id] AS [book_id],
                    [table0].[title] AS [book_title]
                FROM [books] AS [table0]
                WHERE [table0].[id] = 5001
                    AND [table0].[title] = 'My New Book'
                    AND [table0].[publisher_id] = 1234
                ORDER BY [id]
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string actual = await GetGraphQLResultAsync(graphQLMutation, graphQLMutationName, _graphQLController);
            string expected = await GetDatabaseResultAsync(msSqlQuery);

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
                message: DbExceptionParserBase.GENERIC_DB_EXCEPTION_MESSAGE,
                statusCode: $"{DataGatewayException.SubStatusCodes.DatabaseOperationFailed}"
            );

            string msSqlQuery = @"
                SELECT COUNT(*) AS count
                FROM [books]
                WHERE [books].[publisher_id] = - 1
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string dbResponse = await GetDatabaseResultAsync(msSqlQuery);
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
                message: DbExceptionParserBase.GENERIC_DB_EXCEPTION_MESSAGE,
                statusCode: $"{DataGatewayException.SubStatusCodes.DatabaseOperationFailed}"
            );

            string msSqlQuery = @"
                SELECT COUNT(*) AS count
                FROM [books]
                WHERE [books].[id] = 1
                    AND [books].[publisher_id] = - 1
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string dbResponse = await GetDatabaseResultAsync(msSqlQuery);
            using JsonDocument dbResponseJson = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(dbResponseJson.RootElement.GetProperty("count").GetInt64(), 0);
        }

        /// <summary>
        /// <code>Do: </code>use an update mutation without passing any of the optional new values to update
        /// <code>Check: </code>check that GraphQL returns an appropriate exception to the user
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
                message: DbExceptionParserBase.GENERIC_DB_EXCEPTION_MESSAGE,
                statusCode: $"{DataGatewayException.SubStatusCodes.DatabaseOperationFailed}"
            );
        }
        #endregion
    }
}
