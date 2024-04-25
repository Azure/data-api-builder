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
        /// <code>Do: </code> Point create mutation with entities related through a N:1 relationship.
        /// Relationship is defined in the database layer using FK constraints.
        /// <code>Check: </code> Publisher item is successfully created first in the database.
        /// Then, Book item is created where book.publisher_id is populated with the previously created
        /// Book record's id.
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
        /// <code>Do: </code> Point create mutation with entities related through a 1:N relationship.
        /// Relationship is defined in the database layer using FK constraints.
        /// <code>Check: </code> Book item is successfully created first in the database.
        /// Then, Review items are created where review.book_id is populated with the previously
        /// created Book record's id.
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
        /// <code>Do: </code> Point create mutation with entities related through a M:N relationship.
        /// Relationship is defined in the database layer using FK constraints.
        /// <code>Check: </code> Book item is successfully created in the database.
        /// Author items are successfully created in the database.
        /// Then, the newly created Book and Author ID fields are inserted into the linking table.
        /// Linking table contents are verified with follow-up database query looking for
        /// (book.id, author.id) record.
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

            // Book - Author entities are related through a M:N relationship.
            // After successful creation of Book and Author items, a record will be created in the linking table
            // with the newly created Book and Author record's id.
            // The following database query validates that two records exist in the linking table book_author_link
            // with (book_id, author_id) : (5001, 5001) and (5001, 5002)
            // These two records are also validated to ensure that they are created with the right
            // value in royalty_percentage column.
            string actualResponseFromLinkingTable = await GetDatabaseResultAsync(linkingTableDbValidationQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponseFromLinkingTable, actualResponseFromLinkingTable);
        }

        /// <summary>
        /// <code>Do: </code> Point create mutation with entities related through a 1:1 relationship.
        /// The goal with this mutation request is to create a Stock item, Stocks_Price item
        /// and link the Stocks_Price item with the Stock item. Since, the idea is to link the Stocks_Price
        /// item with the Stock item  that is being created in the same mutation request, the
        /// mutation input for stocks_price will not contain the fields categoryid and pieceid.
        /// <code>Check: </code> Stock item is successfully created first in the database.
        /// Then, the Stocks_Price item is created where stocks_price.categoryid and stocks_price.pieceid
        /// are populated with the previously created Stock record's categoryid and pieceid.
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
        /// <code> Do: </code> Point multiple create mutation with entities related through
        /// 1:1, N:1, 1:N and M:N relationships, all in a single mutation request.
        /// Relationships involved in the create mutation request are both
        /// defined at the database layer and through the config file.
        /// 1. a) 1:1 relationship between Review - WebsiteUser entity is defined through the config file.
        ///    b) Other relationships are defined through FK constraints 
        /// 2. Depth of this create mutation request = 2. Book --> Review --> WebsiteUser.
        /// <code> Check: </code> Records are successfully created in all the related entities.
        /// The created items are related as intended in the mutation request.
        /// The right order of insertion is as follows:
        /// 1. Publisher item is successfully created in the database.
        /// 2. Book item is created with books.publisher_id populated with the Publisher record's id.
        /// 3. WebsiteUser item is successfully created in the database.
        /// 4. The first Review item is created with reviews.website_userid
        /// populated with the WebsiteUser record's id.
        /// 5. Second Review item is created. reviews.website_userid is populated with
        /// the value present in the input request.
        /// 6. Author item is successfully created in the database.
        /// 7. A record in the linking table is created with the newly created Book and Author record's id.
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

            // Book - Author entities are related through a M:N relationship.
            // After successful creation of Book and Author items, a record will be created in the linking table
            // with the newly created Book and Author record's id.
            // The following database query validates that two records exist in the linking table book_author_link
            // with (book_id, author_id) : (5001, 5001) and (5001, 5002)
            // These two records are also validated to ensure that they are created with the right
            // value in royalty_percentage column.
            string actualResponseFromLinkingTable = await GetDatabaseResultAsync(linkingTableDbValidationQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponseFromLinkingTable, actualResponseFromLinkingTable);
        }

        /// <summary>
        /// <code>Do : </code> Many type multiple create mutation request with entities related through
        /// 1:1, N:1, 1:N and M:N relationships, all in a single mutation request.This also a
        /// combination relationships defined at the database layer and through the config file.
        /// 1. a) 1:1 relationship between Review - WebsiteUser entity is defined through the config file.
        ///    b) Other relationships are defined through FK constraints.
        /// 2. Depth of this create mutation request = 2. Book --> Review --> WebsiteUser.
        /// <code>Check : </code> Records are successfully created in all the related entities.
        /// The created items are related as intended in the mutation request.
        /// Correct linking of the newly created items are validated by querying all the relationship fields
        /// in the selection set and validating it against the expected response.
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
        /// <code>Do: </code> Point create mutation with entities related through a 1:1 relationship
        /// through User_NonAutogenRelationshipColumn.username and
        /// UserProfile_NonAutogenRelationshipColumn.username fields
        /// Relationship is defined through the config file.
        /// <code>Check: User_NonAutogenRelationshipColumn and UserProfile_NonAutogenRelationshipColumn items are
        /// successfully created in the database. UserProfile_NonAutogenRelationshipColumn item is created
        /// and linked in the database.</code> 
        /// </summary>
        public async Task MultipleCreateMutationWithOneToOneRelationshipDefinedInConfigFile(string expectedResponse1, string expectedResponse2)
        {
            // Point create mutation request with the related entity(UserProfile_NonAutogenRelationshipColumn)
            // acting as referencing entity.
            // First, User_NonAutogenRelationshipColumn item is created in the database.
            // Then, the UserProfile_NonAutogenRelationshipColumn item is created in the database
            // with username populated using User_NonAutogenRelationshipColumn.username field's value.
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

            // Point create mutation request with the top level entity(User_NonAutogenRelationshipColumn)
            // acting as referencing entity.
            // First, UserProfile_NonAutogenRelationshipColumn item is created in the database.
            // Then, the User_NonAutogenRelationshipColumn item is created in the database
            // with username populated using UserProfile_NonAutogenRelationshipColumn.username field's value.
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
        /// <code>Do: </code> Point create mutation with entities related through a N:1 relationship.
        /// Relationship is defined through the config file.
        /// <code>Check: </code> Publisher_MM item is successfully created first in the database.
        /// Then, Book_MM item is created where book_mm.publisher_id is populated with the previously created
        /// Book_MM record's id.
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
        /// <code>Do: </code> Point create mutation with entities related through a 1:N relationship.
        /// Relationship is defined through the config file.
        /// <code>Check: </code> Book_MM item is successfully created first in the database.
        /// Then, Review_MM items are created where review_mm.book_id is populated with the previously
        /// created Book_MM record's id.
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
        /// <code>Do: </code> Point create mutation with entities related through a M:N relationship.
        /// Relationship is defined through the config file.
        /// <code>Check: </code> Book_MM item is successfully created in the database.
        /// Author_MM items are successfully created in the database.
        /// Then, the newly created Book_MM and Author_MM ID fields are inserted
        /// into the linking table book_author_link_mm.
        /// Linking table contents are verified with follow-up database query looking for
        /// (book.id, author.id) record.
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

            // After successful creation of Book_MM and Author_MM items, a record will be created in the linking table
            // with the newly created Book and Author record's id.
            // The following database query validates that two records exist in the linking table book_author_link
            // with (book_id, author_id) : (5001, 5001) and (5001, 5002)
            // These two records are also validated to ensure that they are created with the right
            // value in royalty_percentage column.
            string actualResponseFromLinkingTable = await GetDatabaseResultAsync(linkingTableDbValidationQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponseFromLinkingTable, actualResponseFromLinkingTable);
        }

        /// <summary>
        /// <code> Do: </code> Point multiple create mutation with entities related
        /// through 1:1, N:1, 1:N and M:N relationships, all in a single mutation request.
        /// All the relationships are defined through the config file.
        /// Also, the depth of this create mutation request = 2. Book_MM --> Review_MM --> WebsiteUser_MM.
        /// <code> Check: </code> Records are successfully created in all the related entities.
        /// The created items are related as intended in the mutation request.
        /// The right order of insertion is as follows:
        /// 1. Publisher_MM item is successfully created in the database.
        /// 2. Book_MM item is created with books_mm.publisher_id populated with the Publisher_MM record's id.
        /// 3. WebsiteUser_MM item is successfully created in the database.
        /// 4. The first Review_MM item is created with reviews_mm.website_userid
        /// populated with the WebsiteUser_MM record's id.
        /// 5. Second Review_MM item is created. reviews_mm.website_userid is populated with
        /// the value present in the input request.
        /// 6. Author_MM item is successfully created in the database.
        /// 7. A record in the linking table is created with the newly created Book_MM and Author_MM record's id.
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

            // Book_MM - Author_MM entities are related through a M:N relationship.
            // After successful creation of Book_MM and Author_MM items, a record will be created in the linking table
            // with the newly created Book_MM and Author_MM record's id.
            // The following database query validates that two records exist in the linking table book_author_link_mm
            // with (book_id, author_id) : (5001, 5001) and (5001, 5002)
            // These two records are also validated to ensure that they are created with the right
            // value in royalty_percentage column.
            string actualResponseFromLinkingTable = await GetDatabaseResultAsync(linkingTableDbValidationQuery);
            SqlTestHelper.PerformTestEqualJsonStrings(expectedResponseFromLinkingTable, actualResponseFromLinkingTable);
        }

        /// <summary>
        /// <code>Do : </code> Many type multiple create mutation request with entities related through
        /// 1:1, N:1, 1:N and M:N relationships, all in a single mutation request.
        /// All the relationships are defined through the config file.
        /// Also, depth of this create mutation request = 2. Book_MM --> Review_MM --> WebsiteUser_MM.
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
        /// Point multiple create mutation request is executed with the role: role_multiple_create_policy_tester.
        /// This role has the following create policy defined on "Book" entity: "@item.title ne 'Test'".
        /// Since this mutation tries to create a book with title "Test", it is expected
        /// to fail with a database policy violation error.
        /// The error message and status code are validated for accuracy.
        /// </summary>
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
        /// Point multiple create mutation request is executed with the role: role_multiple_create_policy_tester.
        /// This role has the following create policy defined on "Publisher" entity: "@item.name ne 'Test'"
        /// Since, this mutation tries to create a publisher with title "Test" (along with creating a book item),
        /// it is expected to fail with a database policy violation error.
        /// As a result of this mutation, no Book and Publisher items should be created.  
        /// The error message and status code are validated for accuracy.
        /// Also, the database is queried to ensure that no new record got created.
        /// </summary>
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
        /// Many type multiple create mutation request is executed with the role: role_multiple_create_policy_tester.
        /// This role has the following create policy defined on "Book" entity: "@item.title ne 'Test'"
        /// In this request, the second Book item in the input violates the create policy defined.
        /// Processing of that input item is expected to result in database policy violation error.
        /// All the items created successfully prior to this faulty input will also be rolled back.
        /// So, the end result is that no new items should be created. 
        /// </summary>
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
        /// This test validates that read policies are honored when constructing the response.
        /// Point multiple create mutation request is executed with the role: role_multiple_create_policy_tester.
        /// This role has the following read policy defined on "Reviews" entity: "@item.websiteuser_id ne 1".
        /// The second Review item in the input violates the read policy defined.
        /// Hence, it is not expected to be returned in the response.
        /// The returned response is validated against an expected response for correctness.
        /// </summary>
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
