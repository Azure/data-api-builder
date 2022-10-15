using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLMutationTests
{
    /// <summary>
    /// Base class for GraphQL Mutation tests targetting Sql databases.
    /// </summary>
    [TestClass]
    public abstract class GraphQLMutationTestBase : SqlTestBase
    {
        #region Positive Tests

        /// <summary>
        /// <code>Do: </code> Inserts new book and return its id and title
        /// <code>Check: </code> If book with the expected values of the new book is present in the database and
        /// if the mutation query has returned the correct information
        /// </summary>
        public async Task InsertMutation(string dbQuery)
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"
                mutation {
                    createbook(item: { title: ""My New Book"", publisher_id: 1234 }) {
                        id
                        title
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// <code>Do: </code> Inserts new book using variables to set its title and publisher_id
        /// <code>Check: </code> If book with the expected values of the new book is present in the database and
        /// if the mutation query has returned the correct information
        /// </summary>
        public async Task InsertMutationWithVariables(string dbQuery)
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"
                mutation($title: String!, $publisher_id: Int!) {
                    createbook(item: { title: $title, publisher_id: $publisher_id }) {
                        id
                        title
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true, new() { { "title", "My New Book" }, { "publisher_id", 1234 } });
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// <code>Do: </code> Inserts new review with default content for a Review and return its id and content
        /// <code>Check: </code> If book with the given id is present in the database then
        /// the mutation query will return the review Id with the content of the review added
        /// </summary>
        public async Task InsertMutationForConstantdefaultValue(string dbQuery)
        {
            string graphQLMutationName = "createreview";
            string graphQLMutation = @"
                mutation {
                    createreview(item: { book_id: 1 }) {
                        id
                        content
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// <code>Do: </code>Update book in database and return its updated fields
        /// <code>Check: </code>if the book with the id of the edited book and the new values exists in the database
        /// and if the mutation query has returned the values correctly
        /// </summary>
        public async Task UpdateMutation(string dbQuery)
        {
            string graphQLMutationName = "updatebook";
            string graphQLMutation = @"
                mutation {
                    updatebook(id: 1, item: { title: ""Even Better Title"", publisher_id: 2345} ) {
                        title
                        publisher_id
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// <code>Do: </code>Delete book by id
        /// <code>Check: </code>if the mutation returned result is as expected and if book by that id has been deleted
        /// </summary>
        public async Task DeleteMutation(string dbQueryForResult, string dbQueryToVerifyDeletion)
        {
            string graphQLMutationName = "deletebook";
            string graphQLMutation = @"
                mutation {
                    deletebook(id: 1) {
                        title
                        publisher_id
                    }
                }
            ";

            // query the table before deletion is performed to see if what the mutation
            // returns is correct
            string expected = await GetDatabaseResultAsync(dbQueryForResult);
            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());

            string dbResponse = await GetDatabaseResultAsync(dbQueryToVerifyDeletion);

            using JsonDocument result = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(result.RootElement.GetProperty("count").GetInt64(), 0);
        }

        /// <summary>
        /// <code>Do: </code>run a mutation which mutates a relationship instead of a graphql type
        /// <code>Check: </code>that the insertion of the entry in the appropriate link table was successful
        /// </summary>
        // IGNORE FOR NOW, SEE: Issue #285
        public async Task InsertMutationForNonGraphQLTypeTable(string dbQuery)
        {
            string graphQLMutationName = "addAuthorToBook";
            string graphQLMutation = @"
                mutation {
                    addAuthorToBook(author_id: 123, book_id: 2)
                }
            ";

            await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string dbResponse = await GetDatabaseResultAsync(dbQuery);

            using JsonDocument result = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(result.RootElement.GetProperty("count").GetInt64(), 1);
        }

        /// <summary>
        /// <code>Do: </code>a new Book insertion and do a nested querying of the returned book
        /// <code>Check: </code>if the result returned from the mutation is correct
        /// </summary>
        public async Task NestedQueryingInMutation(string dbQuery)
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"
                mutation {
                    createbook(item: {title: ""My New Book"", publisher_id: 1234}) {
                        id
                        title
                        publishers {
                            name
                        }
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test explicitly inserting a null column
        /// </summary>
        public async Task TestExplicitNullInsert(string dbQuery)
        {
            string graphQLMutationName = "createmagazine";
            string graphQLMutation = @"
                mutation {
                    createmagazine(item: { id: 800, title: ""New Magazine"", issue_number: null }) {
                        id
                        title
                        issue_number
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test implicitly inserting a null column
        /// </summary>
        public async Task TestImplicitNullInsert(string dbQuery)
        {
            string graphQLMutationName = "createmagazine";
            string graphQLMutation = @"
                mutation {
                    createmagazine(item: {id: 801, title: ""New Magazine 2""}) {
                        id
                        title
                        issue_number
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test updating a column to null
        /// </summary>
        public async Task TestUpdateColumnToNull(string dbQuery)
        {
            string graphQLMutationName = "updatemagazine";
            string graphQLMutation = @"
                mutation {
                    updatemagazine(id: 1, item: { issue_number: null} ) {
                        id
                        issue_number
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test updating a missing column in the update mutation will not be updated to null
        /// </summary>
        public async Task TestMissingColumnNotUpdatedToNull(string dbQuery)
        {
            string graphQLMutationName = "updatemagazine";
            string graphQLMutation = @"
                mutation {
                    updatemagazine(id: 1, item: {id: 1, title: ""Newest Magazine""}) {
                        id
                        title
                        issue_number
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Test to check graphQL support for aliases(arbitrarily set by user while making request).
        /// book_id and book_title are aliases used for corresponding query fields.
        /// The response for the query will use the alias instead of raw db column.
        /// </summary>
        public async Task TestAliasSupportForGraphQLMutationQueryFields(string dbQuery)
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"
                mutation {
                    createbook(item: { title: ""My New Book"", publisher_id: 1234 }) {
                        book_id: id
                        book_title: title
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Insert into a simple view (contains columns from one table)
        /// </summary>
        public async Task InsertIntoSimpleView(string dbQuery)
        {
            string graphQLMutationName = "createbooks_view_all";
            string graphQLMutation = @"
                mutation {
                    createbooks_view_all(item: { title: ""Book View"", publisher_id: 1234 }) {
                        id
                        title
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Update a simple view (contains columns from one table)
        /// </summary>
        public async Task UpdateSimpleView(string dbQuery)
        {
            string graphQLMutationName = "updatebooks_view_all";
            string graphQLMutation = @"
                mutation {
                    updatebooks_view_all(id: 1 item: { title: ""New title from View""}) {
                        id
                        title
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Delete from simple view (contains columns from one table)
        /// </summary>
        public async Task DeleteFromSimpleView(string dbQueryForResult, string dbQueryToVerifyDeletion)
        {
            string graphQLMutationName = "deletebooks_view_all";
            string graphQLMutation = @"
                mutation {
                    deletebooks_view_all(id: 1) {
                        id
                        title
                    }
                }
            ";

            // query the table before deletion is performed to see if what the mutation
            // returns is correct
            string expected = await GetDatabaseResultAsync(dbQueryForResult);
            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());

            // check if entry is actually deleted
            string dbResponse = await GetDatabaseResultAsync(dbQueryToVerifyDeletion);

            using JsonDocument result = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(result.RootElement.GetProperty("count").GetInt64(), 0);
        }

        /// <summary>
        /// Insert into an "insertable" complex view (contains columns from one table)
        /// books_publishers_view_composite_insertable has a trigger to handle inserts
        /// </summary>
        public async Task InsertIntoInsertableComplexView(string dbQuery)
        {
            string graphQLMutationName = "createbooks_publishers_view_composite_insertable";
            string graphQLMutation = @"
                mutation {
                    createbooks_publishers_view_composite_insertable(item: { title: ""Book Complex View"", publisher_id: 1234 }) {
                        id
                        title
                        publisher_id
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// <code>Do: </code>insert a new Book with an invalid foreign key
        /// <code>Check: </code>that GraphQL returns an error and that the book has not actually been added
        /// </summary>
        public async Task InsertWithInvalidForeignKey(string dbQuery, string errorMessage)
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"
                mutation {
                    createbook(item: { title: ""My New Book"", publisher_id: -1}) {
                        id
                        title
                    }
                }
            ";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);

            SqlTestHelper.TestForErrorInGraphQLResponse(
                result.ToString(),
                message: errorMessage,
                statusCode: $"{DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed}"
            );

            string dbResponse = await GetDatabaseResultAsync(dbQuery);
            using JsonDocument dbResponseJson = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(dbResponseJson.RootElement.GetProperty("count").GetInt64(), 0);
        }

        /// <summary>
        /// <code>Do: </code>edit a book with an invalid foreign key
        /// <code>Check: </code>that GraphQL returns an error and the book has not been editted
        /// </summary>
        public async Task UpdateWithInvalidForeignKey(string dbQuery, string errorMessage)
        {
            string graphQLMutationName = "updatebook";
            string graphQLMutation = @"
                mutation {
                    updatebook(id: 1, item: {publisher_id: -1 }) {
                        id
                        title
                    }
                }
            ";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);

            SqlTestHelper.TestForErrorInGraphQLResponse(
                result.ToString(),
                message: errorMessage,
                statusCode: $"{DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed}"
            );

            string dbResponse = await GetDatabaseResultAsync(dbQuery);
            using JsonDocument dbResponseJson = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(dbResponseJson.RootElement.GetProperty("count").GetInt64(), 0);
        }

        /// <summary>
        /// <code>Do: </code>use an update mutation without passing any of the optional new values to update
        /// <code>Check: </code>check that GraphQL returns an appropriate exception to the user
        /// </summary>
        public virtual async Task UpdateWithNoNewValues()
        {
            string graphQLMutationName = "updatebook";
            string graphQLMutation = @"
                mutation {
                    updatebook(id: 1) {
                        id
                        title
                    }
                }
            ";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), message: $"item");
        }

        /// <summary>
        /// <code>Do: </code>use an update mutation with an invalid id to update
        /// <code>Check: </code>check that GraphQL returns an appropriate exception to the user
        /// </summary>
        public virtual async Task UpdateWithInvalidIdentifier()
        {
            string graphQLMutationName = "updatebook";
            string graphQLMutation = @"
                mutation {
                    updatebook(id: -1, item: { title: ""Even Better Title"" }) {
                        id
                        title
                    }
                }
            ";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataApiBuilderException.SubStatusCodes.EntityNotFound}");
        }

        /// <summary>
        /// Test adding a website placement to a book which already has a website
        /// placement
        /// </summary>
        public async Task TestViolatingOneToOneRelashionShip(string errorMessage)
        {
            string graphQLMutationName = "createBookWebsitePlacement";
            string graphQLMutation = @"
                mutation {
                    createBookWebsitePlacement(item: {book_id: 1, price: 25 }) {
                        id
                    }
                }
            ";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.TestForErrorInGraphQLResponse(
                result.ToString(),
                message: errorMessage,
                statusCode: $"{DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed}"
            );
        }

        /// <summary>
        /// Insert into a complex view (contains columns from one table)
        /// This will fail unless there are some triggers set up to instruct the db
        /// how to handle the insertion in complex views.
        /// books_publishers_view_composite doesn't have such triggers so the mutation
        /// will fail
        /// </summary>
        public async Task InsertIntoCompositeView(string dbQuery)
        {
            string graphQLMutationName = "createbooks_publishers_view_composite";
            string graphQLMutation = @"
                mutation {
                    createbooks_publishers_view_composite(item: { name: ""Big Company"", publisher_id: 1234 }) {
                        id
                        name
                    }
                }
            ";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed}");
        }

        #endregion
    }
}
