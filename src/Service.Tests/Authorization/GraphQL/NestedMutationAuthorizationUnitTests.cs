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
    public class NestedMutationAuthorizationUnitTests : SqlTestBase
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
        public async Task NestedCreateOnEntityWithoutCreatePermission()
        {
            string createBookMutationName = "createbook";
            string nestedCreateOneBook = @"mutation {
                    createbook(item: { title: ""My New Book"", publishers: { name: ""New publisher""}}) {
                        id
                        title
                    }
                }";

            // The anonymous role has create permissions on the Book entity but not on the Publisher entity.
            // Hence the request will fail during authorization check.
            await ValidateRequestIsUnauthorized(createBookMutationName, nestedCreateOneBook);

            string createBooksMutationName = "createbooks";
            string nestedCreateMultipleBook = @"mutation {
                    createbooks(items: [{ title: ""Book #1"", publishers: { name: ""Publisher #1""}},
                                        { title: ""Book #2"", publishers: { name: ""Publisher #2""}}]) {
                        items{
                            id
                            title
                        }
                    }
                }";

            await ValidateRequestIsUnauthorized(createBooksMutationName, nestedCreateMultipleBook);
        }

        private async Task ValidateRequestIsUnauthorized(string graphQLMutationName, string graphQLMutation)
        {
            
            JsonElement actual = await ExecuteGraphQLRequestAsync(
                query: graphQLMutation,
                queryName: graphQLMutationName,
                isAuthenticated: false,
                variables: null,
                clientRoleHeader: "anonymous");

            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "Unauthorized due to one or more fields in this mutation.",
                    statusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed.ToString()
                );
        }
    }
}
