// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLMutationTests.MultipleCreateMutationTests
{
    /// <summary>
    /// Test class for GraphQL Multiple Create Mutation tests against MsSQL database type.
    /// </summary>

    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MsSqlMultipleCreateMutationTests : MultipleCreateMutationTestBase
    {

        #region Test Fixture Setup
        /// <summary>
        /// Set the database engine for the tests
        /// </summary>
        [ClassInitialize]
        public static async Task SetupAsync(TestContext context)
        {
            DatabaseEngine = TestCategory.MSSQL;
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

        [TestMethod]
        public async Task MultipleCreateMutationWithManyToOneRelationship()
        {
            string dbQuery = @"SELECT TOP 1 [table0].[id] AS [id], [table0].[title] AS [title], [table0].[publisher_id] AS [publisher_id],
                               JSON_QUERY ([table1_subq].[data]) AS [publishers] FROM [dbo].[books] AS [table0]
                               OUTER APPLY (SELECT TOP 1 [table1].[id] AS [id], [table1].[name] AS [name] FROM [dbo].[publishers] AS [table1]
                               WHERE [table0].[publisher_id] = [table1].[id]
                               ORDER BY [table1].[id] ASC FOR JSON PATH, INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER)
                               AS [table1_subq]([data])
                               WHERE [table0].[id] = 5001 AND [table0].[title] = 'Book #1' 
                               ORDER BY [table0].[id] ASC FOR JSON PATH, INCLUDE_NULL_VALUES,WITHOUT_ARRAY_WRAPPER";

            await MultipleCreateMutationWithManyToOneRelationship(dbQuery);
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithOneToManyRelationship()
        {
            string expectedResponse = @"{
                                          ""id"": 5001,
                                          ""title"": ""Book #1"",
                                          ""publisher_id"": 1234,
                                          ""reviews"": {
                                            ""items"": [
                                              {
                                                ""book_id"": 5001,
                                                ""id"": 5001,
                                                ""content"": ""Book #1 - Review #1""
                                              },
                                              {
                                                ""book_id"": 5001,
                                                ""id"": 5002,
                                                ""content"": ""Book #1 - Review #2""
                                              }
                                            ]
                                          }
                                        }";

            await MultipleCreateMutationWithOneToManyRelationship(expectedResponse);
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithManyToManyRelationship()
        {
            string expectedResponse = @"{
                                          ""id"": 5001,
                                          ""title"": ""Book #1"",
                                          ""publisher_id"": 1234,
                                          ""authors"": {
                                            ""items"": [
                                              {
                                                ""id"": 5001,
                                                ""name"": ""Author #1"",
                                                ""birthdate"": ""2000-01-01""
                                              },
                                              {
                                                ""id"": 5002,
                                                ""name"": ""Author #2"",
                                                ""birthdate"": ""2000-02-03""
                                              }
                                            ]
                                          }
                                        }";

            string linkingTableDbValidationQuery = @"SELECT [book_id], [author_id], [royalty_percentage]
                                                     FROM [dbo].[book_author_link] 
                                                     WHERE [dbo].[book_author_link].[book_id] = 5001 AND ([dbo].[book_author_link].[author_id] = 5001 OR [dbo].[book_author_link].[author_id] = 5002) 
                                                     ORDER BY [dbo].[book_author_link].[book_id], [dbo].[book_author_link].[author_id] ASC FOR JSON PATH, INCLUDE_NULL_VALUES;";

            string expectedResponseFromLinkingTable = @"[{""book_id"":5001,""author_id"":5001,""royalty_percentage"":50.0},{""book_id"":5001,""author_id"":5002,""royalty_percentage"":50.0}]";

            await MultipleCreateMutationWithManyToManyRelationship(expectedResponse, linkingTableDbValidationQuery, expectedResponseFromLinkingTable);
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithOneToOneRelationship()
        {
            string expectedResponse = @" {                                            
                                            ""categoryid"": 101,
                                            ""pieceid"": 101,
                                            ""categoryName"": ""SciFi"",
                                            ""piecesAvailable"": 100,
                                            ""piecesRequired"": 50,
                                            ""stocks_price"": {
                                                ""categoryid"": 101,
                                                ""pieceid"": 101,
                                                ""instant"": ""2024-04-02"",
                                                ""price"": 75,
                                                ""is_wholesale_price"": true
                                            }
                                        }";

            await MultipleCreateMutationWithOneToOneRelationship(expectedResponse);
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithAllRelationshipTypes()
        {
            string expectedResponse = @"{
                                            ""id"": 5001,
                                            ""title"": ""Book #1"",
                                            ""publishers"": {
                                            ""id"": 5001,
                                            ""name"": ""Publisher #1""
                                            },
                                            ""reviews"": {
                                            ""items"": [
                                                {
                                                ""book_id"": 5001,
                                                ""id"": 5001,
                                                ""content"": ""Book #1 - Review #1"",
                                                ""website_users"": {
                                                    ""id"": 5001,
                                                    ""username"": ""WebsiteUser #1""
                                                }
                                                },
                                                {
                                                ""book_id"": 5001,
                                                ""id"": 5002,
                                                ""content"": ""Book #1 - Review #2"",
                                                ""website_users"": {
                                                    ""id"": 1,
                                                    ""username"": ""George""
                                                }
                                                }
                                            ]
                                            },
                                            ""authors"": {
                                            ""items"": [
                                                {
                                                ""id"": 5001,
                                                ""name"": ""Author #1"",
                                                ""birthdate"": ""2000-02-01""
                                                },
                                                {
                                                ""id"": 5002,
                                                ""name"": ""Author #2"",
                                                ""birthdate"": ""2000-01-02""
                                                }
                                            ]
                                        }
                                    }";

            string linkingTableDbValidationQuery = @"SELECT [book_id], [author_id], [royalty_percentage]
                                                     FROM [dbo].[book_author_link] 
                                                     WHERE [dbo].[book_author_link].[book_id] = 5001 AND ([dbo].[book_author_link].[author_id] = 5001 OR [dbo].[book_author_link].[author_id] = 5002) 
                                                     ORDER BY [dbo].[book_author_link].[book_id], [dbo].[book_author_link].[author_id] ASC FOR JSON PATH, INCLUDE_NULL_VALUES;";

            string expectedResponseFromLinkingTable = @"[{""book_id"":5001,""author_id"":5001,""royalty_percentage"":50.0},{""book_id"":5001,""author_id"":5002,""royalty_percentage"":50.0}]";

            await MultipleCreateMutationWithAllRelationshipTypes(expectedResponse, linkingTableDbValidationQuery, expectedResponseFromLinkingTable);
        }

        [TestMethod]
        public async Task ManyTypeMultipleCreateMutationOperation()
        {
            string expectedResponse = @"{
                                          ""items"": [
                                            {
                                              ""id"": 5001,
                                              ""title"": ""Book #1"",
                                              ""publisher_id"": 5001,
                                              ""publishers"": {
                                                ""id"": 5001,
                                                ""name"": ""Publisher #1""
                                              },
                                              ""reviews"": {
                                                ""items"": [
                                                  {
                                                    ""book_id"": 5001,
                                                    ""id"": 5001,
                                                    ""content"": ""Book #1 - Review #1"",
                                                    ""website_users"": {
                                                      ""id"": 5001,
                                                      ""username"": ""Website user #1""
                                                    }
                                                  },
                                                  {
                                                    ""book_id"": 5001,
                                                    ""id"": 5002,
                                                    ""content"": ""Book #1 - Review #2"",
                                                    ""website_users"": {
                                                      ""id"": 4,
                                                      ""username"": ""book_lover_95""
                                                    }
                                                  }
                                                ]
                                              },
                                              ""authors"": {
                                                ""items"": [
                                                  {
                                                    ""id"": 5001,
                                                    ""name"": ""Author #1"",
                                                    ""birthdate"": ""2000-01-02""
                                                  },
                                                  {
                                                    ""id"": 5002,
                                                    ""name"": ""Author #2"",
                                                    ""birthdate"": ""2001-02-03""
                                                  }
                                                ]
                                              }
                                            },
                                            {
                                              ""id"": 5002,
                                              ""title"": ""Book #2"",
                                              ""publisher_id"": 1234,
                                              ""publishers"": {
                                                ""id"": 1234,
                                                ""name"": ""Big Company""
                                              },
                                              ""reviews"": {
                                                ""items"": []
                                              },
                                              ""authors"": {
                                                ""items"": [
                                                  {
                                                    ""id"": 5003,
                                                    ""name"": ""Author #3"",
                                                    ""birthdate"": ""2000-01-02""
                                                  },
                                                  {
                                                    ""id"": 5004,
                                                    ""name"": ""Author #4"",
                                                    ""birthdate"": ""2001-02-03""
                                                  }
                                                ]
                                              }
                                            }
                                          ]
                                        }";

            string linkingTableDbValidationQuery = @"SELECT [book_id], [author_id], [royalty_percentage] FROM [dbo].[book_author_link] 
                                                    WHERE ( [dbo].[book_author_link].[book_id] = 5001 AND ([dbo].[book_author_link].[author_id] = 5001 OR [dbo].[book_author_link].[author_id] = 5002)) 
                                                        OR ([dbo].[book_author_link].[book_id] = 5002 AND ([dbo].[book_author_link].[author_id] = 5003 OR [dbo].[book_author_link].[author_id] = 5004))
                                                    ORDER BY [dbo].[book_author_link].[book_id] ASC FOR JSON PATH, INCLUDE_NULL_VALUES;";

            string expectedResponseFromLinkingTable = @"[{""book_id"":5001,""author_id"":5001,""royalty_percentage"":50.0},{""book_id"":5001,""author_id"":5002,""royalty_percentage"":50.0},{""book_id"":5002,""author_id"":5003,""royalty_percentage"":65.0},{""book_id"":5002,""author_id"":5004,""royalty_percentage"":35.0}]";

            await ManyTypeMultipleCreateMutationOperation(expectedResponse, linkingTableDbValidationQuery, expectedResponseFromLinkingTable);
        }

        /// <summary>
        /// Point multiple create mutation request is executed with the role: role_multiple_create_policy_tester. This role has the following create policy defined on "Book" entity: "@item.title ne 'Test'"
        /// Because this mutation tries to create a book with title "Test", it is expected to fail with a database policy violation error. The error message and status code are validated for accuracy.
        /// </summary>
        [TestMethod]
        public async Task PointMultipleCreateFailsDueToCreatePolicyViolationAtTopLevelEntity()
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"mutation{
                                          createbook(item:{
                                            title: ""Test"",
                                            publishers:{
                                              name: ""Publisher #1""
                                            }
                                          }){
                                            id
                                            title
                                            publishers{
                                              id
                                              name
                                            }
                                          }
                                        }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true, clientRoleHeader: "role_multiple_create_policy_tester");

            string expectedErrorMessage = "Could not insert row with given values for entity: Book";

            SqlTestHelper.TestForErrorInGraphQLResponse(
                response: actual.ToString(),
                message: expectedErrorMessage,
                statusCode: $"{DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure}"
            );

            // Validate that no book item is created
            string dbQuery = @"
                SELECT *
                FROM [books] AS [table0]
                WHERE [table0].[id] = 5001
                ORDER BY [id] asc
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            string dbResponse = await GetDatabaseResultAsync(dbQuery);
            Assert.AreEqual("[]", dbResponse);

            // Validate that no publisher item is created
            dbQuery = @"
                SELECT *
                FROM [publishers] AS [table0]
                WHERE [table0].[id] = 5001
                ORDER BY [id] asc
                FOR JSON PATH, INCLUDE_NULL_VALUES";
            dbResponse = await GetDatabaseResultAsync(dbQuery);
            Assert.AreEqual("[]", dbResponse);
        }

        /// <summary>
        /// Point multiple create mutation request is executed with the role: role_multiple_create_policy_tester. This role has the following create policy defined on "Publisher" entity: "@item.name ne 'Test'"
        /// Because this mutation tries to create a publisher with title "Test" (along with creating a book item), it is expected to fail with a database policy violation error.
        /// As a result of this mutation, no Book and Publisher item should be created.  
        /// The error message and status code are validated for accuracy. Also, the database is queried to ensure that no new record got created.
        /// </summary>
        [TestMethod]
        public async Task PointMultipleCreateFailsDueToCreatePolicyViolationAtRelatedEntity()
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"mutation{
                                          createbook(item:{
                                            title: ""Book #1"",
                                            publishers:{
                                              name: ""Test""
                                            }
                                          }){
                                            id
                                            title
                                            publishers{
                                              id
                                              name
                                            }
                                          }
                                        }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true, clientRoleHeader: "role_multiple_create_policy_tester");

            string expectedErrorMessage = "Could not insert row with given values for entity: Publisher";

            SqlTestHelper.TestForErrorInGraphQLResponse(
                response: actual.ToString(),
                message: expectedErrorMessage,
                statusCode: $"{DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure}"
            );

            // Validate that no book item is created
            string dbQuery = @"
                SELECT *
                FROM [books] AS [table0]
                WHERE [table0].[id] = 5001
                ORDER BY [id] asc
                FOR JSON PATH, INCLUDE_NULL_VALUES";

            string dbResponse = await GetDatabaseResultAsync(dbQuery);
            Assert.AreEqual("[]", dbResponse);

            // Validate that no publisher item is created
            dbQuery = @"
                SELECT *
                FROM [publishers] AS [table0]
                WHERE [table0].[id] = 5001
                ORDER BY [id] asc
                FOR JSON PATH, INCLUDE_NULL_VALUES";
            dbResponse = await GetDatabaseResultAsync(dbQuery);
            Assert.AreEqual("[]", dbResponse);
        }

        /// <summary>
        /// Many type multiple create mutation request is executed with the role: role_multiple_create_policy_tester. This role has the following create policy defined on "Book" entity: "@item.title ne 'Test'"
        /// In this request, the second Book item in the input violates the create policy defined. Processing of that input item is expected to result in database policy violation error.
        /// All the items created successfully prior to this fault input will also be rolled back. So, the end result is that no new items should be created. 
        /// </summary>
        [TestMethod]
        public async Task ManyTypeMultipleCreateFailsDueToCreatePolicyFailure()
        {
            string graphQLMutationName = "createbooks";
            string graphQLMutation = @"mutation {
                                          createbooks(
                                            items: [
                                              { title: ""Book #1"", publisher_id: 2345 }
                                              { title: ""Test"", publisher_id: 2345 }
                                            ]
                                          ) {
                                            items {
                                              id
                                              title
                                              publishers {
                                                id
                                                name
                                              }
                                            }
                                          }
                                        }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true, clientRoleHeader: "role_multiple_create_policy_tester");

            string expectedErrorMessage = "Could not insert row with given values for entity: Book";

            SqlTestHelper.TestForErrorInGraphQLResponse(
                response: actual.ToString(),
                message: expectedErrorMessage,
                statusCode: $"{DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure}");

            // Validate that no book item is created
            string dbQuery = @"
                SELECT *
                FROM [books] AS [table0]
                WHERE [table0].[id] >= 5001
                ORDER BY [id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES";

            string dbResponse = await GetDatabaseResultAsync(dbQuery);
            Assert.AreEqual("[]", dbResponse);

            // Validate that no publisher item is created
            dbQuery = @"
                SELECT *
                FROM [publishers] AS [table0]
                WHERE [table0].[id] >= 5001
                ORDER BY [id] asc
                FOR JSON PATH,
                    INCLUDE_NULL_VALUES";

            dbResponse = await GetDatabaseResultAsync(dbQuery);
            Assert.AreEqual("[]", dbResponse);
        }

        /// <summary>
        /// Point type multiple create mutation request is executed with the role: role_multiple_create_policy_tester. This role has the following create policy defined on "Reviews" entity: "@item.websiteuser_id ne 1".
        /// In this request, the second Review item in the input violates the read policy defined. Hence, it is not to be returned in the response.
        /// The returned response is validated against an expected response for correctness.
        /// </summary>
        [TestMethod]
        public async Task PointMultipleCreateMutationWithReadPolicyViolationAtRelatedEntity()
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"mutation {
                                          createbook(
                                            item: {
                                              title: ""Book #1""
                                              publisher_id: 2345
                                              reviews: [
                                                {
                                                  content: ""Review #1"",
                                                  websiteuser_id: 4
                                                }
                                                { content: ""Review #2"",
                                                  websiteuser_id: 1
                                                }
                                              ]
                                            }
                                          ) {
                                            id
                                            title
                                            publisher_id
                                            reviews {
                                              items {
                                                book_id
                                                id
                                                content
                                                websiteuser_id
                                              }
                                            }
                                          }
                                        }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true, clientRoleHeader: "role_multiple_create_policy_tester");

            string expectedResponse = @"{
                                          ""id"": 5001,
                                          ""title"": ""Book #1"",
                                          ""publisher_id"": 2345,
                                          ""reviews"": {
                                            ""items"": [
                                              {
                                                ""book_id"": 5001,
                                                ""id"": 5001,
                                                ""content"": ""Review #1"",
                                                ""websiteuser_id"": 4
                                              }
                                            ]
                                          }
                                        }";

            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponse, actual.ToString());
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithOneToOneRelationshipDefinedInConfigFile()
        {
            string expectedResponse1 = @"{
                                          ""userid"": 3,
                                          ""username"": ""DAB"",
                                          ""email"": ""dab@microsoft.com"",
                                          ""UserProfile_NonAutogenRelationshipColumn"": {
                                            ""profileid"": 3,
                                            ""userid"": 10,
                                            ""username"": ""DAB"",
                                            ""profilepictureurl"": ""dab/profilepicture""
                                          }
                                        }";

            string expectedResponse2 = @"{
                                          ""userid"": 4,
                                          ""username"": ""DAB2"",
                                          ""email"": ""dab@microsoft.com"",
                                          ""UserProfile_NonAutogenRelationshipColumn"": {
                                            ""profileid"": 4,
                                            ""userid"": 10,
                                            ""username"": ""DAB2"",
                                            ""profilepictureurl"": ""dab/profilepicture""
                                          }
                                        }";

            await MultipleCreateMutationWithOneToOneRelationshipDefinedInConfigFile(expectedResponse1, expectedResponse2);
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithManyToOneRelationshipDefinedInConfigFile()
        {
            string expectedResponse = @"{
                                          ""id"": 5001,
                                          ""title"": ""Book #1"",
                                          ""publisher_id"": 5001,
                                          ""publishers"": {
                                          ""id"": 5001,
                                          ""name"": ""Publisher #1""
                                            }
                                        }";

            await MultipleCreateMutationWithManyToOneRelationshipDefinedInConfigFile(expectedResponse);
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithOneToManyRelationshipDefinedInConfigFile()
        {
            string expectedResponse = @"{
                                          ""id"": 5001,
                                          ""title"": ""Book #1"",
                                          ""publisher_id"": 1234,
                                          ""reviews"": {
                                            ""items"": [
                                              {
                                                ""book_id"": 5001,
                                                ""id"": 5001,
                                                ""content"": ""Book #1 - Review #1""
                                              },
                                              {
                                                ""book_id"": 5001,
                                                ""id"": 5002,
                                                ""content"": ""Book #1 - Review #2""
                                              }
                                            ]
                                          }
                                        }";

            await MultipleCreateMutationWithOneToManyRelationshipDefinedInConfigFile(expectedResponse);
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithManyToManyRelationshipDefinedInConfigFile()
        {
            string expectedResponse = @"{
                                          ""id"": 5001,
                                          ""title"": ""Book #1"",
                                          ""publisher_id"": 1234,
                                          ""authors"": {
                                            ""items"": [
                                              {
                                                ""id"": 5001,
                                                ""name"": ""Author #1"",
                                                ""birthdate"": ""2000-01-01""
                                              },
                                              {
                                                ""id"": 5002,
                                                ""name"": ""Author #2"",
                                                ""birthdate"": ""2000-02-03""
                                              }
                                            ]
                                          }
                                        }";

            string linkingTableDbValidationQuery = @"SELECT [book_id], [author_id], [royalty_percentage]
                                                     FROM [dbo].[book_author_link_mm] 
                                                     WHERE [dbo].[book_author_link_mm].[book_id] = 5001 AND ([dbo].[book_author_link_mm].[author_id] = 5001 OR [dbo].[book_author_link_mm].[author_id] = 5002) 
                                                     ORDER BY [dbo].[book_author_link_mm].[book_id], [dbo].[book_author_link_mm].[author_id] ASC FOR JSON PATH, INCLUDE_NULL_VALUES;";

            string expectedResponseFromLinkingTable = @"[{""book_id"":5001,""author_id"":5001,""royalty_percentage"":50.0},{""book_id"":5001,""author_id"":5002,""royalty_percentage"":50.0}]";

            await MultipleCreateMutationWithManyToManyRelationshipDefinedInConfigFile(expectedResponse, linkingTableDbValidationQuery, expectedResponseFromLinkingTable);
        }

        [TestMethod]
        public async Task MultipleCreateMutationWithAllRelationshipTypesDefinedInConfigFile()
        {
            string expectedResponse = @"{
                                            ""id"": 5001,
                                            ""title"": ""Book #1"",
                                            ""publishers"": {
                                            ""id"": 5001,
                                            ""name"": ""Publisher #1""
                                            },
                                            ""reviews"": {
                                            ""items"": [
                                                {
                                                ""book_id"": 5001,
                                                ""id"": 5001,
                                                ""content"": ""Book #1 - Review #1"",
                                                ""website_users"": {
                                                    ""id"": 5001,
                                                    ""username"": ""WebsiteUser #1""
                                                }
                                                },
                                                {
                                                ""book_id"": 5001,
                                                ""id"": 5002,
                                                ""content"": ""Book #1 - Review #2"",
                                                ""website_users"": {
                                                    ""id"": 1,
                                                    ""username"": ""George""
                                                }
                                                }
                                            ]
                                            },
                                            ""authors"": {
                                            ""items"": [
                                                {
                                                ""id"": 5001,
                                                ""name"": ""Author #1"",
                                                ""birthdate"": ""2000-02-01""
                                                },
                                                {
                                                ""id"": 5002,
                                                ""name"": ""Author #2"",
                                                ""birthdate"": ""2000-01-02""
                                                }
                                            ]
                                        }
                                    }";

            string linkingTableDbValidationQuery = @"SELECT [book_id], [author_id], [royalty_percentage]
                                                     FROM [dbo].[book_author_link_mm] 
                                                     WHERE [dbo].[book_author_link_mm].[book_id] = 5001 AND ([dbo].[book_author_link_mm].[author_id] = 5001 OR [dbo].[book_author_link_mm].[author_id] = 5002) 
                                                     ORDER BY [dbo].[book_author_link_mm].[book_id], [dbo].[book_author_link_mm].[author_id] ASC FOR JSON PATH, INCLUDE_NULL_VALUES;";

            string expectedResponseFromLinkingTable = @"[{""book_id"":5001,""author_id"":5001,""royalty_percentage"":50.0},{""book_id"":5001,""author_id"":5002,""royalty_percentage"":50.0}]";

            await MultipleCreateMutationWithAllRelationshipTypesDefinedInConfigFile(expectedResponse, linkingTableDbValidationQuery, expectedResponseFromLinkingTable);
        }

        [TestMethod]
        public async Task ManyTypeMultipleCreateMutationOperationRelationshipsDefinedInConfig()
        {
            string expectedResponse = @"{
                                          ""items"": [
                                            {
                                              ""id"": 5001,
                                              ""title"": ""Book #1"",
                                              ""publisher_id"": 5001,
                                              ""publishers"": {
                                                ""id"": 5001,
                                                ""name"": ""Publisher #1""
                                              },
                                              ""reviews"": {
                                                ""items"": [
                                                  {
                                                    ""book_id"": 5001,
                                                    ""id"": 5001,
                                                    ""content"": ""Book #1 - Review #1"",
                                                    ""website_users"": {
                                                      ""id"": 5001,
                                                      ""username"": ""Website user #1""
                                                    }
                                                  },
                                                  {
                                                    ""book_id"": 5001,
                                                    ""id"": 5002,
                                                    ""content"": ""Book #1 - Review #2"",
                                                    ""website_users"": {
                                                      ""id"": 4,
                                                      ""username"": ""book_lover_95""
                                                    }
                                                  }
                                                ]
                                              },
                                              ""authors"": {
                                                ""items"": [
                                                  {
                                                    ""id"": 5001,
                                                    ""name"": ""Author #1"",
                                                    ""birthdate"": ""2000-01-02""
                                                  },
                                                  {
                                                    ""id"": 5002,
                                                    ""name"": ""Author #2"",
                                                    ""birthdate"": ""2001-02-03""
                                                  }
                                                ]
                                              }
                                            },
                                            {
                                              ""id"": 5002,
                                              ""title"": ""Book #2"",
                                              ""publisher_id"": 1234,
                                              ""publishers"": {
                                                ""id"": 1234,
                                                ""name"": ""Big Company""
                                              },
                                              ""reviews"": {
                                                ""items"": []
                                              },
                                              ""authors"": {
                                                ""items"": [
                                                  {
                                                    ""id"": 5003,
                                                    ""name"": ""Author #3"",
                                                    ""birthdate"": ""2000-01-02""
                                                  },
                                                  {
                                                    ""id"": 5004,
                                                    ""name"": ""Author #4"",
                                                    ""birthdate"": ""2001-02-03""
                                                  }
                                                ]
                                              }
                                            }
                                          ]
                                        }";

            string linkingTableDbValidationQuery = @"SELECT [book_id], [author_id], [royalty_percentage] FROM [dbo].[book_author_link_mm] 
                                                    WHERE ( [dbo].[book_author_link_mm].[book_id] = 5001 AND ([dbo].[book_author_link_mm].[author_id] = 5001 OR [dbo].[book_author_link_mm].[author_id] = 5002)) 
                                                        OR ([dbo].[book_author_link_mm].[book_id] = 5002 AND ([dbo].[book_author_link_mm].[author_id] = 5003 OR [dbo].[book_author_link_mm].[author_id] = 5004))
                                                    ORDER BY [dbo].[book_author_link_mm].[book_id] ASC FOR JSON PATH, INCLUDE_NULL_VALUES;";

            string expectedResponseFromLinkingTable = @"[{""book_id"":5001,""author_id"":5001,""royalty_percentage"":50.0},{""book_id"":5001,""author_id"":5002,""royalty_percentage"":50.0},{""book_id"":5002,""author_id"":5003,""royalty_percentage"":65.0},{""book_id"":5002,""author_id"":5004,""royalty_percentage"":35.0}]";

            await ManyTypeMultipleCreateMutationOperationRelationshipsDefinedInConfig(expectedResponse, linkingTableDbValidationQuery, expectedResponseFromLinkingTable);
        }
    }
}
