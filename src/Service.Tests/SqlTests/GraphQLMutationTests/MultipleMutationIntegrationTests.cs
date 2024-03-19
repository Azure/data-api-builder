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
        /// Test to validate that we throw an appropriate exception when the user request provides no source of value for a non-nullable
        /// and non-default referencing column. There are three sources of values for a referencing column:
        /// 1. Explicit value assignment to the referencing column,
        /// 2. Value derived from the parent referenced entity,
        /// 3. Providing value for inserting record in the referenced target entity, in which case value for referencing column is derived
        /// from the value of corresponding referenced column after insertion in the referenced entity.
        /// When the user provides none of the above, we throw an exception.
        /// </summary>
        [TestMethod]
        public async Task AbsenceOfSourceOfTruthForReferencingColumn()
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
        /// Test to validate that we throw an appropriate exception when the user request provides multiple sources of value for a
        /// referencing column. There are 3 sources of values for a referencing column:
        /// -> Explicit value assignment to the referencing column,
        /// -> Value derived from the parent referenced entity,
        /// -> Providing value for inserting record in the referenced target entity, in which case value for referencing column is derived
        /// from the value of corresponding referenced column after insertion in the referenced entity.
        /// When the user provides more than one of the above, we throw an exception.
        /// </summary>
        [TestMethod]
        public async Task PresenceOfMultipleSourcesOfTruthForReferencingColumn()
        {
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
                    message: "Found conflicting sources of values for the field: publisher_id for entity: Book at level: 1." +
                    "Source 1: entity: Book ,Source 2: Relationship: publishers.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );

            // For a relationship between Review (Referencing) - Book (Referenced) defined as reviews.book_id -> books.id,
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
                    message: "The field: book_id cannot be present for entity: Review at level: 2.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );

            string createOnePublisherName = "Publisher";

            // For a relationship between Book (Referencing) - Publisher (Referenced) defined as books.publisher_id -> publisher.id,
            // consider the request input for Book:
            // 1. Publisher is the parent referenced entity - so it passes on a value for publisher_id to the referencing entity Book.
            // 2. A value is assigned for the target (referenced entity) via relationship field ('publisher'), which will give back
            // another value for publisher_id.
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
                    message: "Found conflicting sources of values for the field: publisher_id for entity: Book at level: 2." +
                    "Source 1: Parent entity: Publisher ,Source 2: Relationship: publishers.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }

        /*[TestMethod]
        public async Task ReferencingEntityContainingReferencingField()
        {
            string createMultipleBookMutationName = "createbook";
            string nestedCreateMultipleBooks = @"mutation {
                    createbooks(items: [{ title: ""My New Book"", publisher_id: 1234 publishers: { name: ""New publisher""}}]) {
                        items{
                            id
                            title
                        }
                    }
                }";

            actual = await ExecuteGraphQLRequestAsync(
                query: nestedCreateMultipleBooks,
                queryName: createMultipleBookMutationName,
                isAuthenticated: true,
                variables: null,
                clientRoleHeader: "authenticated");

            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "Either the field: publisher_id or the relationship field: publishers can be specified.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }*/
    }
}
