// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Globalization;
using System.Linq;
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
        /// <code>Do: </code> Inserts new row and return all its columns
        /// <code>Check: </code> A row is inserted in the table that has rows with default values as built_in methods.
        /// it should insert it correctly with default values handled by database.
        /// current_date, current_timestamp, random_number, next_date have default value as built_in methods GETDATE(), NOW(), RAND(), DATEADD(), resp.
        /// default_string_with_parenthesis has default value "()", default_function_string_with_parenthesis has default value "NOW()".
        /// default_integer has default value 100, default_date_string has default value "1999-01-08 10:23:54".
        /// Returned response would look like:
        /// "createDefaultBuiltInFunction": {
        ///   "current_date": "2023-12-15T16:24:48.267Z",
        ///   "current_timestamp": "2023-12-15T16:24:48.267Z",
        ///   "random_number": 0,
        ///   "next_date": "2023-12-16T00:00:00.000Z",
        ///   "default_string_with_paranthesis": "()",
        ///   "default_function_string_with_paranthesis": "NOW()",
        ///   "default_integer": 100,
        ///   "default_date_string": "1999-01-08T10:23:54.000Z"
        /// }
        /// </summary>
        public virtual async Task InsertMutationWithDefaultBuiltInFunctions(string dbQuery)
        {
            string graphQLMutationName = "createDefaultBuiltInFunction";
            string graphQLMutation = @"
                mutation {
                    createDefaultBuiltInFunction(item: { user_value: 1234 }) {
                        id
                        user_value
                        current_date
                        current_timestamp
                        random_number
                        next_date
                        default_string_with_parenthesis
                        default_function_string_with_parenthesis
                        default_integer
                        default_date_string
                    }
                }
            ";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            // Assert that the values inserted in the DB is same as the values returned by the mutation
            SqlTestHelper.PerformTestEqualJsonStrings(expected, result.ToString());

            // Assert the values
            Assert.IsFalse(string.IsNullOrEmpty(result.GetProperty("current_date").GetString()));
            Assert.IsFalse(string.IsNullOrEmpty(result.GetProperty("current_timestamp").GetString()));
            Assert.IsNotNull(result.GetProperty("random_number").GetInt32());
            Assert.IsFalse(string.IsNullOrEmpty(result.GetProperty("next_date").GetString()));
            Assert.AreEqual("()", result.GetProperty("default_string_with_parenthesis").GetString());
            Assert.AreEqual("NOW()", result.GetProperty("default_function_string_with_parenthesis").GetString());
            Assert.AreEqual(100, result.GetProperty("default_integer").GetInt32());
            Assert.AreEqual("1999-01-08T10:23:54.000Z", result.GetProperty("default_date_string").GetString());
        }

        /// <summary>
        /// <code>Do: </code> Attempt to insert a new publisher with name not allowed by database policy (@item.name ne 'New publisher')
        /// <code>Check: </code> Mutation fails with expected authorization failure.
        /// </summary>
        /// <param name="dbQuery">SELECT query to validate expected result.</param>
        /// <param name="errorMessage">Expected error message.</param>
        /// <param name="roleName">Custom client role in whose context this authenticated request will be executed</param>
        public async Task InsertMutationFailingDatabasePolicy(string dbQuery, string errorMessage, string roleName)
        {
            string graphQLMutationName = "createPublisher";
            string graphQLMutation = @"
                mutation {
                    createPublisher(item: { name: ""New publisher"" }) {
                        id
                        name
                    }
                }
            ";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, clientRoleHeader: roleName, isAuthenticated: true);

            SqlTestHelper.TestForErrorInGraphQLResponse(
                result.ToString(),
                message: errorMessage,
                statusCode: $"{DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure}"
            );

            string dbResponse = await GetDatabaseResultAsync(dbQuery);
            using JsonDocument dbResponseJson = JsonDocument.Parse(dbResponse);

            // Assert that the create mutation fails to insert the record into the table and that the select query returns no result.
            Assert.AreEqual(dbResponseJson.RootElement.GetProperty("count").GetInt64(), 0);
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
        /// <code>Do: </code> Inserts new sale item into sales table that automatically calculates the total price
        /// based on subtotal and tax.
        /// <code>Check: Calculated column is persisted successfully with correct calculated result. </code>
        /// </summary>
        public async Task InsertMutationForComputedColumns(string dbQuery)
        {
            string graphQLMutationName = "createSales";
            string graphQLMutation = @"
                mutation{
                    createSales(item: {item_name: ""headphones"", subtotal: 195.00, tax: 10.33}) {
                        id
                        item_name
                        subtotal
                        tax
                        total
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
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
        /// <code>Do: </code> Inserts new book in the books table with given publisher_id
        /// <code>Check: </code> If the new book is inserted into the DB and
        /// verifies the response.
        /// </summary>
        public async Task TestStoredProcedureMutationForInsertion(string dbQuery)
        {
            string graphQLMutationName = "executeInsertBook";
            string graphQLMutation = @"
                mutation {
                    executeInsertBook(title: ""Random Book"", publisher_id: 1234 ) {
                        result
                    }
                }
            ";

            string currentDbResponse = await GetDatabaseResultAsync(dbQuery);
            JsonDocument currentResult = JsonDocument.Parse(currentDbResponse);
            Assert.AreEqual(currentResult.RootElement.GetProperty("count").GetInt64(), 0);
            JsonElement graphQLResponse = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);

            // Stored Procedure didn't return anything
            SqlTestHelper.PerformTestEqualJsonStrings("[]", graphQLResponse.ToString());

            // check to verify new element is inserted
            string updatedDbResponse = await GetDatabaseResultAsync(dbQuery);
            JsonDocument updatedResult = JsonDocument.Parse(updatedDbResponse);
            Assert.AreEqual(updatedResult.RootElement.GetProperty("count").GetInt64(), 1);
        }

        /// <summary>
        /// <code>Do: </code> Deletes book from the books table with given id
        /// <code>Check: </code> If the intended book is deleted from the DB and
        /// verifies the response.
        /// </summary>
        public async Task TestStoredProcedureMutationForDeletion(string dbQueryToVerifyDeletion)
        {
            string graphQLMutationName = "executeDeleteLastInsertedBook";
            string graphQLMutation = @"
                mutation {
                    executeDeleteLastInsertedBook {
                        result
                    }
                }
            ";

            string currentDbResponse = await GetDatabaseResultAsync(dbQueryToVerifyDeletion);
            JsonDocument currentResult = JsonDocument.Parse(currentDbResponse);
            Assert.AreEqual(currentResult.RootElement.GetProperty("maxId").GetInt64(), 14);
            JsonElement graphQLResponse = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);

            // Stored Procedure didn't return anything
            SqlTestHelper.PerformTestEqualJsonStrings("[]", graphQLResponse.ToString());

            // check to verify new element is inserted
            string updatedDbResponse = await GetDatabaseResultAsync(dbQueryToVerifyDeletion);
            JsonDocument updatedResult = JsonDocument.Parse(updatedDbResponse);
            Assert.AreEqual(updatedResult.RootElement.GetProperty("maxId").GetInt64(), 13);
        }

        public async Task InsertMutationOnTableWithTriggerWithNonAutoGenPK(string dbQuery)
        {
            // Given input item { id: 4, name: ""Tommy"", salary: 45 }, this test verifies that the selection would return salary = 30.
            // Thus confirming that we return the data being updated by the trigger where,
            // the trigger behavior is that it updates the salary to max(0,min(30,salary)).
            string graphQLMutationName = "createInternData";
            string graphQLMutation = @"
                mutation{
                    createInternData(item: { id: 4, name: ""Tommy"", salary: 45 }) {
                        id
                        months
                        name
                        salary
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        public async Task InsertMutationOnTableWithTriggerWithAutoGenPK(string dbQuery)
        {
            // Given input item { name: ""Joel"", salary: 102 }, this test verifies that the selection would return salary = 100.
            // Thus confirming that we return the data being updated by the trigger where,
            // the trigger behavior is that it updates the salary to max(0,min(100,salary)).
            string graphQLMutationName = "createFteData";
            string graphQLMutation = @"
                mutation{
                    createFteData(item: { name: ""Joel"", salary: 102 }) {
                        id
                        u_id
                        name
                        position
                        salary
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        public async Task UpdateMutationOnTableWithTriggerWithNonAutoGenPK(string dbQuery)
        {
            // Given input item { salary: 100 }, this test verifies that the selection would return salary = 50.
            // Thus confirming that we return the data being updated by the trigger where,
            // the trigger behavior is that it updates the salary to max(0,min(50,salary)).
            string graphQLMutationName = "updateInternData";
            string graphQLMutation = @"
                mutation{
                    updateInternData(id: 1, months: 3, item: { salary: 100 }) {
                        id
                        months
                        name
                        salary
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        public async Task UpdateMutationOnTableWithTriggerWithAutoGenPK(string dbQuery)
        {
            // Given input item { salary: -9 }, this test verifies that the selection would return salary = 0.
            // Thus confirming that we return the data being updated by the trigger where,
            // the trigger behavior is that it updates the salary to max(0,min(150,salary)).
            string graphQLMutationName = "updateFteData";
            string graphQLMutation = @"
                mutation{
                    updateFteData(id: 1, u_id: 2, item: { salary: -9 }) {
                        id
                        u_id
                        name
                        position
                        salary
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// <code>Do: </code> Insert a new book with a given title and publisher name.
        /// and returns all the books under the given publisher.
        /// <code>Check: </code> If the intended book is inserted into the DB and
        /// verifies the non-empty response.
        /// </summary>
        public async Task TestStoredProcedureMutationNonEmptyResponse(string dbQuery)
        {
            string graphQLMutationName = "executeInsertAndDisplayAllBooksUnderGivenPublisher";
            string graphQLMutation = @"
                mutation{
                    executeInsertAndDisplayAllBooksUnderGivenPublisher(title: ""Orange Tomato"" publisher_name: ""Big Company""){
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
        /// <code>Do: </code> updates a book title from the books table with given id
        /// and new title.
        /// <code>Check: </code> The book title should be updated with the given id
        /// DB is queried to verify the result.
        /// </summary>
        public async Task TestStoredProcedureMutationForUpdate(string dbQuery)
        {
            string graphQLMutationName = "executeUpdateBookTitle";
            string graphQLMutation = @"
                mutation {
                    executeUpdateBookTitle(id: 14, title: ""Before Midnight"") {
                        id
                        title
                        publisher_id
                    }
                }
            ";

            string beforeUpdate = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings("{\"id\":14,\"title\":\"Before Sunset\",\"publisher_id\":1234}", beforeUpdate);
            JsonElement graphQLResponse = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string afterUpdate = await GetDatabaseResultAsync(dbQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(afterUpdate.ToString(), graphQLResponse.EnumerateArray().First().ToString());
        }

        /// <summary>
        /// <code>Do: </code> Inserts new stock price with default current timestamp as the value of 
        /// 'instant' column and returns the inserted row.
        /// <code>Check: </code> If stock price with the given (category, piece id) is successfully inserted
        /// in the database by the mutation with a default value for 'instant'.
        /// </summary>
        public async Task InsertMutationForVariableNotNullDefault(string dbQuery)
        {
            string graphQLMutationName = "createstocks_price";
            string graphQLMutation = @"
                mutation {
                  createstocks_price(item: { categoryid: 100 pieceid: 99 price: 50.0 is_wholesale_price: true } ) {
                    pieceid
                    categoryid
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
        /// <code>Do: </code>Update book in database and return the typename of the entity
        /// <code>Check: </code>if the mutation executed successfully and returned the correct typename
        /// </summary>
        [TestMethod]
        public virtual async Task UpdateMutationWithOnlyTypenameInSelectionSet()
        {
            string graphQLMutationName = "updatebook";
            string graphQLMutation = @"
                mutation {
                    updatebook(id: 1, item: { title: ""Even Better Title"", publisher_id: 2345} ) {
                        __typename
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = @"
              {
                ""__typename"": ""book""
              }
            ";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// <code>Do :</code> Delete a book from database and return the typename of the book entity
        /// <code>Check :</code>if the mutation executed successfully and returned the correct typename
        /// </summary>
        [TestMethod]
        public virtual async Task DeleteMutationWithOnlyTypename()
        {
            string graphQLMutationName = "deletebook";
            string graphQLMutation = @"
                mutation {
                    deletebook(id: 1) {
                        __typename
                    }
                }
            ";

            string expected = @"
              {
                ""__typename"": ""book""
              }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// <code>Do: </code>Inserts a new book in database and returns the typename of the book entity
        /// <code>Check: </code>if the mutation executed successfully and returned the correct typename
        /// </summary>
        [TestMethod]
        public virtual async Task InsertMutationWithOnlyTypenameInSelectionSet()
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"
                mutation {
                    createbook(item: { title: ""Awesome Book"", publisher_id: 1234 }) {
                        __typename
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = @"
              {
                ""__typename"": ""book""
              }
            ";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// <code>Do: </code> Execute a stored procedure and return the typename of the SP entity
        /// <code>Check :</code>if the mutation executed successfully and returned the correct typename
        /// </summary>
        public virtual async Task ExecuteMutationWithOnlyTypenameInSelectionSet()
        {
            string graphQLMutationName = "executeCountBooks";
            string graphQLMutation = @"
                mutation {
                    executeCountBooks{
                        __typename
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = @"
              [
                {
                  ""__typename"": ""CountBooks""
                }
              ]
            ";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// <code>Do: </code>Update Sales in database and return its updated fields
        /// <code>Check: The calculated column has successfully been updated after updating the other fields </code>
        /// </summary>
        public async Task UpdateMutationForComputedColumns(string dbQuery)
        {
            string graphQLMutationName = "updateSales";
            string graphQLMutation = @"
                mutation{
                    updateSales(id: 2, item: {item_name: ""phone"", subtotal: 495.00, tax: 30.33}) {
                        id
                        item_name
                        subtotal
                        tax
                        total
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

        /// <summary>
        /// Demonstrates that using mapped column names for fields within the GraphQL mutation results in successful engine processing.
        /// </summary>
        public async Task InsertMutationWithVariablesAndMappings(string dbQuery)
        {
            string graphQLMutationName = "createGQLmappings";
            string graphQLMutation = @"
                mutation($id: Int!, $col2Value: String) {
                    createGQLmappings(item: { column1: $id, column2: $col2Value }) {
                        column1
                        column2
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true, new() { { "id", 2 }, { "col2Value", "My New Value" } });
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Demonstrates that using mapped column names for fields within the GraphQL mutation results in successful engine processing
        /// of the column2 value update for the record where column1 = $id.
        /// </summary>
        public async Task UpdateMutationWithVariablesAndMappings(string dbQuery)
        {
            string graphQLMutationName = "updateGQLmappings";
            string graphQLMutation = @"
                mutation($id: Int!, $col2Value: String) {
                    updateGQLmappings(column1: $id, item: { column2: $col2Value }) {
                        column1
                        column2
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true, new() { { "id", 3 }, { "col2Value", "Updated Value of Mapped Column" } });
            string expected = await GetDatabaseResultAsync(dbQuery);

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Demonstrates that using mapped column names for fields within the GraphQL mutation results in successful engine processing
        /// of removal of the record where column1 = $id and the returned object representing the deleting record utilizes the mapped column values.
        /// </summary>
        public async Task DeleteMutationWithVariablesAndMappings(string dbQuery, string dbQueryToVerifyDeletion)
        {
            string graphQLMutationName = "deleteGQLmappings";
            string graphQLMutation = @"
                mutation($id: Int!) {
                    deleteGQLmappings(column1: $id) {
                        column1
                        column2
                    }
                }
            ";

            string expected = await GetDatabaseResultAsync(dbQuery);
            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true, new() { { "id", 4 } });

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());

            string dbResponse = await GetDatabaseResultAsync(dbQueryToVerifyDeletion);

            using JsonDocument result = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(result.RootElement.GetProperty("count").GetInt64(), 0);
        }

        /// <summary>
        /// Performs concurrent update mutations on the same item and validates that the responses
        /// returned are consistent
        /// gQLMutation1 : Updates the title column of Book table to New Title
        /// gQLMutation2 : Updates the title column of Book table to Updated Title
        /// The title field in the responses returned for each of the mutations should be
        /// the same value it had written to the table.
        /// </summary>
        [TestMethod]
        public virtual async Task TestParallelUpdateMutations()
        {
            string graphQLMutationName = "updatebook";
            string gQLMutation1 = @"
                mutation {
                    updatebook(id : 1, item: { title: ""New Title"" })
                    {
                        title
                    }
                }";

            string gQLMutation2 = @"
                mutation {
                    updatebook(id : 1, item: { title: ""Updated Title"" })
                    {
                        title
                    }
                }";

            Task<JsonElement> responeTask1 = ExecuteGraphQLRequestAsync(gQLMutation1, graphQLMutationName, isAuthenticated: true);
            Task<JsonElement> responseTask2 = ExecuteGraphQLRequestAsync(gQLMutation2, graphQLMutationName, isAuthenticated: true);

            JsonElement response1 = await responeTask1;
            JsonElement response2 = await responseTask2;

            Assert.AreEqual("{\"title\":\"New Title\"}", response1.ToString());
            Assert.AreEqual("{\"title\":\"Updated Title\"}", response2.ToString());
        }

        /// <summary>
        /// Performs concurrent insert mutation on a table where the PK is auto-generated.
        /// Since, PK is auto-generated, essentially both the mutations are operating on
        /// different items. Therefore, both the mutations should succeed.
        /// </summary>
        [TestMethod]
        public virtual async Task TestParallelInsertMutationPKAutoGenerated()
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation1 = @"
                mutation {
                    createbook(item: { title: ""Awesome Book"", publisher_id: 1234 }) {
                        title
                    }
                }
            ";

            string graphQLMutation2 = @"
                mutation {
                    createbook(item: { title: ""Another Awesome Book"", publisher_id: 1234 }) {
                        title
                    }
                }
            ";

            Task<JsonElement> responeTask1 = ExecuteGraphQLRequestAsync(graphQLMutation1, graphQLMutationName, isAuthenticated: true);
            Task<JsonElement> responseTask2 = ExecuteGraphQLRequestAsync(graphQLMutation2, graphQLMutationName, isAuthenticated: true);

            JsonElement response1 = await responeTask1;
            JsonElement response2 = await responseTask2;

            Assert.AreEqual("{\"title\":\"Awesome Book\"}", response1.ToString());
            Assert.AreEqual("{\"title\":\"Another Awesome Book\"}", response2.ToString());

        }

        /// <summary>
        /// Performs concurrent insert mutation on a table where the PK is not auto-generated and
        /// validates that only one of the mutations is successful.
        /// Both the mutations attempt to create an item with the same primary key. The mutation request
        /// that runs first at the database layer should succeed and the other request should fail with
        /// primary key violation constraint.
        /// </summary>
        [TestMethod]
        public virtual async Task TestParallelInsertMutationPKNonAutoGenerated()
        {
            string graphQLMutationName = "createComic";

            string graphQLMutation1 = @"
                mutation {
                    createComic (item: { id : 5001, categoryName: ""Fantasy"", title: ""Harry Potter""}){
                    id
                    title    
                }
                }
            ";

            string graphQLMutation2 = @"
                mutation {
                    createComic (item: { id : 5001, categoryName: ""Fantasy"", title: ""Lord of the Rings""}){
                    id
                    title    
                }
                }
            ";

            Task<JsonElement> responeTask1 = ExecuteGraphQLRequestAsync(graphQLMutation1, graphQLMutationName, isAuthenticated: true);
            Task<JsonElement> responseTask2 = ExecuteGraphQLRequestAsync(graphQLMutation2, graphQLMutationName, isAuthenticated: true);

            JsonElement response1 = await responeTask1;
            JsonElement response2 = await responseTask2;

            string responseString1 = response1.ToString();
            string responseString2 = response2.ToString();
            string expectedStatusCode = $"{DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed}";

            // It is not possible to know beforehand which mutation created the new item. So, validations
            // are performed for the cases where either mutation could have succeeded. In each case,
            // one of the mutation's reponse will contain a valid repsonse and the other mutation's
            // response would contain DatabaseOperationFailed sub-status code as it would've failed at
            // the database layer due to primary key violation constraint.
            if (responseString1.Contains($"\"code\":\"{expectedStatusCode}\""))
            {
                Assert.AreEqual("{\"id\":5001,\"title\":\"Lord of the Rings\"}", responseString2);
            }
            else if (responseString2.Contains($"\"code\":\"{expectedStatusCode}\""))
            {
                Assert.AreEqual("{\"id\":5001,\"title\":\"Harry Potter\"}", responseString1);
            }
            else
            {
                Assert.Fail("Unexpected error. Atleast one of the mutations should've succeeded");
            }
        }

        /// <summary>
        /// Performs concurrent delete mutations on the same item and validates that only one of the
        /// requests is successful.
        /// </summary>
        [TestMethod]
        public virtual async Task TestParallelDeleteMutations()
        {
            string graphQLMutationName = "deletebook";

            string graphQLMutation1 = @"
                mutation {
                  deletebook (id: 1){
                    id
                    title
                  }
                }
            ";

            Task<JsonElement> responeTask1 = ExecuteGraphQLRequestAsync(graphQLMutation1, graphQLMutationName, isAuthenticated: true);
            Task<JsonElement> responseTask2 = ExecuteGraphQLRequestAsync(graphQLMutation1, graphQLMutationName, isAuthenticated: true);

            JsonElement response1 = await responeTask1;
            JsonElement response2 = await responseTask2;

            string responseString1 = response1.ToString();
            string responseString2 = response2.ToString();
            string expectedResponse = "{\"id\":1,\"title\":\"Awesome book\"}";

            // The mutation request that deletes the item is expected to have a valid response
            // and the other mutation is expected to receive an empty response as it
            // won't see the item in the table.
            if (responseString1.Length == 0)
            {
                Assert.AreEqual(expectedResponse, responseString2);
            }
            else if (responseString2.Length == 0)
            {
                Assert.AreEqual(expectedResponse, responseString1);
            }
            else
            {
                Assert.Fail("Unexpected failure. Atleast one of the delete mutations should've succeeded");
            }

        }

        /// <summary>
        /// Performs create item on different Windows Regional format settings and validate that the data type Float is correct
        /// </summary>
        public async Task CanCreateItemWithCultureInvariant(string cultureInfo, string dbQuery)
        {
            CultureInfo ci = new(cultureInfo);
            CultureInfo.DefaultThreadCurrentCulture = ci;

            string graphQLMutationName = "createSales";
            string graphQLMutation = @"
                mutation {
                    createSales (item: { item_name: ""test_name"", subtotal: 3.14, tax: 1.15 }) {
                        id
                        item_name
                        subtotal
                        tax
                    }
                }
            ";

            JsonElement response = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string dbResponse = await GetDatabaseResultAsync(dbQuery);
            using JsonDocument dbResponseJson = JsonDocument.Parse(dbResponse);

            // Validate results
            Assert.AreEqual(Convert.ToDouble(dbResponseJson.RootElement.GetProperty("subtotal").GetDouble(), CultureInfo.InvariantCulture), response.GetProperty("subtotal").GetDouble());
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// <code>Do: </code> Attempts to insert a new row with a null value for a variable default column.
        /// <code>Check: </code> Even though the operation is expected to fail,
        /// the failure happens in the database, not with a GraphQL schema error.
        /// This verifies that since the column has a default value on the database,
        /// the GraphQL schema input field type is nullable even though the underlying column type is not.
        /// </summary>
        [TestMethod]
        public virtual async Task TestTryInsertMutationForVariableNotNullDefault()
        {
            string graphQLMutationName = "createSupportedType";
            string graphQLMutation = @"
                mutation {
                    createstocks_price(item: { categoryid: 100 pieceid: 99 instant: null } ) {
                    categoryid
                    pieceid
                    instant
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.TestForErrorInGraphQLResponse(
                actual.ToString(),
                statusCode: $"{DataApiBuilderException.SubStatusCodes.DatabaseOperationFailed}");
        }

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
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataApiBuilderException.SubStatusCodes.ItemNotFound}");
        }

        /// <summary>
        /// Test adding a website placement to a book which already has a website
        /// placement
        /// </summary>
        public async Task TestViolatingOneToOneRelationship(string errorMessage)
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

        /// <summary>
        /// Test to validate failure of a request when one or more fields referenced in the database policy for create operation are not provided in the request body.
        /// </summary>
        /// <returns></returns>
        [TestMethod]
        public virtual async Task TestDbPolicyForCreateOperationReferencingFieldAbsentInRequest()
        {
            string graphQLMutationName = "createSupportedType";
            string graphQLMutation = @"
                mutation {
                    createRevenue(item: { id: 18, category: ""SciFi"", accessible_role: ""Anonymous"" })
                            {
                                id
                            }
                        }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true, clientRoleHeader: "database_policy_tester");
            SqlTestHelper.TestForErrorInGraphQLResponse(
                actual.ToString(),
                message: "One or more fields referenced by the database policy are not present in the request body.",
                statusCode: $"{DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed}");
        }

        #endregion
    }
}
