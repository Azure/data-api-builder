// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.DataApiBuilder.Config.ObjectModel;
using Azure.DataApiBuilder.Core.Authorization;
using Azure.DataApiBuilder.Core.Configurations;
using Azure.DataApiBuilder.Service.Exceptions;
using Azure.DataApiBuilder.Service.Tests.SqlTests;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static Azure.DataApiBuilder.Core.AuthenticationHelpers.AppServiceAuthentication;

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
            // So, no errors are expected during authorization checks.
            // Hence, passing an empty string as the expected error message.
            await ValidateRequestIsAuthorized(
                graphQLMutationName: createBookMutationName,
                graphQLMutation: createOneBookMutation,
                isAuthenticated: true,
                clientRoleHeader: "authenticated",
                expectedErrorMessage: ""
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
            // So, no errors are expected during authorization checks.
            // Hence, passing an empty string as the expected error message.
            await ValidateRequestIsAuthorized(
                graphQLMutationName: createMultipleBooksMutationName,
                graphQLMutation: createMultipleBookMutation,
                isAuthenticated: true,
                clientRoleHeader: "authenticated",
                expectedErrorMessage: ""
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
            // Hence, passing an empty string as the expected error message.
            await ValidateRequestIsAuthorized(
                graphQLMutationName: createOneStockMutationName,
                graphQLMutation: createOneStockWithoutPiecesAvailable,
                isAuthenticated: true,
                clientRoleHeader: "test_role_with_excluded_fields_on_create",
                expectedErrorMessage: "");

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
            // Hence, passing an empty string as the expected error message.
            await ValidateRequestIsAuthorized(
                graphQLMutationName: createOneStockMutationName,
                graphQLMutation: createOneStocksPriceWithoutPiecesAvailable,
                isAuthenticated: true,
                clientRoleHeader: "test_role_with_excluded_fields_on_create",
                expectedErrorMessage: "");
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
            // Hence, passing an empty string as the expected error message.
            await ValidateRequestIsAuthorized(
                graphQLMutationName: createMultipleStockMutationName,
                graphQLMutation: createMultipleStocksWithoutPiecesAvailable,
                isAuthenticated: true,
                clientRoleHeader: "test_role_with_excluded_fields_on_create",
                expectedErrorMessage: "");
        }

        #endregion

        #region Multiple create linking table authorization tests

        private const string LINKING_TABLE_ENTITY_NAME = "BookAuthorLink";
        private const string ALTERNATIVE_LINKING_TABLE_ENTITY_NAME = "BookAuthorLinkAlternative";
        private const string LINKING_TABLE_CONFIG = "linking-table-authorization-config.json";
        private const string ROLE_WITH_EXCLUDED_LINKING_FIELD = "test_role_with_excluded_linking_field_on_create";
        private const string ROLE_WITH_EXCLUDED_LINKING_FOREIGN_KEY = "test_role_with_excluded_linking_foreign_key_on_create";
        private const string ROLE_WITH_LINKING_TABLE_CREATE = "test_role_with_linking_table_create";
        private const string ROLE_WITH_LINKING_TABLE_CREATE_POLICY = "test_role_with_linking_table_create_policy";
        private const string ROLE_WITH_ALTERNATIVE_UNRESTRICTED_CREATE = "test_role_with_alternative_unrestricted_linking_table_create";
        private const string ROLE_WITH_ALTERNATIVE_CREATE_POLICIES = "test_role_with_alternative_linking_table_create_policies";
        private const string UNAUTHORIZED_FIELDS_MESSAGE = "Unauthorized due to one or more fields in this mutation.";

        /// <summary>
        /// Validates that when the linking table of a many-to-many relationship is exposed as an entity, the linking table row
        /// created by a multiple create mutation needs the role's create permission on that entity for every column written:
        /// any linking table field the request supplies, and the foreign key columns derived from the related records.
        /// Each role may create Book and Author, so the request is rejected only because of the linking table row,
        /// and without leaving any record in the database.
        /// </summary>
        [DataTestMethod]
        [DataRow(AuthorizationResolver.ROLE_AUTHENTICATED, true, DisplayName = "Role without create permission on the linking table entity supplies the field")]
        [DataRow(AuthorizationResolver.ROLE_AUTHENTICATED, false, DisplayName = "Role without create permission on the linking table entity omits the field")]
        [DataRow(ROLE_WITH_EXCLUDED_LINKING_FIELD, true, DisplayName = "Role whose create permission on the linking table entity excludes the field supplies it")]
        [DataRow(ROLE_WITH_EXCLUDED_LINKING_FOREIGN_KEY, false, DisplayName = "Role whose create permission on the linking table entity excludes a foreign key column")]
        public async Task ValidateAuthZCheckOnLinkingTableWritesForMultipleCreateMutations(string role, bool supplyLinkingField)
        {
            string title = $"Linking table authz book ({role}, {supplyLinkingField})";
            string authorName = $"Linking table authz author ({role}, {supplyLinkingField})";
            string mutation = CreateBookWithAuthorMutation(title, authorName, supplyLinkingField ? "99.5" : null);

            await ValidateLinkingTableWriteIsRejected("createbook", mutation, variables: null, role, title, authorName);
        }

        /// <summary>
        /// Validates the authorization of a linking table field supplied through other input shapes: a many type mutation,
        /// the inverse relationship, a deeper nesting level, a variable, or an explicit null value.
        /// </summary>
        [DataTestMethod]
        [DataRow("ManyType", DisplayName = "Many type multiple create mutation")]
        [DataRow("InverseRelationship", DisplayName = "Linking table field supplied through the inverse relationship")]
        [DataRow("DeeperNesting", DisplayName = "Linking table field supplied at a deeper nesting level")]
        [DataRow("Variable", DisplayName = "Linking table field supplied through a variable")]
        [DataRow("ExplicitNull", DisplayName = "Linking table field explicitly set to null")]
        public async Task ValidateAuthZCheckOnLinkingTableFieldsForAlternateMultipleCreateInputs(string inputShape)
        {
            string title = $"Linking table authz book ({inputShape})";
            string authorName = $"Linking table authz author ({inputShape})";
            string publisherName = $"Linking table authz publisher ({inputShape})";
            Dictionary<string, object> variables = null;
            string mutationName;
            string mutation;
            switch (inputShape)
            {
                case "ManyType":
                    mutationName = "createbooks";
                    mutation = @"mutation {
                        createbooks(items: [{
                            title: """ + title + @""",
                            publisher_id: 1234,
                            authors: [{ name: """ + authorName + @""", birthdate: ""2000-01-01"", royalty_percentage: 99.5 }] }])
                        {
                            items { id }
                        }
                    }";
                    break;
                case "InverseRelationship":
                    mutationName = "createAuthor";
                    mutation = @"mutation {
                        createAuthor(item: {
                            name: """ + authorName + @""",
                            birthdate: ""2000-01-01"",
                            books: [{ title: """ + title + @""", publisher_id: 1234, royalty_percentage: 99.5 }] })
                        {
                            id
                        }
                    }";
                    break;
                case "DeeperNesting":
                    mutationName = "createPublisher";
                    mutation = @"mutation {
                        createPublisher(item: {
                            name: """ + publisherName + @""",
                            books: [{
                                title: """ + title + @""",
                                authors: [{ name: """ + authorName + @""", birthdate: ""2000-01-01"", royalty_percentage: 99.5 }] }] })
                        {
                            id
                        }
                    }";
                    break;
                case "Variable":
                    mutationName = "createbook";
                    mutation = @"mutation ($royalty: Float) {
                        createbook(item: {
                            title: """ + title + @""",
                            publisher_id: 1234,
                            authors: [{ name: """ + authorName + @""", birthdate: ""2000-01-01"", royalty_percentage: $royalty }] })
                        {
                            id
                        }
                    }";
                    variables = new() { { "royalty", 99.5 } };
                    break;
                case "ExplicitNull":
                    mutationName = "createbook";
                    mutation = @"mutation {
                        createbook(item: {
                            title: """ + title + @""",
                            publisher_id: 1234,
                            authors: [{ name: """ + authorName + @""", birthdate: ""2000-01-01"", royalty_percentage: null }] })
                        {
                            id
                        }
                    }";
                    break;
                default:
                    throw new ArgumentException($"Unexpected input shape: {inputShape}", nameof(inputShape));
            }

            await ValidateLinkingTableWriteIsRejected(mutationName, mutation, variables, AuthorizationResolver.ROLE_AUTHENTICATED, title, authorName, publisherName);
        }

        /// <summary>
        /// Validates that a multiple create mutation writes the linking table row as permitted by an entity exposing the linking
        /// table: a supplied field is stored, an omitted field gets its database default, and the entity's create policy applies.
        /// When several entities expose the linking table, the row is written if any of them allows it, as a direct create would.
        /// </summary>
        [DataTestMethod]
        [DataRow(ROLE_WITH_LINKING_TABLE_CREATE, "99.5", @"[{""royalty_percentage"":99.5}]", DisplayName = "Role permitted to create the field supplies it")]
        [DataRow(ROLE_WITH_EXCLUDED_LINKING_FIELD, null, @"[{""royalty_percentage"":0.0}]", DisplayName = "Role with the field excluded omits it")]
        [DataRow(ROLE_WITH_LINKING_TABLE_CREATE_POLICY, "25.0", @"[{""royalty_percentage"":25.0}]", DisplayName = "Role with a create policy supplies a value the policy allows")]
        [DataRow(ROLE_WITH_ALTERNATIVE_UNRESTRICTED_CREATE, null, @"[{""royalty_percentage"":0.0}]", DisplayName = "Field referenced by one entity's create policy is omitted and another entity has no policy")]
        [DataRow(ROLE_WITH_ALTERNATIVE_CREATE_POLICIES, "60.0", @"[{""royalty_percentage"":60.0}]", DisplayName = "Value rejected by one entity's create policy is allowed by another entity's policy")]
        public async Task ValidateAuthorizedLinkingTableWritesForMultipleCreateMutations(string role, string royaltyPercentage, string expectedLinkingRows)
        {
            string title = $"Linking table authz allowed book ({role}, {royaltyPercentage})";
            string authorName = $"Linking table authz allowed author ({role}, {royaltyPercentage})";

            JsonElement response;
            using (TestServer server = CreateServerWithLinkingTableEntity())
            {
                response = await ExecuteLinkingTableMutationAsync(server, "createbook", CreateBookWithAuthorMutation(title, authorName, royaltyPercentage), variables: null, role);
            }

            Assert.AreEqual(JsonValueKind.Object, response.ValueKind, $"Expected the mutation to succeed. Response: {response}");
            SqlTestHelper.PerformTestEqualJsonStrings(expectedLinkingRows, await GetLinkingTableRowsAsync(title, authorName));
        }

        /// <summary>
        /// Validates that whether a linking table row is allowed does not depend on the order in which the entities exposing the
        /// linking table are configured: a value rejected by one entity's create policy is written as allowed by the other entity.
        /// </summary>
        [DataTestMethod]
        [DataRow(false, DisplayName = "Entity whose create policy rejects the value is configured first")]
        [DataRow(true, DisplayName = "Entity without a create policy is configured first")]
        public async Task ValidateLinkingTableWriteDoesNotDependOnExposingEntityOrder(bool isAlternativeEntityFirst)
        {
            string title = $"Linking table authz order book ({isAlternativeEntityFirst})";
            string authorName = $"Linking table authz order author ({isAlternativeEntityFirst})";

            JsonElement response;
            using (TestServer server = CreateServerWithLinkingTableEntity(isAlternativeEntityFirst))
            {
                response = await ExecuteLinkingTableMutationAsync(server, "createbook", CreateBookWithAuthorMutation(title, authorName, "99.5"), variables: null, ROLE_WITH_ALTERNATIVE_UNRESTRICTED_CREATE);
            }

            Assert.AreEqual(JsonValueKind.Object, response.ValueKind, $"Expected the mutation to succeed. Response: {response}");
            SqlTestHelper.PerformTestEqualJsonStrings(@"[{""royalty_percentage"":99.5}]", await GetLinkingTableRowsAsync(title, authorName));
        }

        /// <summary>
        /// Validates that the create policy of the entity exposing the linking table applies to the linking table row created by
        /// a multiple create mutation, as it does to a direct create of that entity, and that a violation rolls back the mutation.
        /// When several entities expose the linking table, the mutation is rejected only if none of them allows the row.
        /// </summary>
        [DataTestMethod]
        [DataRow(ROLE_WITH_LINKING_TABLE_CREATE_POLICY, "99.5", "Could not insert row with given values for entity: BookAuthorLink", DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure, DisplayName = "Supplied value violates the create policy")]
        [DataRow(ROLE_WITH_LINKING_TABLE_CREATE_POLICY, null, "One or more fields referenced by the database policy are not present in the request body.", DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed, DisplayName = "Field referenced by the create policy is omitted")]
        [DataRow(ROLE_WITH_ALTERNATIVE_CREATE_POLICIES, "99.5", "Could not insert row with given values for entity: BookAuthorLink", DataApiBuilderException.SubStatusCodes.DatabasePolicyFailure, DisplayName = "Supplied value violates the create policies of all exposing entities")]
        [DataRow(ROLE_WITH_ALTERNATIVE_CREATE_POLICIES, null, "One or more fields referenced by the database policy are not present in the request body.", DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed, DisplayName = "Field referenced by the create policies of all exposing entities is omitted")]
        public async Task ValidateLinkingTableEntityCreatePolicyForMultipleCreateMutations(string role, string royaltyPercentage, string expectedMessage, DataApiBuilderException.SubStatusCodes expectedStatusCode)
        {
            string title = $"Linking table authz policy book ({role}, {royaltyPercentage})";
            string authorName = $"Linking table authz policy author ({role}, {royaltyPercentage})";

            await ValidateLinkingTableWriteIsRejected(
                "createbook",
                CreateBookWithAuthorMutation(title, authorName, royaltyPercentage),
                variables: null,
                role,
                title,
                authorName,
                expectedMessage: expectedMessage,
                expectedStatusCode: expectedStatusCode);
        }

        /// <summary>
        /// Validates that when no entity exposes the linking table, a multiple create mutation keeps writing the linking table row
        /// for a role that may create both related entities: a supplied field is stored and an omitted field gets its database default.
        /// </summary>
        [DataTestMethod]
        [DataRow("99.5", @"[{""royalty_percentage"":99.5}]", DisplayName = "Linking table field supplied")]
        [DataRow(null, @"[{""royalty_percentage"":0.0}]", DisplayName = "Linking table field omitted")]
        public async Task ValidateLinkingTableWritesWithoutLinkingTableEntityForMultipleCreateMutations(string royaltyPercentage, string expectedLinkingRows)
        {
            string title = $"Linking table without entity book ({royaltyPercentage})";
            string authorName = $"Linking table without entity author ({royaltyPercentage})";

            JsonElement response = await ExecuteGraphQLRequestAsync(
                query: CreateBookWithAuthorMutation(title, authorName, royaltyPercentage),
                queryName: "createbook",
                isAuthenticated: true,
                clientRoleHeader: AuthorizationResolver.ROLE_AUTHENTICATED);

            Assert.AreEqual(JsonValueKind.Object, response.ValueKind, $"Expected the mutation to succeed. Response: {response}");
            SqlTestHelper.PerformTestEqualJsonStrings(expectedLinkingRows, await GetLinkingTableRowsAsync(title, authorName));
        }

        private static string CreateBookWithAuthorMutation(string title, string authorName, string royaltyPercentage)
        {
            string linkingField = royaltyPercentage is null ? string.Empty : $", royalty_percentage: {royaltyPercentage}";
            return @"mutation {
                    createbook(item: {
                        title: """ + title + @""",
                        publisher_id: 1234,
                        authors: [{ name: """ + authorName + @""", birthdate: ""2000-01-01""" + linkingField + @" }] })
                    {
                        id
                    }
                }";
        }

        private static Task<string> GetLinkingTableRowsAsync(string title, string authorName)
        {
            return GetDatabaseResultAsync(@"
                SELECT [link].[royalty_percentage]
                FROM [dbo].[book_author_link] AS [link]
                INNER JOIN [dbo].[books] AS [book] ON [book].[id] = [link].[book_id]
                INNER JOIN [dbo].[authors] AS [author] ON [author].[id] = [link].[author_id]
                WHERE [book].[title] = '" + title + @"' AND [author].[name] = '" + authorName + @"'
                FOR JSON PATH, INCLUDE_NULL_VALUES");
        }

        /// <summary>
        /// Executes a multiple create mutation whose linking table row the role may not create, and validates that it fails
        /// with the expected error without leaving any of the records referenced in the mutation in the database.
        /// </summary>
        private static async Task ValidateLinkingTableWriteIsRejected(
            string mutationName,
            string mutation,
            Dictionary<string, object> variables,
            string role,
            string title,
            string authorName,
            string publisherName = "",
            string expectedMessage = UNAUTHORIZED_FIELDS_MESSAGE,
            DataApiBuilderException.SubStatusCodes expectedStatusCode = DataApiBuilderException.SubStatusCodes.AuthorizationCheckFailed)
        {
            JsonElement response;
            using (TestServer server = CreateServerWithLinkingTableEntity())
            {
                response = await ExecuteLinkingTableMutationAsync(server, mutationName, mutation, variables, role);
            }

            string writtenRecords = await GetDatabaseResultAsync(@"
                SELECT
                    (SELECT COUNT(*) FROM [dbo].[publishers] WHERE [name] = '" + publisherName + @"') AS [publishers],
                    (SELECT COUNT(*) FROM [dbo].[books] WHERE [title] = '" + title + @"') AS [books],
                    (SELECT COUNT(*) FROM [dbo].[authors] WHERE [name] = '" + authorName + @"') AS [authors],
                    (SELECT COUNT(*) FROM [dbo].[book_author_link] AS [link]
                        INNER JOIN [dbo].[authors] AS [author] ON [author].[id] = [link].[author_id]
                        WHERE [author].[name] = '" + authorName + @"') AS [links]
                FOR JSON PATH, WITHOUT_ARRAY_WRAPPER");

            Assert.IsTrue(
                SqlTestHelper.JsonStringsDeepEqual(@"{""publishers"":0,""books"":0,""authors"":0,""links"":0}", writtenRecords),
                $"The rejected mutation left records: {writtenRecords}. Response: {response}");
            SqlTestHelper.TestForErrorInGraphQLResponse(
                response.ToString(),
                message: expectedMessage,
                statusCode: expectedStatusCode.ToString());
        }

        /// <summary>
        /// Starts an engine whose configuration also exposes the linking table book_author_link, used by the many-to-many
        /// relationship between Book and Author, as the BookAuthorLink entity with role specific create permissions.
        /// The entity maps book_id to bookId, so that its exposed names differ from the linking table's column names.
        /// The BookAuthorLinkAlternative entity exposes the same table with other create policies for some roles.
        /// </summary>
        /// <param name="isAlternativeEntityFirst">Whether BookAuthorLinkAlternative is configured before BookAuthorLink.</param>
        private static TestServer CreateServerWithLinkingTableEntity(bool isAlternativeEntityFirst = false)
        {
            TestHelper.SetupDatabaseEnvironment(TestCategory.MSSQL);
            RuntimeConfig config = SqlTestHelper.SetupRuntimeConfig();

            EntityAction read = new(EntityActionOperation.Read, Fields: null, Policy: new());
            EntityAction create = new(EntityActionOperation.Create, Fields: null, Policy: new());
            EntityAction createWithoutRoyaltyPercentage = new(
                EntityActionOperation.Create,
                Fields: new(Exclude: new HashSet<string> { "royalty_percentage" }),
                Policy: new());
            EntityAction createWithoutAuthorId = new(
                EntityActionOperation.Create,
                Fields: new(Exclude: new HashSet<string> { "author_id" }),
                Policy: new());
            EntityAction createWithPolicy = new(
                EntityActionOperation.Create,
                Fields: null,
                Policy: new(Database: "@item.royalty_percentage le 50"));
            EntityAction createWithLaxerPolicy = new(
                EntityActionOperation.Create,
                Fields: null,
                Policy: new(Database: "@item.royalty_percentage le 75"));

            Dictionary<string, Entity> entities = new(config.Entities);
            foreach (string relatedEntityName in new[] { "Book", "Author" })
            {
                Entity relatedEntity = entities[relatedEntityName];
                entities[relatedEntityName] = relatedEntity with
                {
                    Permissions = relatedEntity.Permissions.Concat(new[]
                    {
                        new EntityPermission(ROLE_WITH_EXCLUDED_LINKING_FIELD, new[] { read, create }),
                        new EntityPermission(ROLE_WITH_EXCLUDED_LINKING_FOREIGN_KEY, new[] { read, create }),
                        new EntityPermission(ROLE_WITH_LINKING_TABLE_CREATE, new[] { read, create }),
                        new EntityPermission(ROLE_WITH_LINKING_TABLE_CREATE_POLICY, new[] { read, create }),
                        new EntityPermission(ROLE_WITH_ALTERNATIVE_UNRESTRICTED_CREATE, new[] { read, create }),
                        new EntityPermission(ROLE_WITH_ALTERNATIVE_CREATE_POLICIES, new[] { read, create })
                    }).ToArray()
                };
            }

            Entity linkingTableEntity = new(
                Source: new("book_author_link", EntitySourceType.Table, Parameters: null, KeyFields: null),
                GraphQL: new(Singular: LINKING_TABLE_ENTITY_NAME, Plural: LINKING_TABLE_ENTITY_NAME + "s"),
                Fields: null,
                Rest: new(Enabled: true),
                Permissions: new[]
                {
                    new EntityPermission(AuthorizationResolver.ROLE_AUTHENTICATED, new[] { read }),
                    new EntityPermission(ROLE_WITH_EXCLUDED_LINKING_FIELD, new[] { read, createWithoutRoyaltyPercentage }),
                    new EntityPermission(ROLE_WITH_EXCLUDED_LINKING_FOREIGN_KEY, new[] { read, createWithoutAuthorId }),
                    new EntityPermission(ROLE_WITH_LINKING_TABLE_CREATE, new[] { read, create }),
                    new EntityPermission(ROLE_WITH_LINKING_TABLE_CREATE_POLICY, new[] { read, createWithPolicy }),
                    new EntityPermission(ROLE_WITH_ALTERNATIVE_UNRESTRICTED_CREATE, new[] { read, createWithPolicy }),
                    new EntityPermission(ROLE_WITH_ALTERNATIVE_CREATE_POLICIES, new[] { read, createWithPolicy })
                },
                Mappings: new() { { "book_id", "bookId" } },
                Relationships: null);

            Entity alternativeLinkingTableEntity = new(
                Source: new("book_author_link", EntitySourceType.Table, Parameters: null, KeyFields: null),
                GraphQL: new(Singular: ALTERNATIVE_LINKING_TABLE_ENTITY_NAME, Plural: ALTERNATIVE_LINKING_TABLE_ENTITY_NAME + "s"),
                Fields: null,
                Rest: new(Enabled: true),
                Permissions: new[]
                {
                    new EntityPermission(ROLE_WITH_ALTERNATIVE_UNRESTRICTED_CREATE, new[] { read, create }),
                    new EntityPermission(ROLE_WITH_ALTERNATIVE_CREATE_POLICIES, new[] { read, createWithLaxerPolicy })
                },
                Mappings: null,
                Relationships: null);

            if (isAlternativeEntityFirst)
            {
                entities.Add(ALTERNATIVE_LINKING_TABLE_ENTITY_NAME, alternativeLinkingTableEntity);
                entities.Add(LINKING_TABLE_ENTITY_NAME, linkingTableEntity);
            }
            else
            {
                entities.Add(LINKING_TABLE_ENTITY_NAME, linkingTableEntity);
                entities.Add(ALTERNATIVE_LINKING_TABLE_ENTITY_NAME, alternativeLinkingTableEntity);
            }

            File.WriteAllText(LINKING_TABLE_CONFIG, (config with { Entities = new(entities) }).ToJson());
            return new(Program.CreateWebHostBuilder(new[] { $"--ConfigFileName={LINKING_TABLE_CONFIG}" }));
        }

        private static async Task<JsonElement> ExecuteLinkingTableMutationAsync(
            TestServer server,
            string mutationName,
            string mutation,
            Dictionary<string, object> variables,
            string role)
        {
            using HttpClient client = server.CreateClient();
            string authToken = AuthTestHelper.CreateAppServiceEasyAuthToken(
                roleClaimType: AuthenticationOptions.ROLE_CLAIM_TYPE,
                additionalClaims: new[] { new AppServiceClaim { Typ = AuthenticationOptions.ROLE_CLAIM_TYPE, Val = role } });

            return await GraphQLRequestExecutor.PostGraphQLRequestAsync(
                client,
                server.Services.GetRequiredService<RuntimeConfigProvider>(),
                mutationName,
                mutation,
                variables,
                authToken,
                clientRoleHeader: role);
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
        /// <param name="expectedErrorMessage">Expected error message.</param>
        /// <param name="isAuthenticated">Boolean indicating whether the request should be treated as authenticated or not.</param>
        /// <param name="clientRoleHeader">Value of X-MS-API-ROLE client role header.</param>
        private async Task ValidateRequestIsAuthorized(
            string graphQLMutationName,
            string graphQLMutation,
            string expectedErrorMessage = "Value cannot be null",
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
                    message: expectedErrorMessage
                );
        }

        #endregion
    }
}
