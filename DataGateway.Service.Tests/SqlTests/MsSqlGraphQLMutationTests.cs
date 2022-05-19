using System.Threading.Tasks;
using Azure.DataGateway.Service.Controllers;
using Azure.DataGateway.Service.Resolvers;
using Azure.DataGateway.Service.Services;
using HotChocolate.Language;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataGateway.Service.Tests.SqlTests
{

    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlGraphQLMutationTests : GraphQLMutationTestBase
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
            await InitializeTestFixture(context, TestCategory.MSSQL);

            // Setup GraphQL Components
            _graphQLService = new GraphQLService(
                _runtimeConfigProvider,
                _queryEngine,
                _mutationEngine,
                graphQLMetadataProvider: null,
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

            await InsertMutation(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code> Inserts new review with default content for a Review and return its id and content
        /// <code>Check: </code> If book with the given id is present in the database then
        /// the mutation query will return the review Id with the content of the review added
        /// </summary>
        [TestMethod]
        public async Task InsertMutationForConstantdefaultValue()
        {
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

            await InsertMutationForConstantdefaultValue(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>Update book in database and return its updated fields
        /// <code>Check: </code>if the book with the id of the edited book and the new values exists in the database
        /// and if the mutation query has returned the values correctly
        /// </summary>
        [TestMethod]
        public async Task UpdateMutation()
        {
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

            await UpdateMutation(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>Delete book by id
        /// <code>Check: </code>if the mutation returned result is as expected and if book by that id has been deleted
        /// </summary>
        [TestMethod]
        public async Task DeleteMutation()
        {
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

            string msSqlQueryToVerifyDeletion = @"
                SELECT COUNT(*) AS count
                FROM [books]
                WHERE [id] = 1
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await DeleteMutation(msSqlQueryForResult, msSqlQueryToVerifyDeletion);
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
            string msSqlQuery = @"
                SELECT COUNT(*) AS count
                FROM [book_author_link]
                WHERE [book_author_link].[book_id] = 2
                    AND [book_author_link].[author_id] = 123
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await InsertMutationForNonGraphQLTypeTable(msSqlQuery);
        }

        /// <summary>
        /// <code>Do: </code>a new Book insertion and do a nested querying of the returned book
        /// <code>Check: </code>if the result returned from the mutation is correct
        /// </summary>
        [TestMethod]
        public async Task NestedQueryingInMutation()
        {
            string msSqlQuery = @"
                SELECT TOP 1 [table0].[id] AS [id],
                    [table0].[title] AS [title],
                    JSON_QUERY([table1_subq].[data]) AS [publishers]
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

            await NestedQueryingInMutation(msSqlQuery);
        }

        /// <summary>
        /// Test explicitly inserting a null column
        /// </summary>
        [TestMethod]
        public async Task TestExplicitNullInsert()
        {
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

            await TestExplicitNullInsert(msSqlQuery);
        }

        /// <summary>
        /// Test implicitly inserting a null column
        /// </summary>
        [TestMethod]
        public async Task TestImplicitNullInsert()
        {
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

            await TestImplicitNullInsert(msSqlQuery);
        }

        /// <summary>
        /// Test updating a column to null
        /// </summary>
        [TestMethod]
        public async Task TestUpdateColumnToNull()
        {
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

            await TestUpdateColumnToNull(msSqlQuery);
        }

        /// <summary>
        /// Test updating a missing column in the update mutation will not be updated to null
        /// </summary>
        [TestMethod]
        public async Task TestMissingColumnNotUpdatedToNull()
        {
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

            await TestMissingColumnNotUpdatedToNull(msSqlQuery);
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id and book_title are aliases used for corresponding query fields.
        /// The response for the query will use the alias instead of raw db column.
        /// </summary>
        [TestMethod]
        public async Task TestAliasSupportForGraphQLMutationQueryFields()
        {
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

            await TestAliasSupportForGraphQLMutationQueryFields(msSqlQuery);
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
            string msSqlQuery = @"
                SELECT COUNT(*) AS count
                FROM [books]
                WHERE [books].[publisher_id] = - 1
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await InsertWithInvalidForeignKey(msSqlQuery, DbExceptionParserBase.GENERIC_DB_EXCEPTION_MESSAGE);
        }

        /// <summary>
        /// <code>Do: </code>edit a book with an invalid foreign key
        /// <code>Check: </code>that GraphQL returns an error and the book has not been editted
        /// </summary>
        [TestMethod]
        public async Task UpdateWithInvalidForeignKey()
        {
            string msSqlQuery = @"
                SELECT COUNT(*) AS count
                FROM [books]
                WHERE [books].[id] = 1
                    AND [books].[publisher_id] = - 1
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            await UpdateWithInvalidForeignKey(msSqlQuery, DbExceptionParserBase.GENERIC_DB_EXCEPTION_MESSAGE);
        }

        /// <summary>
        /// <code>Do: </code>use an update mutation without passing any of the optional new values to update
        /// <code>Check: </code>check that GraphQL returns an appropriate exception to the user
        /// </summary>
        [TestMethod]
        public override async Task UpdateWithNoNewValues()
        {
            await base.UpdateWithNoNewValues();
        }

        /// <summary>
        /// <code>Do: </code>use an update mutation with an invalid id to update
        /// <code>Check: </code>check that GraphQL returns an appropriate exception to the user
        /// </summary>
        [TestMethod]
        public override async Task UpdateWithInvalidIdentifier()
        {
            await base.UpdateWithInvalidIdentifier();
        }

        /// <summary>
        /// Test adding a website placement to a book which already has a website
        /// placement
        /// </summary>
        [TestMethod]
        public async Task TestViolatingOneToOneRelashionShip()
        {
            await TestViolatingOneToOneRelashionShip(DbExceptionParserBase.GENERIC_DB_EXCEPTION_MESSAGE);
        }
        #endregion
    }
}
