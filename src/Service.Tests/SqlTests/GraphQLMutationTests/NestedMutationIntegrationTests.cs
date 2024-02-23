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
    public class NestedMutationIntegrationTests : SqlTestBase
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

        [TestMethod]
        public async Task ReferencingEntityContainingReferencingField()
        {
            string createOneBookMutationName = "createbook";
            string nestedCreateOneBookWithoutPubId = @"mutation {
                    createbook(item: { title: ""My New Book"" }) {
                        id
                        title
                    }
                }";

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                query: nestedCreateOneBookWithoutPubId,
                queryName: createOneBookMutationName,
                isAuthenticated: true,
                variables: null,
                clientRoleHeader: "authenticated");

            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "No value found for non-null/non-default column: publisher_id for entity: Book.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );

            string nestedCreateOneBook = @"mutation {
                    createbook(item: { title: ""My New Book"", publisher_id: 1234, publishers: { name: ""New publisher""}}) {
                        id
                        title
                    }
                }";

            actual = await ExecuteGraphQLRequestAsync(
                query: nestedCreateOneBook,
                queryName: createOneBookMutationName,
                isAuthenticated: true,
                variables: null,
                clientRoleHeader: "authenticated");

            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "Either the field: publisher_id or the relationship field: publishers can be specified.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );

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

            string nestedCreateOneBookWithReviews = @"mutation {
                    createbook(item: { title: ""My New Book"", publisher_id: 1234, reviews: [{ content: ""Good book"", book_id: 123}]}) {
                        id
                        title
                    }
                }";

            actual = await ExecuteGraphQLRequestAsync(
                query: nestedCreateOneBookWithReviews,
                queryName: createOneBookMutationName,
                isAuthenticated: true,
                variables: null,
                clientRoleHeader: "authenticated");

            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "The field: book_id cannot be present for entity: Review at level: 2.",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );

            string nestedCreateSameEntityAtGrandParentLevel = @"mutation {
                    createbook(item: { title: ""My New Book"",
                                       publishers:{
                                            name: ""New publisher"",
                                            books:[{ title: ""Book at level2"" }, { title: ""Another book at level2"" }]} }) {
                        id
                        title
                    }
                }";

            actual = await ExecuteGraphQLRequestAsync(
                query: nestedCreateSameEntityAtGrandParentLevel,
                queryName: createOneBookMutationName,
                isAuthenticated: true,
                variables: null,
                clientRoleHeader: "authenticated");

            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "Exception!!!",
                    statusCode: DataApiBuilderException.SubStatusCodes.BadRequest.ToString()
                );
        }
    }
}
