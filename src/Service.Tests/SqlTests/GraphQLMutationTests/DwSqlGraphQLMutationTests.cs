// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLMutationTests
{

    [TestClass, TestCategory(TestCategory.DWSQL)]
    public class DwSqlGraphQLMutationTests : GraphQLMutationTestBase
    {
        #region Test Fixture Setup
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.DWSQL;
            await InitializeTestFixture();
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
        /// <code>Check: </code> Result value of success is verified in the response.
        /// </summary>
        [TestMethod]
        public async Task InsertMutation()
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"
                mutation {
                    createbook(item: { id: 1, title: ""My New Book"", publisher_id: 1234 }) {
                        result
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            Assert.IsTrue(actual.ToString().Contains("success"), "DwSql mutation insert should be successful");
        }

        /// <summary>
        /// <code>Do: </code> Inserts new book using variables to set its title and publisher_id
        /// <code>Check: </code> Result value of success is verified in the response.
        /// </summary>
        [TestMethod]
        public async Task InsertMutationWithVariables()
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"
                mutation($title: String!, $publisher_id: Int!) {
                    createbook(item: { id: 2, title: $title, publisher_id: $publisher_id }) {
                        result
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true, new() { { "title", "My New Book" }, { "publisher_id", 1234 } });
            Assert.IsTrue(actual.ToString().Contains("success"), "DwSql mutation insert with variables should be successful");
        }

        /// <summary>
        /// <code>Do: </code>Inserts a new book in database and returns the typename
        /// <code>Check: </code>if the mutation executed successfully and returned the correct typename
        /// </summary>
        [TestMethod]
        public override async Task InsertMutationWithOnlyTypenameInSelectionSet()
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"
                mutation {
                    createbook(item: { id: 1, title: ""Awesome Book"", publisher_id: 1234 }) {
                        __typename
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string expected = @"
              {
                ""__typename"": ""DbOperationResult""
              }
            ";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// <code>Do: </code> Inserts new Publisher with name = 'New publisher'
        /// <code>Check: </code> Mutation fails because the database policy (@item.name ne 'New publisher') prohibits insertion of records with name = 'New publisher'.
        /// </summary>
        [TestMethod]
        public async Task InsertMutationFailingDatabasePolicy()
        {
            string errorMessage = "Could not insert row with given values.";
            string msSqlQuery = @"
                SELECT count(*) as count
                   FROM [publishers]
                WHERE [name] = 'New publisher'
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string graphQLMutationName = "createPublisher";
            string graphQLMutationPayload = @"
                mutation {
                    createPublisher(item: { id: 1 name: ""New publisher"" }) {
                        result
                    }
                }
            ";

            await InsertMutationFailingDatabasePolicy(
                dbQuery: msSqlQuery,
                errorMessage: errorMessage,
                roleName: "database_policy_tester",
                graphQLMutationName: graphQLMutationName,
                graphQLMutationPayload: graphQLMutationPayload);
        }

        /// <summary>
        /// <code>Do: </code> Inserts new Publisher with name = 'Not New publisher'
        /// <code>Check: </code> Mutation succeeds because the database policy (@item.name ne 'New publisher') is passed
        /// </summary>
        [TestMethod]
        public async Task InsertMutationWithDatabasePolicy()
        {
            string msSqlQuery = @"
                SELECT COUNT(*) AS [count]
                   FROM [publishers]
                WHERE [name] = 'Not New publisher'
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string graphqlMutationName = "createPublisher";
            string graphQLMutationPayload = @"
                mutation {
                    createPublisher(item: { id: 1 name: ""Not New publisher"" }) {
                        result
                    }
                }
            ";

            await InsertMutationWithDatabasePolicy(
                dbQuery: msSqlQuery,
                roleName: "database_policy_tester",
                graphQLMutationName: graphqlMutationName,
                graphQLMutationPayload: graphQLMutationPayload);
        }

        /// <summary>
        /// <code>Do: </code>Update book in database and return its updated fields
        /// <code>Check: </code>Result value of success is verified in the response.
        /// </summary>
        [TestMethod]
        public async Task UpdateMutation()
        {
            string graphQLMutationName = "updatebook";
            string graphQLMutation = @"
                mutation {
                    updatebook(id: 1, item: { title: ""Even Better Title"", publisher_id: 2345} ) {
                        result
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            Assert.IsTrue(actual.ToString().Contains("success"), "DwSql mutation update should be successful");
        }

        /// <summary>
        /// <code>Do: </code>Delete book by id
        /// <code>Check: </code>Result value of success is verified in the response and book by that id has been deleted
        /// </summary>
        [TestMethod]
        public async Task DeleteMutation()
        {
            string msSqlQueryToVerifyDeletion = @"
                SELECT COUNT(*) AS count
                FROM [books]
                WHERE [id] = 1
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string graphQLMutationName = "deletebook";
            string graphQLMutation = @"
                mutation {
                    deletebook(id: 1) {
                        result
                    }
                }
            ";

            // query the table before deletion is performed
            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);

            Assert.IsTrue(actual.ToString().Contains("success"), "DwSql mutation update should be successful");

            string dbResponse = await GetDatabaseResultAsync(msSqlQueryToVerifyDeletion);

            using JsonDocument result = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(result.RootElement.GetProperty("count").GetInt64(), 0);
        }

        /// <summary>
        /// Test explicitly inserting a null column
        /// </summary>
        [TestMethod]
        public async Task TestExplicitNullInsert()
        {
            string graphQLMutationName = "createmagazine";
            string graphQLMutation = @"
                mutation {
                    createmagazine(item: { id: 800, title: ""New Magazine"", issue_number: null }) {
                        result
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            Assert.IsTrue(actual.ToString().Contains("success"), "DwSql mutation with explicit null should be successful");
        }

        /// <summary>
        /// Test implicitly inserting a null column
        /// </summary>
        [TestMethod]
        public async Task TestImplicitNullInsert()
        {
            string graphQLMutationName = "createmagazine";
            string graphQLMutation = @"
                mutation {
                    createmagazine(item: {id: 801, title: ""New Magazine 2""}) {
                        result
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            Assert.IsTrue(actual.ToString().Contains("success"), "DwSql mutation with implicit null should be successful");
        }

        /// <summary>
        /// Test updating a column to null
        /// </summary>
        [TestMethod]
        public async Task TestUpdateColumnToNull()
        {
            string graphQLMutationName = "updatemagazine";
            string graphQLMutation = @"
                mutation {
                    updatemagazine(id: 1, item: { issue_number: null} ) {
                        result
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            Assert.IsTrue(actual.ToString().Contains("success"), "DwSql mutation update to null should be successful");
        }

        /// <summary>
        /// Test updating a missing column in the update mutation will not be updated to null
        /// </summary>
        [TestMethod]
        public async Task TestMissingColumnNotUpdatedToNull()
        {
            string graphQLMutationName = "updatemagazine";
            string graphQLMutation = @"
                mutation {
                    updatemagazine(id: 1, item: {id: 1, title: ""Newest Magazine""}) {
                        result
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            Assert.IsTrue(actual.ToString().Contains("success"), "DwSql mutation missing column should not update to null");
        }

        /// <summary>
        /// <code>Do: </code>insert into a simple view
        /// <code>Check: </code> that the new entry is in the view
        /// </summary>
        [TestMethod]
        public async Task InsertIntoSimpleView()
        {
            string graphQLMutationName = "createbooks_view_all";
            string graphQLMutation = @"
                mutation {
                    createbooks_view_all(item: { id: 1, title: ""Book View"", publisher_id: 1234 }) {
                        result
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            Assert.IsTrue(actual.ToString().Contains("success"), "DwSql mutation insert into view should be successful");
        }

        /// <summary>
        /// <code>Do: </code>Update a simple view
        /// <code>Check: </code> Result value of success is verified in the response.
        /// </summary>
        [TestMethod]
        public async Task UpdateSimpleView()
        {
            string graphQLMutationName = "updatebooks_view_all";
            string graphQLMutation = @"
                mutation {
                    updatebooks_view_all(id: 1 item: { title: ""New title from View""}) {
                        result
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            Assert.IsTrue(actual.ToString().Contains("success"), "DwSql mutation update view should be successful");
        }

        [TestMethod]
        public async Task InsertMutationWithVariablesAndMappings()
        {
            string graphQLMutationName = "createGQLmappings";
            string graphQLMutation = @"
                mutation($id: Int!, $col2Value: String) {
                    createGQLmappings(item: { column1: $id, column2: $col2Value }) {
                        result
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true, new() { { "id", 2 }, { "col2Value", "My New Value" } });
            Assert.IsTrue(actual.ToString().Contains("success"), "DwSql insert mutation with variables and mappings should be successful");
        }

        /// <summary>
        /// Demonstrates that using mapped column names for fields within the GraphQL mutation results in successful engine processing
        /// of the column2 value update for the record where column1 = $id.
        /// </summary>
        [TestMethod]
        public async Task UpdateMutationWithVariablesAndMappings()
        {
            string graphQLMutationName = "updateGQLmappings";
            string graphQLMutation = @"
                mutation($id: Int!, $col2Value: String) {
                    updateGQLmappings(column1: $id, item: { column2: $col2Value }) {
                        result
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true, new() { { "id", 3 }, { "col2Value", "Updated Value of Mapped Column" } });
            Assert.IsTrue(actual.ToString().Contains("success"), "DwSql insert mutation with variables and mappings should be successful");
        }

        /// <summary>
        /// <code>Do: </code>Update book in database and return the typename of the entity
        /// <code>Check: </code>if the mutation executed successfully and returned the correct typename
        /// </summary>
        [TestMethod]
        public override async Task UpdateMutationWithOnlyTypenameInSelectionSet()
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
                ""__typename"": ""DbOperationResult""
              }
            ";

            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// Demonstrates that using mapped column names for fields within the GraphQL mutation results in successful engine processing
        /// of removal of the record where column1 = $id.
        /// Result value of success is verified in the response.
        /// </summary>
        [TestMethod]
        public async Task DeleteMutationWithVariablesAndMappings()
        {
            string msSqlQueryToVerifyDeletion = @"
                SELECT COUNT(*) AS count
                FROM [GQLmappings]
                WHERE [__column1] = 4
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string graphQLMutationName = "deleteGQLmappings";
            string graphQLMutation = @"
                mutation($id: Int!) {
                    deleteGQLmappings(column1: $id) {
                        result
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true, new() { { "id", 4 } });

            Assert.IsTrue(actual.ToString().Contains("success"), "DwSql delete with variables and mappings should be successful");

            string dbResponse = await GetDatabaseResultAsync(msSqlQueryToVerifyDeletion);

            using JsonDocument result = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(result.RootElement.GetProperty("count").GetInt64(), 0);
        }

        /// <summary>
        /// <code>Do: </code>Delete an entry from a simple view
        /// <code>Check: </code>Result value of success is verified in the response and if the entry of that id has been deleted
        /// </summary>
        [TestMethod]
        public async Task DeleteFromSimpleView()
        {
            string msSqlQueryToVerifyDeletion = @"
                SELECT COUNT(*) AS count
                FROM [books_view_all]
                WHERE [id] = 1
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES,
                    WITHOUT_ARRAY_WRAPPER
            ";

            string graphQLMutationName = "deletebooks_view_all";
            string graphQLMutation = @"
                mutation {
                    deletebooks_view_all(id: 1) {
                        result
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);

            Assert.IsTrue(actual.ToString().Contains("success"), "DwSql delete from view should be successful");

            // check if entry is actually deleted
            string dbResponse = await GetDatabaseResultAsync(msSqlQueryToVerifyDeletion);

            using JsonDocument result = JsonDocument.Parse(dbResponse);
            Assert.AreEqual(result.RootElement.GetProperty("count").GetInt64(), 0);
        }

        /// <summary>
        /// <code>Do :</code> Delete a book from database and return the typename
        /// <code>Check :</code>if the mutation executed successfully and returned the correct typename
        /// </summary>
        [TestMethod]
        public override async Task DeleteMutationWithOnlyTypename()
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
                ""__typename"": ""DbOperationResult""
              }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.PerformTestEqualJsonStrings(expected, actual.ToString());
        }

        /// <summary>
        /// <code>Do: </code>insert a new book and return all the books with same publisher
        /// <code>Check: </code>the intended book is inserted.
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureMutationNonEmptyResponse()
        {
            string dbQuery = @"
SELECT CASE
         WHEN EXISTS (
           SELECT 1
           FROM [books] AS [table0]
           JOIN (
             SELECT id
             FROM [publishers]
             WHERE name = 'Big Company'
           ) AS [table1] ON [table0].publisher_id = [table1].id
         ) THEN 'true' ELSE 'false'
       END AS IsInserted
            ";

            string graphQLMutationName = "executeInsertAndDisplayAllBooksUnderGivenPublisher";
            string graphQLMutation = @"
                mutation{
                    executeInsertAndDisplayAllBooksUnderGivenPublisher(book_id: 1, title: ""Orange Tomato"" publisher_name: ""Big Company""){
                        id
                        title
                    }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string insertedValue = "{\"id\":1,\"title\":\"Orange Tomato\"}";
            string expected = await GetDatabaseResultAsync(dbQuery);
            Assert.IsTrue(actual.ToString().Contains(insertedValue));
            Assert.IsTrue(expected.ToString().Equals("True"));
        }

        /// <summary>
        /// <code>Do: </code>insert new Book and return an empty array.
        /// <code>Check: </code>if the intended book is inserted in books table
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureMutationForInsertion()
        {
            string msSqlQuery = @"
SELECT CASE 
         WHEN EXISTS (
           SELECT 1
           FROM [books] AS [table0]
           WHERE [table0].[title] = 'Random Book'
             AND [table0].[publisher_id] = 1234
         ) THEN 'true' 
         ELSE 'false'
       END AS BookExists
            ";

            string graphQLMutationName = "executeInsertBook";
            string graphQLMutation = @"
                mutation {
                    executeInsertBook(book_id: 10, title: ""Random Book"", publisher_id: 1234 ) {
                        result
                    }
                }
            ";

            string currentDbResponse = await GetDatabaseResultAsync(msSqlQuery);
            Assert.AreEqual(currentDbResponse, "False", "Entry should not exist in the db before insertion");
            JsonElement graphQLResponse = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);

            // Stored Procedure didn't return anything
            SqlTestHelper.PerformTestEqualJsonStrings("[]", graphQLResponse.ToString());

            // check to verify new element is inserted
            string updatedDbResponse = await GetDatabaseResultAsync(msSqlQuery);
            Assert.AreEqual(updatedDbResponse, "True", "Entry should be inserted into db.");
        }

        /// <summary>
        /// <code>Do: </code>Update book title and return the updated row
        /// <code>Check: </code>if the result returned from the mutation is correct and
        /// updated value exists in the database
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureMutationForUpdate()
        {
            string dbQuery = @"
SELECT CASE
         WHEN EXISTS (
           SELECT 1
           FROM [books] AS [table0]
           WHERE [table0].[id] = 14
             AND [table0].[publisher_id] = 1234
             AND [table0].[title] = 'Before Midnight'
         ) THEN 'true' ELSE 'false'
       END AS BookExists
            ";

            string graphQLMutationName = "executeUpdateBookTitle";
            string graphQLMutation = @"
                mutation {
                    executeUpdateBookTitle(id: 14, title: ""Before Midnight"") {
                        id
                        title
                    }
                }
            ";

            JsonElement graphQLResponse = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            string updatedValueExists = await GetDatabaseResultAsync(dbQuery);
            string insertedValue = "{\"id\":14,\"title\":\"Before Midnight\"}";
            Assert.IsTrue(updatedValueExists.Equals("True"), "Title should be updated for given book id.");
            Assert.IsTrue(graphQLResponse.ToString().Contains(insertedValue), "GraphQL response should contain the updated entry");
        }

        /// <summary>
        /// <code>Do: </code>deletes a Book and return an empty array.
        /// <code>Check: </code>the intended book is deleted
        /// </summary>
        [TestMethod]
        public async Task TestStoredProcedureMutationForDeletion()
        {
            string dbQueryToVerifyDeletion = @"
SELECT CAST(MAX(table0.id) AS VARCHAR) AS [maxId]
FROM [books] AS [table0]
            ";

            string graphQLMutationName = "executeDeleteLastInsertedBook";
            string graphQLMutation = @"
                mutation {
                    executeDeleteLastInsertedBook {
                        result
                    }
                }
            ";

            string maxIdBeforeDeletion = await GetDatabaseResultAsync(dbQueryToVerifyDeletion);
            JsonElement graphQLResponse = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);

            // Stored Procedure didn't return anything
            Assert.AreEqual("[]", graphQLResponse.ToString());

            // check to verify new element is deleted
            string maxIdAfterDeletion = await GetDatabaseResultAsync(dbQueryToVerifyDeletion);
            Assert.AreNotEqual(maxIdBeforeDeletion, maxIdAfterDeletion, "Last inserted book should be deleted");
        }

        #endregion

        #region Negative Tests

        /// <summary>
        /// <code>Do: </code>use an update mutation without passing any of the optional new values to update
        /// <code>Check: </code>check that GraphQL returns an appropriate exception to the user
        /// </summary>
        [TestMethod]
        public override async Task UpdateWithNoNewValues()
        {
            string graphQLMutationName = "updatebook";
            string graphQLMutation = @"
                mutation {
                    updatebook(id: 1) {
                        result
                    }
                }
            ";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), message: $"The argument `item` is required");
        }

        /// <summary>
        /// <code>Do: </code>use an update mutation with an invalid id to update
        /// <code>Check: </code>check that GraphQL returns an appropriate exception to the user
        /// </summary>
        [TestMethod]
        public override async Task UpdateWithInvalidIdentifier()
        {
            string graphQLMutationName = "updatebook";
            string graphQLMutation = @"
                mutation {
                    updatebook(id: -1, item: { title: ""Even Better Title"" }) {
                        result
                    }
                }
            ";

            JsonElement result = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.TestForErrorInGraphQLResponse(result.ToString(), statusCode: $"{DataApiBuilderException.SubStatusCodes.ItemNotFound}");
        }

        /// <summary>
        /// Performs concurrent delete mutations on the same item and validates that only one of the
        /// requests is successful.
        /// </summary>
        [TestMethod]
        public override async Task TestParallelDeleteMutations()
        {
            string graphQLMutationName = "deletebook";

            string graphQLMutation1 = @"
                mutation {
                  deletebook (id: 1){
                    result
                  }
                }
            ";

            Task<JsonElement> responseTask1 = ExecuteGraphQLRequestAsync(graphQLMutation1, graphQLMutationName, isAuthenticated: true);
            Task<JsonElement> responseTask2 = ExecuteGraphQLRequestAsync(graphQLMutation1, graphQLMutationName, isAuthenticated: true);

            JsonElement response1 = await responseTask1;
            JsonElement response2 = await responseTask2;

            string responseString1 = response1.ToString();
            string responseString2 = response2.ToString();
            string expectedResponse = "{\"result\":\"success\"}";
            string expectedErrorResponse = "{\"result\":\"item not found\"}";

            // The mutation request that deletes the item is expected to have a valid response
            // and the other mutation is expected to receive an empty response as it
            // won't see the item in the table.
            if (responseString1 == expectedResponse)
            {
                Assert.AreEqual(expectedErrorResponse, responseString2);
            }
            else if (responseString2 == expectedResponse)
            {
                Assert.AreEqual(expectedErrorResponse, responseString1);
            }
            else
            {
                Assert.Fail("Unexpected failure. Atleast one of the delete mutations should've succeeded");
            }

        }

        /// <summary>
        /// Performs concurrent update mutations on the same item and validates that the responses
        /// returned are consistent
        /// gQLMutation1 : Updates the title column of Book table to New Title
        /// gQLMutation2 : Updates the title column of Book table to Updated Title
        /// </summary>
        [TestMethod]
        public override async Task TestParallelUpdateMutations()
        {
            string graphQLMutationName = "updatebook";
            string gQLMutation1 = @"
                mutation {
                    updatebook(id : 1, item: { title: ""New Title"" })
                    {
                        result
                    }
                }";

            string gQLMutation2 = @"
                mutation {
                    updatebook(id : 1, item: { title: ""Updated Title"" })
                    {
                        result
                    }
                }";

            Task<JsonElement> responeTask1 = ExecuteGraphQLRequestAsync(gQLMutation1, graphQLMutationName, isAuthenticated: true);
            Task<JsonElement> responseTask2 = ExecuteGraphQLRequestAsync(gQLMutation2, graphQLMutationName, isAuthenticated: true);

            JsonElement response1 = await responeTask1;
            JsonElement response2 = await responseTask2;

            Assert.AreEqual("{\"result\":\"success\"}", response1.ToString());
            Assert.AreEqual("{\"result\":\"success\"}", response2.ToString());
        }

        [TestMethod]
        [Ignore]
        /// <summary>
        /// Database policy for create operation is only supported for mssql.
        /// </summary>
        public override Task TestDbPolicyForCreateOperationReferencingFieldAbsentInRequest()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Default is not supported in DW.
        /// </summary>
        [TestMethod]
        [Ignore]
        public override Task TestTryInsertMutationForVariableNotNullDefault()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Identity columns and primary key not supported in dw.
        /// </summary>
        [TestMethod]
        [Ignore]
        public override Task TestParallelInsertMutationPKNonAutoGenerated()
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Identity columns and primary key not supported in dw.
        /// </summary>
        [TestMethod]
        [Ignore]
        public override Task TestParallelInsertMutationPKAutoGenerated()
        {
            throw new NotImplementedException();
        }
        #endregion
    }
}
