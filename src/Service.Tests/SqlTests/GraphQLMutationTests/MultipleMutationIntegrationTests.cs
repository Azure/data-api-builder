// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Azure.DataApiBuilder.Service.Tests.Authorization.GraphQL
{
    [TestClass, TestCategory(TestCategory.MSSQL)]
    public class MultipleMutationIntegrationTests : SqlTestBase
    {
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
        /// Test to validate that for 'create one' mutations, we throw an appropriate exception when the user request provides
        /// no source of value for a non-nullable and non-default referencing column.
        /// The referencing column can get its value from 3 possible sources:
        /// 1. Value assigned to the referencing column,
        /// 2. Value derived from insertion in the parent referenced entity,
        /// 3. Value derived from insertion in the target referenced entity.
        /// </summary>
        [TestMethod]
        public async Task AbsenceOfSourceOfTruthForReferencingColumnForCreateOneMutations()
        {
            // For a relationship between Book (Referencing) - Publisher (Referenced) defined as books.publisher_id -> publisher.id,
            // in the request input:
            // 1. No explicit value is provided for referencing field publisher_id
            // 2. Book is the top-level entity - so there is no parent entity.
            // 3. No value is assigned for the referenced entity via relationship field ('publisher').
            string createOneBookMutationName = "createbook";
            string multipleCreateOneBookWithoutPublisher = @"mutation {
                    createbook(item: { title: ""My New Book"" }) {
                        id
                        title
                    }
                }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                query: multipleCreateOneBookWithoutPublisher,
                queryName: createOneBookMutationName,
                isAuthenticated: true,
                variables: null,
                clientRoleHeader: "authenticated");

            // Validate that an appropriate exception is thrown because the value for the referencing field
            // publisher_id cannot be derived from any of the two possible sources mentioned above.
            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "Missing value for required column: publisher_id for entity: Book at level: 1.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// Test to validate that for 'create multiple' mutations, we throw an appropriate exception when the user request provides
        /// no source of value for a non-nullable and non-default referencing column.
        /// There are 3 sources of values for a referencing column:
        /// 1. Explicit value assignment to the referencing column,
        /// 2. Value derived from the parent referenced entity,
        /// 3. Providing value for inserting record in the referenced target entity, in which case value for referencing column
        /// is derived from the value of corresponding referenced column after insertion in the referenced entity.
        /// When the user provides none of the above, we throw an exception.
        /// </summary>
        [TestMethod]
        public async Task AbsenceOfSourceOfTruthForReferencingColumnForCreateMultipleMutations()
        {
            // For a relationship between Book (Referencing) - Publisher (Referenced) defined as books.publisher_id -> publisher.id,
            // in the request input:
            // 1. No explicit value is provided for referencing field publisher_id
            // 2. Book is the top-level entity - so there is no parent entity.
            // 3. No value is assigned for the referenced entity via relationship field ('publisher').
            string createmultipleBooksMutationName = "createbooks";
            string createMultipleBooksWithoutPublisher = @"mutation {
                    createbooks(items: [ { title: ""My New Book"", publisher_id: 1234 }, { title: ""My New Book"" }]) {
                        items{
                           id
                           title
                        }
                    }
                }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                query: createMultipleBooksWithoutPublisher,
                queryName: createmultipleBooksMutationName,
                isAuthenticated: true,
                variables: null,
                clientRoleHeader: "authenticated");

            // Validate that an appropriate exception is thrown because the value for the referencing field
            // publisher_id cannot be derived from any of the two possible sources mentioned above.
            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "Missing value for required column: publisher_id for entity: Book at level: 1.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// Test to validate that for 'create one' mutations, we throw an appropriate exception when the user request provides
        /// multiple sources of value for a referencing column. There are 3 sources of values for a referencing column:
        /// -> Explicit value assignment to the referencing column,
        /// -> Value derived from the parent referenced entity,
        /// -> Providing value for inserting record in the referenced target entity, in which case value for referencing column
        /// is derived from the value of corresponding referenced column after insertion in the referenced entity.
        /// When the user provides more than one of the above, we throw an exception.
        /// </summary>
        [TestMethod]
        public async Task PresenceOfMultipleSourcesOfTruthForReferencingColumnForCreateOneMutations()
        {
            // Test 1.
            // For a relationship between Book (Referencing) - Publisher (Referenced) defined as books.publisher_id -> publisher.id,
            // consider the request input for Book:
            // 1. An explicit value is provided for referencing field publisher_id.
            // 2. A value is assigned for the target (referenced entity) via relationship field ('publisher') which will
            // give back another value for publisher_id.
            string createOneBookMutationName = "createbook";
            string multipleCreateOneBookWithPublisher = @"mutation {
                    createbook(item: { title: ""My New Book"", publisher_id: 1234, publishers: { name: ""New publisher""}}) {
                        id
                        title
                    }
                }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                query: multipleCreateOneBookWithPublisher,
                queryName: createOneBookMutationName,
                isAuthenticated: true,
                variables: null,
                clientRoleHeader: "authenticated");

            // Validate that an appropriate exception is thrown because we have two possibly conflicting sources of
            // values for the referencing field 'publisher_id'.
            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "Found conflicting sources providing a value for the field: publisher_id for entity: Book at level: 1." +
                    "Source 1: entity: Book, Source 2: Relationship: publishers.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );

            // Test 2: Validates the functionality of the MultipleMutationInputValidator.ValidateAbsenceOfReferencingColumnsInTargetEntity().
            // For a relationship between Book (Referenced) - Review (Referencing) - defined as books.id <- reviews.book_id,
            // consider the request input for Review:
            // 1. An explicit value is provided for referencing field book_id.
            // 2. Book is the parent referenced entity - so it passes on a value for book_id to the referencing entity Review.
            string multipleCreateOneBookWithReviews = @"mutation {
                    createbook(item: { title: ""My New Book"", publisher_id: 1234, reviews: [{ content: ""Good book"", book_id: 123}]}) {
                        id
                        title
                    }
                }";

            actual = await ExecuteGraphQLRequestAsync(
                query: multipleCreateOneBookWithReviews,
                queryName: createOneBookMutationName,
                isAuthenticated: true,
                variables: null,
                clientRoleHeader: "authenticated");

            // Validate that an appropriate exception is thrown because we have two possibly conflicting sources of
            // values for the referencing field 'book_id'.
            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "You can't specify the field: book_id in the create input for entity: Review at level: 2 because the value is derived from " +
                    "the creation of the record in it's parent entity specified in the request.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );

            // Test 3.
            // For a relationship between Publisher (Referenced) - Book (Referencing) defined as publisher.id <- books.publisher_id,
            // consider the request input for Book:
            // 1. Publisher is the parent referenced entity - so it passes on a value for publisher_id to the referencing entity Book.
            // 2. A value is assigned for the target (referenced entity) via relationship field ('publisher'), which will give back
            // another value for publisher_id.
            string createOnePublisherName = "createPublisher";
            string multipleCreateOneBookWithTwoPublishers = @"mutation {
                    createPublisher(item: {
                                        name: ""Human publisher"",
                                        books: [
                                            { title: ""Book #1"", publishers: { name: ""Alien publisher"" } }
                                               ]
                                          }
                                    )
                    {
                        id
                        name
                    }
                }";

            actual = await ExecuteGraphQLRequestAsync(
                query: multipleCreateOneBookWithTwoPublishers,
                queryName: createOnePublisherName,
                isAuthenticated: true,
                variables: null,
                clientRoleHeader: "authenticated");

            // Validate that an appropriate exception is thrown because we have two possibly conflicting sources of
            // values for the referencing field 'publisher_id'.
            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "Found conflicting sources providing a value for the field: publisher_id for entity: Book at level: 2." +
                    "Source 1: Parent entity: Publisher, Source 2: Relationship: publishers.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// Test to validate that for 'create multiple' mutations, we throw an appropriate exception when the user request provides
        /// multiple sources of value for a referencing column. There are 3 sources of values for a referencing column:
        /// -> Explicit value assignment to the referencing column,
        /// -> Value derived from the parent referenced entity,
        /// -> Providing value for inserting record in the referenced target entity, in which case value for referencing column
        /// is derived from the value of corresponding referenced column after insertion in the referenced entity.
        /// When the user provides more than one of the above, we throw an exception.
        /// </summary>
        [TestMethod]
        public async Task PresenceOfMultipleSourcesOfTruthForReferencingColumnForCreateMultipleMutations()
        {
            // Test 1.
            // For a relationship between Book (Referencing) - Publisher (Referenced) defined as books.publisher_id -> publisher.id,
            // consider the request input for Book:
            // 1. An explicit value is provided for referencing field publisher_id.
            // 2. A value is assigned for the target (referenced entity) via relationship field ('publisher') which will
            // give back another value for publisher_id.
            string createMultipleBooksMutationName = "createbooks";
            string createMultipleBooksWithPublisher = @"mutation {
                    createbooks(items: [{ title: ""My New Book"", publisher_id: 1234 }, { title: ""My New Book"", publisher_id: 1234, publishers: { name: ""New publisher""}}]) {
                        items{
                           id
                           title
                        }
                    }
                }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                query: createMultipleBooksWithPublisher,
                queryName: createMultipleBooksMutationName,
                isAuthenticated: true,
                variables: null,
                clientRoleHeader: "authenticated");

            // Validate that an appropriate exception is thrown because we have two possibly conflicting sources of
            // values for the referencing field 'publisher_id'.
            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "Found conflicting sources providing a value for the field: publisher_id for entity: Book at level: 1." +
                    "Source 1: entity: Book, Source 2: Relationship: publishers.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );

            // Test 2: Validates the functionality of the MultipleMutationInputValidator.ValidateAbsenceOfReferencingColumnsInTargetEntity().
            // For a relationship between Book (Referenced) - Review (Referencing) -  defined as books.id <- reviews.book_id,
            // consider the request input for Review:
            // 1. An explicit value is provided for referencing field book_id.
            // 2. Book is the parent referenced entity - so it passes on a value for book_id to the referencing entity Review.
            string createMultipleBooksWithReviews = @"mutation {
                    createbooks(items: [{ title: ""My New Book"", publisher_id: 1234, reviews: [{ content: ""Good book""}]},
                                        { title: ""My New Book"", publisher_id: 1234, reviews: [{ content: ""Good book"", book_id: 123}]}]) {
                        items{
                           id
                           title
                        }
                    }
                }";

            actual = await ExecuteGraphQLRequestAsync(
                query: createMultipleBooksWithReviews,
                queryName: createMultipleBooksMutationName,
                isAuthenticated: true,
                variables: null,
                clientRoleHeader: "authenticated");

            // Validate that an appropriate exception is thrown because we have two possibly conflicting sources of
            // values for the referencing field 'book_id'.
            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "You can't specify the field: book_id in the create input for entity: Review at level: 2 because the value is derived from the " +
                    "creation of the record in it's parent entity specified in the request.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );

            // Test 3.
            // For a relationship between Publisher (Referenced) - Book (Referencing) defined as publisher.id <- books.publisher_id,
            // consider the request input for Book:
            // 1. Publisher is the parent referenced entity - so it passes on a value for publisher_id to the referencing entity Book.
            // 2. A value is assigned for the target (referenced entity) via relationship field ('publisher'), which will give back
            // another value for publisher_id.
            string createMultiplePublisherName = "createPublishers";
            string multipleCreateOneBookWithTwoPublishers = @"mutation {
                    createPublishers(items: [{
                                        name: ""Human publisher"",
                                        books: [
                                            { title: ""Book #1"", publishers: { name: ""Alien publisher"" } }
                                               ]
                                          }]
                                    )
                    {
                        items{
                           id
                           name
                        }
                    }
                }";

            actual = await ExecuteGraphQLRequestAsync(
                query: multipleCreateOneBookWithTwoPublishers,
                queryName: createMultiplePublisherName,
                isAuthenticated: true,
                variables: null,
                clientRoleHeader: "authenticated");

            // Validate that an appropriate exception is thrown because we have two possibly conflicting sources of
            // values for the referencing field 'publisher_id'.
            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "Found conflicting sources providing a value for the field: publisher_id for entity: Book at level: 2." +
                    "Source 1: Parent entity: Publisher, Source 2: Relationship: publishers.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// Test to validate that when one referencing column references multiple referenced columns in one referenced entity,
        /// we fail the request during request validation because then we have conflicting sources of truth for values for
        /// the referencing column. In these instances, the column that is being referenced can take on the value from any
        /// column it references. This can create uncertainty about which value should be assigned to the column being referenced.
        /// </summary>
        [TestMethod]
        public async Task InvalidateRepeatedReferencingColumnToOneReferencedEntityInReferencingEntity()
        {
            // For a relationship between User_RepeatedReferencingColumnToOneEntity(username,username) - UserProfile (username, profilepictureurl),
            // when the User_RepeatedReferencingColumnToOneEntity entity acts as the referencing entity, there are two sources
            // of truth for the value of User_RepeatedReferencingColumnToOneEntity.username:
            // 1. UserProfile.username
            // 2. UserProfile.profilepictureurl
            // which leads to ambiguity as to what value should be assigned to the User_RepeatedReferencingColumnToOneEntity.username
            // field after performing insertion in the referenced UserProfile entity.
            string createUserRepeatedRelationshipColumn = "createUser_RepeatedReferencingColumnToOneEntity";
            string createUserRepeatedRelationshipColumnMutation =
                @"mutation {
                    createUser_RepeatedReferencingColumnToOneEntity(
                        item:{
                            email: ""ss""
                            UserProfile: {
                                username: ""s""
                                profilepictureurl: ""ss""
                                }
                             }
                    )
                    {
                        userid
                        username
                    }
                }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                query: createUserRepeatedRelationshipColumnMutation,
                queryName: createUserRepeatedRelationshipColumn,
                isAuthenticated: false,
                variables: null,
                clientRoleHeader: "anonymous");

            // Validate that an appropriate exception is thrown because the value for the referencing field
            // 'username' can be derived from both of the referenced columns in the referenced entity.
            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "The field(s): {username} in the entity: User_RepeatedReferencingColumnToOneEntity reference(s) multiple field(s) in" +
                    " the related entity: UserProfile for the relationship: UserProfile at level: 1.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /// <summary>
        /// Test to validate that when one referencing column references multiple referenced columns in multiple referenced entities,
        /// we fail the request during request validation because then we have conflicting sources of truth for values for
        /// the referencing column. In such cases, the referencing column can assume the value of any referenced column in the referenced entities
        /// which leads to amibugites as to what value to assign to the referencing column.
        /// </summary>
        [TestMethod]
        public async Task InvalidateRepeatedReferencingColumnToMultipleReferencedEntitiesInReferencingEntity()
        {
            // The entity createUserProfile_RepeatedRelationshipColumnToTwoEntities acts as the referencing entity for the two relationships:
            // 1. UserProfile_RepeatedReferencingColumnToTwoEntities.userid -> books.id
            // 2. UserProfile_RepeatedReferencingColumnToTwoEntities.userid -> publishers.id,
            // due to which we have two sources of truth for the column userid - books.id, publishers.id
            // which causes ambiguity as to what value should be assigned to the createUserProfile_RepeatedRelationshipColumnToTwoEntities.userid
            // field after performing insertion in the two referenced entities, i.e. Book and Publisher.
            string createUserRepeatedRelationshipColumn = "createUserProfile_RepeatedReferencingColumnToTwoEntities";
            string createUserRepeatedRelationshipColumnMutation =
                @"mutation {
                    createUserProfile_RepeatedReferencingColumnToTwoEntities(
                        item: {
                                profilepictureurl: ""abc"",
                                username: ""abc"",
                                book: { title: ""abc"", publisher_id: 1234 },
                                publisher: { name: ""abc"" }
                              }
                        )
                        {
                            profileid
                            username
                        }
                }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                query: createUserRepeatedRelationshipColumnMutation,
                queryName: createUserRepeatedRelationshipColumn,
                isAuthenticated: true,
                variables: null,
                clientRoleHeader: "authenticated");

            // Validate that an appropriate exception is thrown because the value for the referencing field
            // userid can be derived from both of the referenced columns in the different referenced entities.
            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "Found conflicting sources providing a value for the field: userid for entity: UserProfile_RepeatedReferencingColumnToTwoEntities at level: 1." +
                    "Source 1: Relationship: book, Source 2: Relationship: publisher.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }
    }
}
