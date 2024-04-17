// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Exceptions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.SqlTests.GraphQLMutationTests.MultipleCreateMutationTests
{
    /// <summary>
    /// Base class for GraphQL Multiple Create Mutation tests.
    /// </summary>
    [TestClass]
    public abstract class MultipleCreateMutationTestBase : SqlTestBase
    {

        #region Relationships defined through database metadata

        /// <summary>
        /// <code>Do: </code> Point create mutation with entities related through a N:1 relationship. Relationship is defined in the database layer using FK constraints.
        /// <code>Check: </code> Publisher item is successfully created in the database. Book item is created with the publisher_id pointing to the newly created publisher item.
        /// </summary>
        public async Task MultipleCreateMutationWithManyToOneRelationship(string dbQuery)
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"
                mutation {
                    createbook(item: { title: ""Book #1"", publishers: { name: ""Publisher #1"" } }) {
                        id
                        title
                        publisher_id
                        publishers{
                            id
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
        /// <code>Do: </code> Point create mutation with entities related through a 1:N relationship. Relationship is defined in the database layer using FK constraints.
        /// <code>Check: </code> Book item is successfully created in the database. Review items are created with the book_id pointing to the newly created book item.
        /// </summary>
        public async Task MultipleCreateMutationWithOneToManyRelationship(string expectedResponse)
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"
                mutation {
                  createbook(
                    item: {
                      title: ""Book #1""
                      publisher_id: 1234
                      reviews: [
                        { content: ""Book #1 - Review #1"" }
                        { content: ""Book #1 - Review #2"" }
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
                      }
                    }
                  }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponse, actual.ToString());
        }

        /// <summary>
        /// <code>Do: </code> Point create mutation with entities related through a M:N relationship. Relationship is defined in the database layer using FK constraints.
        /// <code>Check: </code> Book item is successfully created in the database. Author items are successfully created in the database. The newly created Book and Author items are related using
        /// creating entries in the linking table. This is verified by querying field in the selection set and validating the response.
        /// </summary>
        public async Task MultipleCreateMutationWithManyToManyRelationship(string expectedResponse, string linkingTableDbValidationQuery, string expectedResponseFromLinkingTable)
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"
                    mutation {
                        createbook(
                        item: {
                            title: ""Book #1""
                            publisher_id: 1234
                            authors: [
                            { birthdate: ""2000-01-01"", name: ""Author #1"", royalty_percentage: 50.0 }
                            { birthdate: ""2000-02-03"", name: ""Author #2"", royalty_percentage: 50.0 }
                            ]
                        }
                        ) {
                        id
                        title
                        publisher_id
                        authors {
                            items {
                            id
                            name
                            birthdate
                            }
                        }
                        }
                    }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponse, actual.ToString());

            // Validate that the records are created in the linking table
            string actualResponseFromLinkingTable = await GetDatabaseResultAsync(linkingTableDbValidationQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponseFromLinkingTable, actualResponseFromLinkingTable);
        }

        /// <summary>
        /// <code>Do: </code> Point create mutation with entities related through a 1:1 relationship.
        /// <code>Check: </code> A new record in the stocks and stocks_price table is created successfully. The record created in
        /// stocks_price table should have the same categoryid and pieceid as the record created in the stocks table.
        /// This is validated by querying for categoryid and pieceid in the selection set.
        /// </summary>
        public async Task MultipleCreateMutationWithOneToOneRelationship(string expectedResponse)
        {
            string graphQLMutationName = "createStock";
            string graphQLMutation = @"
                                        mutation {
                                          createStock(
                                            item: {
                                              categoryid: 101
                                              pieceid: 101
                                              categoryName: ""SciFi""
                                              piecesAvailable: 100
                                              piecesRequired: 50
                                              stocks_price: {
                                                is_wholesale_price: true,
                                                price: 75.00,
                                                instant: ""2024-04-02""       
                                              }
                                            }
                                          ) {
                                            categoryid
                                            pieceid
                                            categoryName
                                            piecesAvailable
                                            piecesRequired
                                            stocks_price {
                                              categoryid
                                              pieceid
                                              instant
                                              price
                                              is_wholesale_price
                                            }
                                          }
                                        }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponse, actual.ToString());
        }

        /// <summary>
        /// <code> Do: </code> Point multiple create mutation with entities related through 1:1, N:1, 1:N and M:N relationships, all in a single mutation request. This also a
        /// combination relationships defined at the database layer and through the config file.
        /// 1. a) 1:1 relationship between Review - WebsiteUser entity is defined through the config file. b) Other relationships are defined through FK constraints 
        /// 2. Depth of this create mutation request = 2. Book --> Review --> WebsiteUser.
        /// <code> Check: </code> Records are successfully created in all the related entities. The created items are related as intended in the mutation request.
        /// Correct linking of the newly created items are validated by querying all the relationship fields in the selection set and validating it against the expected response.
        /// </summary>
        public async Task MultipleCreateMutationWithAllRelationshipTypes(string expectedResponse, string linkingTableDbValidationQuery, string expectedResponseFromLinkingTable)
        {
            string graphQLMutationName = "createbook";
            string graphQLMutation = @"mutation {
                                          createbook(
                                            item: {
                                              title: ""Book #1""
                                              publishers: { name: ""Publisher #1"" }
                                              reviews: [
                                                {
                                                  content: ""Book #1 - Review #1""
                                                  website_users: { id: 5001, username: ""WebsiteUser #1"" }
                                                }
                                                { content: ""Book #1 - Review #2"", websiteuser_id: 1 }
                                              ]
                                              authors: [
                                                { birthdate: ""2000-02-01"", name: ""Author #1"", royalty_percentage: 50.0 }
                                                { birthdate: ""2000-01-02"", name: ""Author #2"", royalty_percentage: 50.0 }
                                              ]
                                            }
                                          ) {
                                            id
                                            title
                                            publishers {
                                              id
                                              name
                                            }
                                            reviews {
                                              items {
                                                book_id
                                                id
                                                content
                                                website_users {
                                                  id
                                                  username
                                                }
                                              }
                                            }
                                            authors {
                                              items {
                                                id
                                                name
                                                birthdate
                                              }
                                            }
                                          }
                                        }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponse, actual.ToString());

            // Validate that the records are created in the linking table
            string actualResponseFromLinkingTable = await GetDatabaseResultAsync(linkingTableDbValidationQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponseFromLinkingTable, actualResponseFromLinkingTable);
        }

        /// <summary>
        /// <code>Do : </code> Many type multiple create mutation request with entities related through 1:1, N:1, 1:N and M:N relationships, all in a single mutation request.This also a
        /// combination relationships defined at the database layer and through the config file.
        /// 1. a) 1:1 relationship between Review - WebsiteUser entity is defined through the config file. b) Other relationships are defined through FK constraints 
        /// 2. Depth of this create mutation request = 2. Book --> Review --> WebsiteUser.
        /// <code>Check : </code> Records are successfully created in all the related entities. The created items are related as intended in the mutation request.
        /// Correct linking of the newly created items are validated by querying all the relationship fields in the selection set and validating it against the expected response.
        /// </summary>
        public async Task ManyTypeMultipleCreateMutationOperation(string expectedResponse, string linkingTableDbValidationQuery, string expectedResponseFromLinkingTable)
        {
            string graphQLMutationName = "createbooks";
            string graphQLMutation = @"mutation {
                                            createbooks(
                                            items: [
                                                {
                                                title: ""Book #1""
                                                publishers: { name: ""Publisher #1"" }
                                                reviews: [
                                                    {
                                                    content: ""Book #1 - Review #1""
                                                    website_users: { id: 5001, username: ""Website user #1"" }
                                                    }
                                                    { content: ""Book #1 - Review #2"", websiteuser_id: 4 }
                                                ]
                                                authors: [
                                                    {
                                                    name: ""Author #1""
                                                    birthdate: ""2000-01-02""
                                                    royalty_percentage: 50.0
                                                    }
                                                    {
                                                    name: ""Author #2""
                                                    birthdate: ""2001-02-03""
                                                    royalty_percentage: 50.0
                                                    }
                                                ]
                                                }
                                                {
                                                title: ""Book #2""
                                                publisher_id: 1234
                                                authors: [
                                                    {
                                                    name: ""Author #3""
                                                    birthdate: ""2000-01-02""
                                                    royalty_percentage: 65.0
                                                    }
                                                    {
                                                    name: ""Author #4""
                                                    birthdate: ""2001-02-03""
                                                    royalty_percentage: 35.0
                                                    }
                                                ]
                                                }
                                            ]
                                            ) {
                                            items {
                                                id
                                                title
                                                publisher_id
                                                publishers {
                                                id
                                                name
                                                }
                                                reviews {
                                                items {
                                                    book_id
                                                    id
                                                    content
                                                    website_users {
                                                    id
                                                    username
                                                    }
                                                }
                                                }
                                                authors {
                                                items {
                                                    id
                                                    name
                                                    birthdate
                                                }
                                                }
                                            }
                                            }
                                        }
                                        ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponse, actual.ToString());

            // Validate that the records are created in the linking table
            string actualResponseFromLinkingTable = await GetDatabaseResultAsync(linkingTableDbValidationQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponseFromLinkingTable, actualResponseFromLinkingTable);
        }

        #endregion

        #region Relationships defined through config file

        /// <summary>
        /// <code>Do: </code> Point create mutation with entities related through a 1:1 relationship. Relationship is defined through the config file.
        /// <code>Check: </code> createUser_NonAutogenRelationshipColumn and UserProfile_NonAutogenRelationshipColumn items are successfully created in the database. UserProfile_NonAutogenRelationshipColumn item is created and linked in the database.
        /// </summary>
        public async Task MultipleCreateMutationWithOneToOneRelationshipDefinedInConfigFile(string expectedResponse1, string expectedResponse2)
        {
            // Point create mutation request with the related entity acting as referencing entity.
            string graphQLMutationName = "createUser_NonAutogenRelationshipColumn";
            string graphQLMutation1 = @"mutation {
                  createUser_NonAutogenRelationshipColumn(
                    item: {
                      username: ""DAB""
                      email: ""dab@microsoft.com""
                      UserProfile_NonAutogenRelationshipColumn: {
                        profilepictureurl: ""dab/profilepicture""
                        userid: 10
                      }
                    }
                  ) {
                    userid
                    username
                    email
                    UserProfile_NonAutogenRelationshipColumn {
                      profileid
                      userid
                      username
                      profilepictureurl
                    }
                  }
                }";

            JsonElement actualResponse1 = await ExecuteGraphQLRequestAsync(graphQLMutation1, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponse1, actualResponse1.ToString());

            // Point create mutation request with the top level entity acting as referencing entity.
            string graphQLMutation2 = @"mutation{
                                              createUser_NonAutogenRelationshipColumn(item: {
                                                email: ""dab@microsoft.com"",
                                                UserProfile_NonAutogenRelationshipColumn: {
                                                  profilepictureurl: ""dab/profilepicture"",
                                                  userid: 10,
                                                  username: ""DAB2""
                                                }
                                              }){
                                                 userid
                                                 username
                                                 email
                                                 UserProfile_NonAutogenRelationshipColumn{
                                                  profileid
                                                  username
                                                  userid
                                                  profilepictureurl
                                                 }
                                              }
                                            }";

            JsonElement actualResponse2 = await ExecuteGraphQLRequestAsync(graphQLMutation2, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponse2, actualResponse2.ToString());
        }

        /// <summary>
        /// <code>Do: </code> Point create mutation with entities related through a N:1 relationship. Relationship is defined through the config file.
        /// <code>Check: </code> Publisher_MM item is successfully created in the database. Book_MM item is created with the publisher_id pointing to the newly created publisher_mm item.
        /// </summary>
        public async Task MultipleCreateMutationWithManyToOneRelationshipDefinedInConfigFile(string expectedResponse)
        {
            string graphQLMutationName = "createbook_mm";
            string graphQLMutation = @"mutation {
                                        createbook_mm(
                                        item: { title: ""Book #1"", publishers: { name: ""Publisher #1"" } }) {
                                        id
                                        title
                                        publisher_id
                                        publishers {
                                            id
                                            name
                                        }
                                        }
                                    }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponse, actual.ToString());
        }

        /// <summary>
        /// <code>Do: </code> Point create mutation with entities related through a 1:N relationship. Relationship is defined through the config file.
        /// <code>Check: </code> Book_MM item is successfully created in the database. Review_MM items are created with the book_id pointing to the newly created book_mm item.
        /// </summary>
        public async Task MultipleCreateMutationWithOneToManyRelationshipDefinedInConfigFile(string expectedResponse)
        {
            string graphQLMutationName = "createbook_mm";
            string graphQLMutation = @"
                mutation {
                  createbook_mm(
                    item: {
                      title: ""Book #1""
                      publisher_id: 1234
                      reviews: [
                        { content: ""Book #1 - Review #1"" }
                        { content: ""Book #1 - Review #2"" }
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
                      }
                    }
                  }
                }
            ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponse, actual.ToString());
        }

        /// <summary>
        /// <code>Do: </code> Point create mutation with entities related through a M:N relationship. Relationship is defined through the config file.
        /// <code>Check: </code> Book_MM item is successfully created in the database. Author_MM items are successfully created in the database. The newly created Book_MM and Author_MM items are related using
        /// creating entries in the linking table. This is verified by querying field in the selection set and validating the response.
        /// </summary>
        public async Task MultipleCreateMutationWithManyToManyRelationshipDefinedInConfigFile(string expectedResponse, string linkingTableDbValidationQuery, string expectedResponseFromLinkingTable)
        {
            string graphQLMutationName = "createbook_mm";
            string graphQLMutation = @"
                    mutation {
                        createbook_mm(
                        item: {
                            title: ""Book #1""
                            publisher_id: 1234
                            authors: [
                            { birthdate: ""2000-01-01"", name: ""Author #1"", royalty_percentage: 50.0 }
                            { birthdate: ""2000-02-03"", name: ""Author #2"", royalty_percentage: 50.0 }
                            ]
                        }
                        ) {
                        id
                        title
                        publisher_id
                        authors {
                            items {
                            id
                            name
                            birthdate
                            }
                        }
                        }
                    }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponse, actual.ToString());

            // Validate that the records are created in the linking table
            string actualResponseFromLinkingTable = await GetDatabaseResultAsync(linkingTableDbValidationQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponseFromLinkingTable, actualResponseFromLinkingTable);
        }

        /// <summary>
        /// <code> Do: </code> Point multiple create mutation with entities related through 1:1, N:1, 1:N and M:N relationships, all in a single mutation request. All the relationships are defined
        /// through the config file.
        /// Also, the depth of this create mutation request = 2. Book_MM --> Review_MM --> WebsiteUser_MM.
        /// <code> Check: </code> Records are successfully created in all the related entities. The created items are related as intended in the mutation request.
        /// Correct linking of the newly created items are validated by querying all the relationship fields in the selection set and validating it against the expected response.
        /// </summary>
        public async Task MultipleCreateMutationWithAllRelationshipTypesDefinedInConfigFile(string expectedResponse, string linkingTableDbValidationQuery, string expectedResponseFromLinkingTable)
        {
            string graphQLMutationName = "createbook_mm";
            string graphQLMutation = @"mutation {
                                          createbook_mm(
                                            item: {
                                              title: ""Book #1""
                                              publishers: { name: ""Publisher #1"" }
                                              reviews: [
                                                {
                                                  content: ""Book #1 - Review #1""
                                                  website_users: { id: 5001, username: ""WebsiteUser #1"" }
                                                }
                                                { content: ""Book #1 - Review #2"", websiteuser_id: 1 }
                                              ]
                                              authors: [
                                                { birthdate: ""2000-02-01"", name: ""Author #1"", royalty_percentage: 50.0 }
                                                { birthdate: ""2000-01-02"", name: ""Author #2"", royalty_percentage: 50.0 }
                                              ]
                                            }
                                          ) {
                                            id
                                            title
                                            publishers {
                                              id
                                              name
                                            }
                                            reviews {
                                              items {
                                                book_id
                                                id
                                                content
                                                website_users {
                                                  id
                                                  username
                                                }
                                              }
                                            }
                                            authors {
                                              items {
                                                id
                                                name
                                                birthdate
                                              }
                                            }
                                          }
                                        }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponse, actual.ToString());

            // Validate that the records are created in the linking table
            string actualResponseFromLinkingTable = await GetDatabaseResultAsync(linkingTableDbValidationQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponseFromLinkingTable, actualResponseFromLinkingTable);
        }

        /// <summary>
        /// <code>Do : </code> Many type multiple create mutation request with entities related through 1:1, N:1, 1:N and M:N relationships, all in a single mutation request. All the
        /// relationships are defined through the config file.
        /// Also, depth of this create mutation request = 2. Book --> Review --> WebsiteUser.
        /// <code>Check : </code> Records are successfully created in all the related entities. The created items are related as intended in the mutation request.
        /// Correct linking of the newly created items are validated by querying all the relationship fields in the selection set and validating it against the expected response.
        /// </summary>
        public async Task ManyTypeMultipleCreateMutationOperationRelationshipsDefinedInConfig(string expectedResponse, string linkingTableDbValidationQuery, string expectedResponseFromLinkingTable)
        {
            string graphQLMutationName = "createbooks_mm";
            string graphQLMutation = @"mutation {
                                            createbooks_mm(
                                            items: [
                                                {
                                                title: ""Book #1""
                                                publishers: { name: ""Publisher #1"" }
                                                reviews: [
                                                    {
                                                    content: ""Book #1 - Review #1""
                                                    website_users: { id: 5001, username: ""Website user #1"" }
                                                    }
                                                    { content: ""Book #1 - Review #2"", websiteuser_id: 4 }
                                                ]
                                                authors: [
                                                    {
                                                    name: ""Author #1""
                                                    birthdate: ""2000-01-02""
                                                    royalty_percentage: 50.0
                                                    }
                                                    {
                                                    name: ""Author #2""
                                                    birthdate: ""2001-02-03""
                                                    royalty_percentage: 50.0
                                                    }
                                                ]
                                                }
                                                {
                                                title: ""Book #2""
                                                publisher_id: 1234
                                                authors: [
                                                    {
                                                    name: ""Author #3""
                                                    birthdate: ""2000-01-02""
                                                    royalty_percentage: 65.0
                                                    }
                                                    {
                                                    name: ""Author #4""
                                                    birthdate: ""2001-02-03""
                                                    royalty_percentage: 35.0
                                                    }
                                                ]
                                                }
                                            ]
                                            ) {
                                            items {
                                                id
                                                title
                                                publisher_id
                                                publishers {
                                                id
                                                name
                                                }
                                                reviews {
                                                items {
                                                    book_id
                                                    id
                                                    content
                                                    website_users {
                                                    id
                                                    username
                                                    }
                                                }
                                                }
                                                authors {
                                                items {
                                                    id
                                                    name
                                                    birthdate
                                                }
                                                }
                                            }
                                            }
                                        }
                                        ";

            JsonElement actual = await ExecuteGraphQLRequestAsync(graphQLMutation, graphQLMutationName, isAuthenticated: true);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponse, actual.ToString());

            // Validate that the records are created in the linking table
            string actualResponseFromLinkingTable = await GetDatabaseResultAsync(linkingTableDbValidationQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponseFromLinkingTable, actualResponseFromLinkingTable);
        }

        #endregion

        #region Policy tests

        /// <summary>
        /// Point multiple create mutation request is executed with the role: role_multiple_create_policy_tester. This role has the following create policy defined on "Book" entity: "@item.title ne 'Test'"
        /// Because this mutation tries to create a book with title "Test", it is expected to fail with a database policy violation error. The error message and status code are validated for accuracy.
        /// </summary>
        [TestMethod]
        public async Task PointMultipleCreateFailsDueToCreatePolicyViolationAtTopLevelEntity(string expectedErrorMessage, string bookDbQuery, string publisherDbQuery)
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

            SqlTestHelper.TestForErrorInGraphQLResponse(
                response: actual.ToString(),
                message: expectedErrorMessage,
                statusCode: $"{DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure}"
            );

            // Validate that no book item is created
            string dbResponse = await GetDatabaseResultAsync(bookDbQuery);
            Assert.AreEqual("[]", dbResponse);

            // Validate that no publisher item is created
            dbResponse = await GetDatabaseResultAsync(publisherDbQuery);
            Assert.AreEqual("[]", dbResponse);
        }

        /// <summary>
        /// Point multiple create mutation request is executed with the role: role_multiple_create_policy_tester. This role has the following create policy defined on "Publisher" entity: "@item.name ne 'Test'"
        /// Because this mutation tries to create a publisher with title "Test" (along with creating a book item), it is expected to fail with a database policy violation error.
        /// As a result of this mutation, no Book and Publisher item should be created.  
        /// The error message and status code are validated for accuracy. Also, the database is queried to ensure that no new record got created.
        /// </summary>
        [TestMethod]
        public async Task PointMultipleCreateFailsDueToCreatePolicyViolationAtRelatedEntity(string expectedErrorMessage, string bookDbQuery, string publisherDbQuery)
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

            SqlTestHelper.TestForErrorInGraphQLResponse(
                response: actual.ToString(),
                message: expectedErrorMessage,
                statusCode: $"{DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure}");

            // Validate that no book item is created
            string dbResponse = await GetDatabaseResultAsync(bookDbQuery);
            Assert.AreEqual("[]", dbResponse);

            // Validate that no publisher item is created
            dbResponse = await GetDatabaseResultAsync(publisherDbQuery);
            Assert.AreEqual("[]", dbResponse);
        }

        /// <summary>
        /// Many type multiple create mutation request is executed with the role: role_multiple_create_policy_tester. This role has the following create policy defined on "Book" entity: "@item.title ne 'Test'"
        /// In this request, the second Book item in the input violates the create policy defined. Processing of that input item is expected to result in database policy violation error.
        /// All the items created successfully prior to this fault input will also be rolled back. So, the end result is that no new items should be created. 
        /// </summary>
        [TestMethod]
        public async Task ManyTypeMultipleCreateFailsDueToCreatePolicyFailure(string expectedErrorMessage, string bookDbQuery, string publisherDbQuery)
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

            SqlTestHelper.TestForErrorInGraphQLResponse(
                response: actual.ToString(),
                message: expectedErrorMessage,
                statusCode: $"{DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure}");

            // Validate that no book item is created
            string dbResponse = await GetDatabaseResultAsync(bookDbQuery);
            Assert.AreEqual("[]", dbResponse);

            // Validate that no publisher item is created
            dbResponse = await GetDatabaseResultAsync(publisherDbQuery);
            Assert.AreEqual("[]", dbResponse);
        }

        /// <summary>
        /// Point type multiple create mutation request is executed with the role: role_multiple_create_policy_tester. This role has the following create policy defined on "Reviews" entity: "@item.websiteuser_id ne 1".
        /// In this request, the second Review item in the input violates the read policy defined. Hence, it is not to be returned in the response.
        /// The returned response is validated against an expected response for correctness.
        /// </summary>
        [TestMethod]
        public async Task PointMultipleCreateMutationWithReadPolicyViolationAtRelatedEntity(string expectedResponse)
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
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponse, actual.ToString());
        }

        #endregion
    }
}
