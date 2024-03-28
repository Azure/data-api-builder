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
    public class CreateMutationAuthorizationTests : SqlTestBase
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

        #region Point create mutation tests

        /// <summary>
        /// Test to validate that a 'create one' point mutation request will fail if the user does not have create permission on the
        /// top-level (the only) entity involved in the mutation.
        /// </summary>
        [TestMethod]
        public async Task ValidateAuthZCheckOnEntitiesForCreateOnePointMutations()
        {
            string createPublisherMutationName = "createPublisher";
            string createOnePublisherMutation = @"mutation{
                                         createPublisher(item: {name: ""Publisher #1""})
                                                 {
                                                     id
                                                     name
                                                 }
                                         }";

            // The anonymous role does not have create permissions on the Publisher entity.
            // Hence the request will fail during authorization check.
            await ValidateRequestIsUnauthorized(
                graphQLMutationName: createPublisherMutationName,
                graphQLMutation: createOnePublisherMutation,
                expectedExceptionMessage: "The current user is not authorized to access this resource.",
                isAuthenticated: false,
                clientRoleHeader: "anonymous"
                );
        }

        /// <summary>
        /// Test to validate that a 'create one' point mutation will fail the AuthZ checks if the user does not have create permission
        /// on one more columns belonging to the entity in the mutation.
        /// </summary>
        [TestMethod]
        public async Task ValidateAuthZCheckOnColumnsForCreateOnePointMutations()
        {
            string createOneStockMutationName = "createStock";
            string createOneStockWithPiecesAvailable = @"mutation {
                                     createStock(
                                         item:
                                           {
                                             categoryid: 1,
                                             pieceid: 2,
                                             categoryName: ""xyz""
                                             piecesAvailable: 0
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
                expectedExceptionMessage: "Unauthorized due to one or more fields in this mutation.",
                expectedExceptionStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed.ToString(),
                isAuthenticated: true,
                clientRoleHeader: "test_role_with_excluded_fields_on_create");
        }

        #endregion

        #region Multiple create mutation tests
        /// <summary>
        /// Test to validate that a 'create one' mutation request can only execute successfully when the user, has create permission
        /// for all the entities involved in the mutation.
        /// </summary>
        [TestMethod]
        [Ignore]
        public async Task ValidateAuthZCheckOnEntitiesForCreateOneMultipleMutations()
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
                expectedExceptionMessage: "Unauthorized due to one or more fields in this mutation.",
                expectedExceptionStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed.ToString(),
                isAuthenticated: false,
                clientRoleHeader: "anonymous"
                );

            // The authenticated role has create permissions on both the Book and Publisher entities.
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
        [Ignore]
        public async Task ValidateAuthZCheckOnEntitiesForCreateMultipleMutations()
        {
            string createMultipleBooksMutationName = "createbooks";
            string createMultipleBookMutation = @"mutation {
                    createbooks(items: [{ title: ""Book #1"", publisher_id: 1234 },
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
                expectedExceptionMessage: "Unauthorized due to one or more fields in this mutation.",
                expectedExceptionStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed.ToString(),
                isAuthenticated: false,
                clientRoleHeader: "anonymous");

            // The authenticated role has create permissions on both the Book and Publisher entities.
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
        [Ignore]
        public async Task ValidateAuthZCheckOnColumnsForCreateOneMultipleMutations()
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
                expectedExceptionMessage: "Unauthorized due to one or more fields in this mutation.",
                expectedExceptionStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed.ToString(),
                isAuthenticated: true,
                clientRoleHeader: "test_role_with_excluded_fields_on_create");

            // As soon as we remove the 'piecesAvailable' column from the request body,
            // the authorization check will pass.
            string createOneStockWithoutPiecesAvailable = @"mutation {
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

            // Since the field stocks.piecesAvailable is not included in the mutation,
            // the authorization check should pass.
            await ValidateRequestIsAuthorized(
                graphQLMutationName: createOneStockMutationName,
                graphQLMutation: createOneStockWithoutPiecesAvailable,
                isAuthenticated: true,
                clientRoleHeader: "test_role_with_excluded_fields_on_create",
                expectedResult: "");

            // Executing a similar mutation request but with stocks_price as top-level entity.
            // This validates that the recursive logic to do authorization on fields belonging to related entities
            // work as expected.

            string createOneStockPriceMutationName = "createstocks_price";
            string createOneStocksPriceWithPiecesAvailable = @"mutation {
                                            createstocks_price(
                                                item:
                                                  {
                                                    is_wholesale_price: true,
                                                    instant: ""1996-01-24"",
                                                    price: 49.6,
                                                    Stock:
                                                      {
                                                        categoryid: 1,
                                                        pieceid: 2,
                                                        categoryName: ""xyz""
                                                        piecesAvailable: 0,
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
                graphQLMutationName: createOneStockPriceMutationName,
                graphQLMutation: createOneStocksPriceWithPiecesAvailable,
                expectedExceptionMessage: "Unauthorized due to one or more fields in this mutation.",
                expectedExceptionStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed.ToString(),
                isAuthenticated: true,
                clientRoleHeader: "test_role_with_excluded_fields_on_create");

            string createOneStocksPriceWithoutPiecesAvailable = @"mutation {
                                            createstocks_price(
                                                item:
                                                  {
                                                    is_wholesale_price: true,
                                                    instant: ""1996-01-24"",
                                                    price: 49.6,
                                                    Stock:
                                                      {
                                                        categoryid: 1,
                                                        pieceid: 2,
                                                        categoryName: ""xyz""
                                                      }
                                                  }
                                             )
                                             {
                                                categoryid
                                                pieceid
                                             }
                                          }";

            // Since the field stocks.piecesAvailable is not included in the mutation,
            // the authorization check should pass.
            await ValidateRequestIsAuthorized(
                graphQLMutationName: createOneStockMutationName,
                graphQLMutation: createOneStocksPriceWithoutPiecesAvailable,
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
        [Ignore]
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
                expectedExceptionMessage: "Unauthorized due to one or more fields in this mutation.",
                expectedExceptionStatusCode: DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed.ToString(),
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

            // Since the field stocks.piecesAvailable is not included in the mutation,
            // the authorization check should pass.
            await ValidateRequestIsAuthorized(
                graphQLMutationName: createMultipleStockMutationName,
                graphQLMutation: createMultipleStocksWithoutPiecesAvailable,
                isAuthenticated: true,
                clientRoleHeader: "test_role_with_excluded_fields_on_create",
                expectedResult: "");
        }

        #endregion

        #region Test helpers
        /// <summary>
        /// Helper method to execute and validate response for negative GraphQL requests which expect an authorization failure
        /// as a result of their execution.
        /// </summary>
        /// <param name="graphQLMutationName">Name of the mutation.</param>
        /// <param name="graphQLMutation">Request body of the mutation.</param>
        /// <param name="expectedExceptionMessage">Expected exception message.</param>
        /// <param name="isAuthenticated">Boolean indicating whether the request should be treated as authenticated or not.</param>
        /// <param name="clientRoleHeader">Value of X-MS-API-ROLE client role header.</param>
        private async Task ValidateRequestIsUnauthorized(
            string graphQLMutationName,
            string graphQLMutation,
            string expectedExceptionMessage,
            string expectedExceptionStatusCode = null,
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
                    message: expectedExceptionMessage,
                    statusCode: expectedExceptionStatusCode
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

        #endregion
    }
}
