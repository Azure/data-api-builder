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
    public class MultipleCreateAuthorizationUnitTests : SqlTestBase
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
        /// Test to validate that a 'create one' mutation request can only execute successfully when the user, has create permission
        /// for all the entities involved in the mutation.
        /// </summary>
        [TestMethod]
        public async Task ValidateAuthZCheckOnEntitiesForCreateOneMutations()
        {
            string createBookMutationName = "createbook";
            string createOneBookMutation = @"mutation {
                    createbook(item: { title: ""Book #1"", publishers: { name: ""Publisher #1""}}) {
                        id
                        title
                    }
                }";

            // The anonymous role has create permissions on the Book entity but not on the Publisher entity.
            // Hence the request will fail during authorization check.
            await ValidateRequestIsUnauthorized(
                graphQLMutationName: createBookMutationName,
                graphQLMutation: createOneBookMutation,
                isAuthenticated: false,
                clientRoleHeader: "anonymous"
                );

            // The authenticates role has create permissions on both the Book and Publisher entities.
            // Hence the authorization checks will pass.
            await ValidateRequestIsAuthorized(
                graphQLMutationName: createBookMutationName,
                graphQLMutation: createOneBookMutation,
                isAuthenticated: true,
                clientRoleHeader: "authenticated"
                );
        }

        /// <summary>
        /// Test to validate that a 'create multiple' mutation request can only execute successfully when the user, has create permission
        /// for all the entities involved in the mutation.
        /// </summary>
        [TestMethod]
        public async Task ValidateAuthZCheckOnEntitiesForCreateMultipleMutations()
        {
            string createMultipleBooksMutationName = "createbooks";
            string createMultipleBookMutation = @"mutation {
                    createbooks(items: [{ title: ""Book #1"", publishers: { name: ""Publisher #1""}},
                                        { title: ""Book #2"", publishers: { name: ""Publisher #2""}}]) {
                        items{
                            id
                            title
                        }
                    }
                }";

            // The anonymous role has create permissions on the Book entity but not on the Publisher entity.
            // Hence the request will fail during authorization check.
            await ValidateRequestIsUnauthorized(
                graphQLMutationName: createMultipleBooksMutationName,
                graphQLMutation: createMultipleBookMutation,
                isAuthenticated: false,
                clientRoleHeader: "anonymous");

            // The authenticates role has create permissions on both the Book and Publisher entities.
            // Hence the authorization checks will pass.
            await ValidateRequestIsAuthorized(
                graphQLMutationName: createMultipleBooksMutationName,
                graphQLMutation: createMultipleBookMutation,
                isAuthenticated: true,
                clientRoleHeader: "authenticated",
                expectedResult: "Expected item argument in mutation arguments."
                );
        }

        /// <summary>
        /// Test to validate that a 'create one' mutation request can only execute successfully when the user, in addition to having
        /// create permission for all the entities involved in the create mutation, has the create permission for all the columns
        /// present for each entity in the mutation.
        /// If the user does not have any create permission on one or more column belonging to any of the entity in the
        /// multiple-create mutation, the request will fail during authorization check.
        /// </summary>
        [TestMethod]
        public async Task ValidateAuthZCheckOnColumnsForCreateOneMutations()
        {
            string createOneStockMutationName = "createStock";
            string createOneStockWithPiecesAvailable = @"mutation {
                                            createStock(
                                                item:
                                                  {
                                                    categoryid: 1,
                                                    pieceid: 2,
                                                    categoryName: ""xyz""
                                                    piecesAvailable: 0,
                                                    stocks_price:
                                                      {
                                                        is_wholesale_price: true
                                                        instant: ""1996-01-24""
                                                      }
                                                  }
                                             )
                                             {
                                                categoryid
                                                pieceid
                                             }
                                          }";

            // The 'test_role_with_excluded_fields_on_create' role does not have create permissions on
            // stocks.piecesAvailable field and hence the authorization check should fail.
            await ValidateRequestIsUnauthorized(
                graphQLMutationName: createOneStockMutationName,
                graphQLMutation: createOneStockWithPiecesAvailable,
                isAuthenticated: true,
                clientRoleHeader: "test_role_with_excluded_fields_on_create");

            // As soon as we remove the 'piecesAvailable' column from the request body,
            // the authorization check will pass.
            string nestedCreateOneStockWithoutPiecesAvailable = @"mutation {
                                            createStock(
                                                item:
                                                  {
                                                    categoryid: 1,
                                                    pieceid: 2,
                                                    categoryName: ""xyz"",
                                                    stocks_price:
                                                      {
                                                        is_wholesale_price: true
                                                        instant: ""1996-01-24""
                                                      }
                                                  }
                                             )
                                             {
                                                categoryid
                                                pieceid
                                             }
                                          }";

            // The 'test_role_with_excluded_fields_on_create' role does not have create permissions on
            // stocks.piecesAvailable field and hence the authorization check should fail.
            await ValidateRequestIsAuthorized(
                graphQLMutationName: createOneStockMutationName,
                graphQLMutation: nestedCreateOneStockWithoutPiecesAvailable,
                isAuthenticated: true,
                clientRoleHeader: "test_role_with_excluded_fields_on_create",
                expectedResult: "");
        }

        /// <summary>
        /// Test to validate that a 'create multiple' mutation request can only execute successfully when the user, in addition to having
        /// create permission for all the entities involved in the create mutation, has the create permission for all the columns
        /// present for each entity in the mutation.
        /// If the user does not have any create permission on one or more column belonging to any of the entity in the
        /// multiple-create mutation, the request will fail during authorization check.
        /// </summary>
        [TestMethod]
        public async Task ValidateAuthZCheckOnColumnsForCreateMultipleMutations()
        {
            string createMultipleStockMutationName = "createStocks";
            string createMultipleStocksWithPiecesAvailable = @"mutation {
                                            createStocks(
                                                items: [
                                                  {
                                                    categoryid: 1,
                                                    pieceid: 2,
                                                    categoryName: ""xyz""
                                                    piecesAvailable: 0,
                                                    stocks_price:
                                                      {
                                                        is_wholesale_price: true
                                                        instant: ""1996-01-24""
                                                      }
                                                  }
                                             ])
                                             {
                                                items
                                                {
                                                   categoryid
                                                   pieceid
                                                }
                                             }
                                          }";

            // The 'test_role_with_excluded_fields_on_create' role does not have create permissions on
            // stocks.piecesAvailable field and hence the authorization check should fail.
            await ValidateRequestIsUnauthorized(
                graphQLMutationName: createMultipleStockMutationName,
                graphQLMutation: createMultipleStocksWithPiecesAvailable,
                isAuthenticated: true,
                clientRoleHeader: "test_role_with_excluded_fields_on_create");

            // As soon as we remove the 'piecesAvailable' column from the request body,
            // the authorization check will pass.
            string createMultipleStocksWithoutPiecesAvailable = @"mutation {
                                            createStocks(
                                                items: [
                                                  {
                                                    categoryid: 1,
                                                    pieceid: 2,
                                                    categoryName: ""xyz""
                                                    piecesAvailable: 0,
                                                    stocks_price:
                                                      {
                                                        is_wholesale_price: true
                                                        instant: ""1996-01-24""
                                                      }
                                                  }
                                             ])
                                             {
                                                items
                                                {
                                                   categoryid
                                                   pieceid
                                                }
                                             }
                                          }";

            // The 'test_role_with_excluded_fields_on_create' role does not have create permissions on
            // stocks.piecesAvailable field and hence the authorization check should fail.
            await ValidateRequestIsAuthorized(
                graphQLMutationName: createMultipleStockMutationName,
                graphQLMutation: createMultipleStocksWithoutPiecesAvailable,
                isAuthenticated: true,
                clientRoleHeader: "test_role_with_excluded_fields_on_create",
                expectedResult: "");
        }

        /// <summary>
        /// Helper method to execute and validate response for negative GraphQL requests which expect an authorization failure
        /// as a result of their execution.
        /// </summary>
        /// <param name="graphQLMutationName">Name of the mutation.</param>
        /// <param name="graphQLMutation">Request body of the mutation.</param>
        /// <param name="isAuthenticated">Boolean indicating whether the request should be treated as authenticated or not.</param>
        /// <param name="clientRoleHeader">Value of X-MS-API-ROLE client role header.</param>
        private async Task ValidateRequestIsUnauthorized(
            string graphQLMutationName,
            string graphQLMutation,
            bool isAuthenticated = false,
            string clientRoleHeader = "anonymous")
        {
            
            JsonElement actual = await ExecuteGraphQLRequestAsync(
                query: graphQLMutation,
                queryName: graphQLMutationName,
                isAuthenticated: isAuthenticated,
                variables: null,
                clientRoleHeader: clientRoleHeader);

            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: "Unauthorized due to one or more fields in this mutation.",
                    statusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed.ToString()
                );
        }

        /// <summary>
        /// Helper method to execute and validate response for positive GraphQL requests which expect a successful execution
        /// against the database, passing all the Authorization checks en route.
        /// </summary>
        /// <param name="graphQLMutationName">Name of the mutation.</param>
        /// <param name="graphQLMutation">Request body of the mutation.</param>
        /// <param name="expectedResult">Expected result.</param>
        /// <param name="isAuthenticated">Boolean indicating whether the request should be treated as authenticated or not.</param>
        /// <param name="clientRoleHeader">Value of X-MS-API-ROLE client role header.</param>
        private async Task ValidateRequestIsAuthorized(
            string graphQLMutationName,
            string graphQLMutation,
            string expectedResult = "Value cannot be null",
            bool isAuthenticated = false,
            string clientRoleHeader = "anonymous")
        {

            JsonElement actual = await ExecuteGraphQLRequestAsync(
                query: graphQLMutation,
                queryName: graphQLMutationName,
                isAuthenticated: isAuthenticated,
                variables: null,
                clientRoleHeader: clientRoleHeader);

            SqlTestHelper.TestForErrorInGraphQLResponse(
                    actual.ToString(),
                    message: expectedResult
                );
        }
    }
}
